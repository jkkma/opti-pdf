using ImageMagick;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace opti_pdf
{
    internal sealed class OptimizationResult
    {
        public long SizeBytes { get; set; }

        public bool WithinLimit { get; set; }
    }

    /// <summary>
    /// Builds a size-constrained PDF from a list of pages.
    ///
    /// Two ideas drive the design. First, resolution is expressed relative to
    /// the output page, not to the DPI recorded in the source metadata: that
    /// field is absent or wrong on most images, and keying off it meant
    /// downscaling silently never happened for anything tagged 72 DPI.
    /// Second, the byte budget is split across pages up front, so one large
    /// photo is squeezed instead of dragging every other page down with it.
    /// </summary>
    internal static class PdfOptimizer
    {
        public const long SizeLimitBytes = 5L * 1024 * 1024;
        public const int MinQuality = 40;
        public const int MaxQuality = 80;
        public const int MinDpi = 150;
        public const int MaxDpi = 300;

        private const int MaxParallelism = 4;
        private const int QualityStep = 8;
        private const int MaxBudgetRounds = 3;
        private const int MaxDownscaleSteps = 4;
        private const double DownscaleStep = 0.8;
        private const long MinPageBudgetBytes = 16L * 1024;

        // Target paper is 216 x 330 mm (Oficio / Folio) -- the size the documents
        // this app handles are printed on. Pages are normalised to fit that box
        // via the density tag, so a mixed batch produces consistent page sizes
        // instead of pages measured in feet.
        private const double MillimetresPerInch = 25.4;
        private const double PageLongEdgeMm = 330.0;
        private const double PageShortEdgeMm = 216.0;
        private const double PageLongEdgeInches = PageLongEdgeMm / MillimetresPerInch;
        private const double PageShortEdgeInches = PageShortEdgeMm / MillimetresPerInch;

        // Progress bands per round. They never overlap, so the bar only ever
        // moves forward even though a job may need several rounds.
        private static readonly int[] ProgressBands = { 0, 55, 75, 88, 96 };

        /// <summary>Largest page the pipeline will ever produce, in pixels.</summary>
        private static readonly int MaxPageLongEdgePixels = (int)(MaxDpi * PageLongEdgeInches);
        private static readonly int MaxPageShortEdgePixels = (int)(MaxDpi * PageShortEdgeInches);

        // Q16 RGB is 6 bytes per pixel. A deliberate over-estimate for a
        // greyscale page, which is the safe direction: it yields a more
        // generous ceiling rather than a tighter one.
        private const double CacheBytesPerPixel = 6.0;

        public static OptimizationResult Create(
            IReadOnlyList<PageSource> pages,
            string outputPath,
            IProgress<int> progress,
            IProgress<string> detail,
            CancellationToken cancellationToken)
        {
            if (pages == null || pages.Count == 0)
                throw new InvalidOperationException("No hay imágenes para procesar.");

            var tempBase = Path.Combine(Path.GetTempPath(), "opti_pdf_attempt_" + Guid.NewGuid().ToString("N"));
            var temps = new List<string>();
            string bestTemp = null;
            string promoted = null;
            long bestSize = long.MaxValue;

            // Bound ImageMagick's resident pixel cache for the whole job.
            // Beyond this it spills to disk instead of growing without limit:
            // assembly holds every page decoded at once, which for a long
            // document is far more than a machine wants resident.
            var previousMemoryLimit = ResourceLimits.Memory;
            var ceiling = PixelCacheCeilingBytes();

            // Only ever lower it. An administrator's policy limit cannot be
            // raised from here, and pretending otherwise would make the
            // restore below a no-op too.
            if (ceiling < previousMemoryLimit)
                ResourceLimits.Memory = ceiling;

            try
            {
                progress?.Report(0);
                cancellationToken.ThrowIfCancellationRequested();

                var weights = MeasureWeights(pages, cancellationToken);
                var factor = 0.0;

                for (var round = 0; round <= MaxBudgetRounds; round++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Round 0 is the gentle pass: best quality, resolution
                    // capped to the page. Most jobs finish here in one pass.
                    var budgets = round == 0
                        ? Enumerable.Repeat(long.MaxValue, pages.Count).ToArray()
                        : AllocateBudgets(weights, (long)(SizeLimitBytes * factor));

                    var temp = tempBase + "_r" + round + ".pdf";
                    temps.Add(temp);

                    var size = BuildPdf(pages, budgets, temp, round, progress, detail, cancellationToken);

                    if (size > 0 && size < bestSize)
                    {
                        bestTemp = temp;
                        bestSize = size;
                    }

                    if (bestSize <= SizeLimitBytes)
                        break;

                    // Correct from what the last round actually measured
                    // rather than stepping down by a fixed guess.
                    factor = round == 0
                        ? 0.90
                        : Math.Max(0.25, factor * ((double)SizeLimitBytes / Math.Max(1L, size)) * 0.95);
                }

                if (string.IsNullOrEmpty(bestTemp) || !File.Exists(bestTemp))
                    throw new InvalidOperationException("No se pudo generar el PDF.");

                Promote(bestTemp, outputPath);
                promoted = bestTemp;
            }
            catch (OperationCanceledException)
            {
                // A cancelled run leaves nothing at the destination.
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Error("PdfOptimizer.Create", ex);

                // A failed run still hands over the best attempt it managed
                // to build, but must not hide the promotion failing too.
                if (promoted == null && !string.IsNullOrEmpty(bestTemp) && File.Exists(bestTemp))
                {
                    try
                    {
                        Promote(bestTemp, outputPath);
                        promoted = bestTemp;
                    }
                    catch (Exception promoteError)
                    {
                        AppLog.Error("Promoción del mejor intento", promoteError);
                    }
                }

                throw new InvalidOperationException("Fallo al crear el PDF.", ex);
            }
            finally
            {
                ResourceLimits.Memory = previousMemoryLimit;

                foreach (var temp in temps)
                {
                    if (string.Equals(temp, promoted, StringComparison.OrdinalIgnoreCase))
                        continue;

                    try
                    {
                        if (File.Exists(temp))
                            File.Delete(temp);
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("Limpieza de temporales", ex);
                    }
                }
            }

            progress?.Report(100);
            return new OptimizationResult
            {
                SizeBytes = bestSize,
                WithinLimit = bestSize <= SizeLimitBytes
            };
        }

        /// <summary>
        /// Derived from the page cap rather than from measured output, so it is
        /// a genuine upper bound: enough for the parallel phase to hold a source
        /// and its resized copy per worker, plus a few resident pages during
        /// assembly.
        /// </summary>
        private static ulong PixelCacheCeilingBytes()
        {
            var pagePixels = (double)MaxPageLongEdgePixels * MaxPageShortEdgePixels;
            return (ulong)(pagePixels * CacheBytesPerPixel * (MaxParallelism * 2 + 4));
        }

        private static void Promote(string tempPath, string outputPath)
        {
            if (File.Exists(outputPath))
                File.Delete(outputPath);

            File.Move(tempPath, outputPath);
        }

        /// <summary>
        /// Header-only pass that yields each page's capped pixel count, used
        /// to weight the byte budget. Runs once for the whole job.
        /// </summary>
        private static long[] MeasureWeights(IReadOnlyList<PageSource> pages, CancellationToken cancellationToken)
        {
            var weights = new long[pages.Count];
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Math.Min(MaxParallelism, Environment.ProcessorCount)),
                CancellationToken = cancellationToken
            };

            Parallel.For(0, pages.Count, options, i =>
            {
                try
                {
                    using (var probe = new MagickImage())
                    {
                        probe.Ping(pages[i].Path, pages[i].CreateReadSettings());

                        uint width, height;
                        FitToPage(probe.Width, probe.Height, MaxDpi, out width, out height);
                        weights[i] = (long)width * height;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error($"Ping {pages[i].DisplayName}", ex);
                    weights[i] = 0;
                }
            });

            return weights;
        }

        private static long[] AllocateBudgets(long[] weights, long totalBudget)
        {
            var budgets = new long[weights.Length];
            var sum = weights.Sum();

            for (var i = 0; i < weights.Length; i++)
            {
                var share = sum > 0 ? (double)weights[i] / sum : 1.0 / weights.Length;
                budgets[i] = Math.Max(MinPageBudgetBytes, (long)(totalBudget * share));
            }

            return budgets;
        }

        private static long BuildPdf(
            IReadOnlyList<PageSource> pages,
            long[] budgets,
            string tempPath,
            int round,
            IProgress<int> progress,
            IProgress<string> detail,
            CancellationToken cancellationToken)
        {
            var total = pages.Count;
            var outputs = new PageOutput[total];
            var processed = 0;
            Exception firstError = null;

            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Math.Min(MaxParallelism, Environment.ProcessorCount)),
                CancellationToken = cancellationToken
            };

            Parallel.For(0, total, options, i =>
            {
                try
                {
                    outputs[i] = ProcessPage(pages[i], budgets[i], cancellationToken);
                    detail?.Report(Describe(pages[i], outputs[i], i, total, round));
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Interlocked.CompareExchange(ref firstError, ex, null);
                    AppLog.Error($"Página {pages[i].DisplayName}", ex);
                    detail?.Report($"[{i + 1}/{total}] Error en {pages[i].DisplayName}: {ex.Message}");
                }
                finally
                {
                    var done = Interlocked.Increment(ref processed);
                    progress?.Report(ProgressFor(round, done, total));
                }
            });

            // PDF assembly is single-threaded by design.
            progress?.Report(-1);

            using (var collection = new MagickImageCollection())
            {
                for (var i = 0; i < total; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var output = outputs[i];
                    if (output?.Buffer == null)
                        throw firstError ?? new InvalidOperationException($"No se pudo procesar la página {i + 1}.");

                    var page = new MagickImage(output.Buffer);

                    // Restate the density on every page. JPEG stores it as an
                    // integer and the Group 4 path does not, so without this a
                    // mixed document ends up with two different MediaBox sizes.
                    page.Density = new Density(output.Density, output.Density, DensityUnit.PixelsPerInch);

                    if (output.Bilevel)
                    {
                        PageBinarizer.ApplyGroup4(page, output.Density);
                    }
                    else
                    {
                        // Encoder settings live on the settings object, not on
                        // the pixels, so they do not survive the round trip
                        // through a buffer and have to be restated here.
                        page.Quality = (uint)output.Quality;
                        page.SetCompression(CompressionMethod.JPEG);
                    }

                    // Defines are document-level: the collection reads them from
                    // the first image only. Compression is deliberately NOT set
                    // on Settings for the same reason — doing so forces a single
                    // codec onto every page and measured 29 MB against 1 MB for
                    // the same two pages.
                    if (i == 0)
                    {
                        page.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "4:2:0");
                        page.Settings.SetDefine(MagickFormat.Jpeg, "interlace", "none");
                    }

                    // The collection takes ownership and disposes its items.
                    collection.Add(page);
                }

                collection.Write(tempPath, MagickFormat.Pdf);
            }

            var info = new FileInfo(tempPath);
            var size = info.Exists ? info.Length : 0;

            WarnIfOversized(outputs, size);
            return size;
        }

        private static PageOutput ProcessPage(PageSource source, long budgetBytes, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            // The decode hint lets the JPEG reader scale down while decoding, so
            // a 600 DPI scan never materialises at full size.
            using (var img = new MagickImage(source.Path, source.CreateReadSettings(MaxPageLongEdgePixels)))
            {
                Prepare(img);
                cancellationToken.ThrowIfCancellationRequested();

                uint maxWidth, maxHeight, minWidth, minHeight;
                FitToPage(img.Width, img.Height, MaxDpi, out maxWidth, out maxHeight);
                FitToPage(img.Width, img.Height, MinDpi, out minWidth, out minHeight);

                img.FilterType = FilterType.Cubic;
                if (maxWidth < img.Width || maxHeight < img.Height)
                    img.Resize(maxWidth, maxHeight);

                ApplyPageDensity(img);

                // A text page stored as Group 4 is far smaller than any JPEG of
                // it, so try that before spending the quality ladder. Evaluated
                // before the ladder runs, because the ladder mutates img.
                var bilevel = TryBilevel(img, cancellationToken);

                byte[] smallest = null;
                var smallestQuality = MinQuality;

                for (var step = 0; step <= MaxDownscaleSteps; step++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // Descending ladder: the first quality that fits is the
                    // highest quality that fits, so a page that comfortably
                    // meets its budget costs exactly one encode.
                    for (var quality = MaxQuality; quality >= MinQuality; quality -= QualityStep)
                    {
                        var buffer = Encode(img, quality);

                        if (buffer.Length <= budgetBytes)
                            return Better(bilevel, NewOutput(img, buffer, quality));

                        if (smallest == null || buffer.Length < smallest.Length)
                        {
                            smallest = buffer;
                            smallestQuality = quality;
                        }
                    }

                    // Quality alone was not enough. Drop resolution, but never
                    // below the floor of MinDpi on the page.
                    var nextWidth = (uint)Math.Max(1, Math.Round(img.Width * DownscaleStep));
                    var nextHeight = (uint)Math.Max(1, Math.Round(img.Height * DownscaleStep));
                    if (nextWidth < minWidth || nextHeight < minHeight)
                        break;

                    img.Resize(nextWidth, nextHeight);
                    ApplyPageDensity(img);
                }

                // Both floors reached and still over budget: hand back the
                // smallest attempt. The caller tightens and rebuilds.
                return Better(bilevel, NewOutput(img, smallest ?? Encode(img, MinQuality), smallestQuality));
            }
        }

        /// <summary>
        /// Returns the Group 4 rendering of a page when it is both faithful and
        /// genuinely smaller than the JPEG alternative, otherwise null.
        /// Measuring rather than predicting is the point: it guarantees a
        /// bilevel page can never be larger than the JPEG it replaces, so a
        /// noisy scan degrades to JPEG instead of becoming an irreducible cost
        /// against the size cap.
        /// </summary>
        private static PageOutput TryBilevel(MagickImage img, CancellationToken cancellationToken)
        {
            if (!PageBinarizer.LooksLikeText(img, cancellationToken))
                return null;

            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using (var binary = PageBinarizer.Binarize(img))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (!PageBinarizer.IsFaithful(img, binary))
                        return null;

                    var density = PageDensity(binary.Width, binary.Height);
                    var groupFourBytes = PageBinarizer.MeasureGroup4Bytes(binary, density, cancellationToken);

                    return new PageOutput
                    {
                        Buffer = PageBinarizer.ToBuffer(binary),
                        MeasuredBytes = groupFourBytes,
                        Bilevel = true,
                        Quality = 0,
                        Width = binary.Width,
                        Height = binary.Height,
                        Density = density
                    };
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Binarization is an optimization; never let it fail the page.
                AppLog.Error("Binarización", ex);
                return null;
            }
        }

        /// <summary>
        /// Picks the smaller of the bilevel candidate and the JPEG result,
        /// comparing what each actually costs inside the PDF.
        /// </summary>
        private static PageOutput Better(PageOutput bilevel, PageOutput jpeg)
        {
            if (bilevel == null)
                return jpeg;

            return bilevel.MeasuredBytes < jpeg.MeasuredBytes ? bilevel : jpeg;
        }

        private static PageOutput NewOutput(MagickImage img, byte[] buffer, int quality)
        {
            return new PageOutput
            {
                Buffer = buffer,
                MeasuredBytes = buffer.Length,
                Bilevel = false,
                Quality = quality,
                Width = img.Width,
                Height = img.Height,
                Density = PageDensity(img.Width, img.Height)
            };
        }

        private static void Prepare(MagickImage img)
        {
            // Orientation has to be baked into the pixels: Strip() drops the
            // EXIF tag and PDF has no orientation field, so a rotated phone
            // photo would otherwise land in the document sideways forever.
            img.AutoOrient();

            // Convert through the source profile while it is still attached.
            // Stripping first and then asserting sRGB reinterprets the numbers
            // instead of converting them, which shifts every wide-gamut image.
            if (img.GetColorProfile() != null)
            {
                try
                {
                    img.TransformColorSpace(ColorProfiles.SRGB);
                }
                catch (Exception ex)
                {
                    AppLog.Error("TransformColorSpace", ex);
                    img.ColorSpace = ColorSpace.sRGB;
                }
            }
            else
            {
                img.ColorSpace = ColorSpace.sRGB;
            }

            // JPEG carries no alpha channel: flatten explicitly rather than
            // leaving the background to whatever the encoder happens to pick.
            if (img.HasAlpha)
            {
                img.BackgroundColor = MagickColors.White;
                img.Alpha(AlphaOption.Remove);
            }

            img.Strip();
        }

        private static byte[] Encode(MagickImage img, int quality)
        {
            img.Format = MagickFormat.Jpeg;
            img.Quality = (uint)quality;
            img.Settings.Compression = CompressionMethod.JPEG;
            img.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "4:2:0");
            img.Settings.SetDefine(MagickFormat.Jpeg, "interlace", "none");

            using (var ms = new MemoryStream())
            {
                img.Write(ms);
                return ms.ToArray();
            }
        }

        /// <summary>
        /// Scales <paramref name="width"/> x <paramref name="height"/> down to
        /// what <paramref name="dpi"/> is worth on the output page. Never
        /// upscales, so a small source keeps its own pixels.
        /// </summary>
        private static void FitToPage(uint width, uint height, int dpi, out uint fitWidth, out uint fitHeight)
        {
            double longPixels = Math.Max(width, height);
            double shortPixels = Math.Min(width, height);

            var scale = Math.Min(
                1.0,
                Math.Min(dpi * PageLongEdgeInches / longPixels, dpi * PageShortEdgeInches / shortPixels));

            fitWidth = (uint)Math.Max(1, Math.Round(width * scale));
            fitHeight = (uint)Math.Max(1, Math.Round(height * scale));
        }

        /// <summary>Density that makes the image exactly fill the page box.</summary>
        private static double PageDensity(uint width, uint height)
        {
            double longPixels = Math.Max(width, height);
            double shortPixels = Math.Min(width, height);

            return Math.Max(longPixels / PageLongEdgeInches, shortPixels / PageShortEdgeInches);
        }

        private static void ApplyPageDensity(MagickImage img)
        {
            var density = PageDensity(img.Width, img.Height);
            img.Density = new Density(density, density, DensityUnit.PixelsPerInch);
        }

        private static int ProgressFor(int round, int done, int total)
        {
            var index = Math.Max(0, Math.Min(round, ProgressBands.Length - 2));
            var low = ProgressBands[index];
            var high = ProgressBands[index + 1];

            return low + (int)Math.Round((high - low) * done / (double)Math.Max(1, total));
        }

        private static string Describe(PageSource source, PageOutput output, int index, int total, int round)
        {
            var stage = round == 0 ? "Calidad máxima" : $"Ajuste {round}";

            // A bilevel page has no quality setting, so reporting one would be
            // meaningless.
            var mode = output.Bilevel
                ? "blanco y negro (G4)"
                : $"calidad {output.Quality}";

            return $"[{index + 1}/{total}] {stage} - {source.DisplayName} - "
                 + $"{output.Width}x{output.Height} @ {output.Density:0} PPP - "
                 + $"{mode} - {output.MeasuredBytes / 1024.0:0.#} KB";
        }

        /// <summary>
        /// The PDF should be about the size of the page data that went into it.
        /// A large overshoot means the writer fell back to a codec nobody asked
        /// for, which is silent otherwise — there is no exception and the file
        /// is still valid, just many times too big.
        /// </summary>
        private static void WarnIfOversized(PageOutput[] outputs, long size)
        {
            long expected = 0;
            foreach (var output in outputs)
            {
                if (output != null)
                    expected += output.MeasuredBytes;
            }

            if (expected <= 0 || size <= 0)
                return;

            var ratio = (double)size / expected;
            if (ratio > 1.35)
            {
                AppLog.Error(
                    "Ensamblado PDF",
                    new InvalidOperationException(
                        $"El PDF ({size} B) es {ratio:0.##}x el tamaño esperado ({expected} B); "
                        + "el codificador pudo haber ignorado la compresión solicitada."));
            }
        }

        private sealed class PageOutput
        {
            public byte[] Buffer { get; set; }

            /// <summary>What this page costs in the output, in bytes.</summary>
            public long MeasuredBytes { get; set; }

            public bool Bilevel { get; set; }

            public int Quality { get; set; }

            public uint Width { get; set; }

            public uint Height { get; set; }

            public double Density { get; set; }
        }
    }
}

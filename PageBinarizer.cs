using ImageMagick;
using System;
using System.IO;
using System.Threading;

namespace opti_pdf
{
    /// <summary>
    /// Turns a scanned text page into 1-bit black and white so it can be stored
    /// with CCITT Group 4 compression, which is dramatically smaller than JPEG
    /// for text: a 300 DPI Oficio page measured 16.9 KB as G4 against 497 KB as
    /// JPEG q80.
    ///
    /// Binarizing a photograph destroys it, so the decision is deliberately
    /// conservative: a page is only accepted when binarizing it barely changes
    /// it, and even then only when the measured Group 4 output is genuinely
    /// smaller than the JPEG that would otherwise ship.
    /// </summary>
    internal static class PageBinarizer
    {
        /// <summary>
        /// Long edge of the throwaway copy the decision is made on. Sampling to
        /// this size makes the check about 36x cheaper (30 ms against 1100 ms on
        /// a full 300 DPI page) and the measured error tracks the full
        /// resolution value closely enough to decide on.
        /// </summary>
        private const int ProxyLongEdge = 700;

        /// <summary>
        /// Maximum root-mean-square difference between the greyscale page and
        /// its binarized form for the page to count as text.
        ///
        /// Tuned on synthetic pages: clean, noisy and shadowed text measured
        /// 0.000 to 0.213, while photographs measured 0.433 to 0.690. The cut
        /// sits below the midpoint so the uncertain cases fall to JPEG, which
        /// is always safe. Worth revisiting against real scans.
        /// </summary>
        private const double MaxBinarizationError = 0.30;

        // A page that comes out nearly blank or nearly solid has been destroyed
        // by thresholding, whatever the error metric said.
        private const double MinInkPercent = 0.5;
        private const double MaxInkPercent = 40.0;

        /// <summary>
        /// Cheap first pass: is this page worth binarizing at all? Runs on a
        /// sampled copy so it costs about a fifth of a JPEG encode.
        /// </summary>
        public static bool LooksLikeText(MagickImage prepared, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                using (var proxy = (MagickImage)prepared.Clone())
                {
                    // Sample, not Resize: point sampling is enough to decide on
                    // and avoids filtering a full resolution page.
                    proxy.Sample(ProxyLongEdge, ProxyLongEdge);

                    using (var grey = (MagickImage)proxy.Clone())
                    using (var binary = Binarize(proxy))
                    {
                        grey.Grayscale();
                        return Error(grey, binary) < MaxBinarizationError;
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Any failure here means "not a text page"; the JPEG path is
                // always a correct answer.
                AppLog.Error("Clasificación de página", ex);
                return false;
            }
        }

        /// <summary>
        /// Produces the 1-bit version of a page. Always works on a clone: the
        /// caller still needs the original for the JPEG path if this loses.
        /// </summary>
        public static MagickImage Binarize(MagickImage source)
        {
            var result = (MagickImage)source.Clone();

            try
            {
                result.Grayscale();

                // Divide out an estimate of the lighting before thresholding.
                // Without this a global threshold turns the shadowed corner of a
                // phone photo solid black; with it the same page binarizes
                // cleanly.
                using (var background = (MagickImage)result.Clone())
                {
                    var w = Math.Max(1u, result.Width / 20);
                    var h = Math.Max(1u, result.Height / 20);

                    background.Resize(new MagickGeometry(w, h) { IgnoreAspectRatio = true });
                    background.Blur(0, 8);
                    background.Resize(new MagickGeometry(result.Width, result.Height) { IgnoreAspectRatio = true });

                    result.Composite(background, CompositeOperator.DivideSrc);
                }

                result.AutoThreshold(AutoThresholdMethod.OTSU);
                return result;
            }
            catch
            {
                result.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Confirms the binarized page is neither blank nor solid, and that it
        /// still resembles the greyscale original at full resolution.
        /// </summary>
        public static bool IsFaithful(MagickImage prepared, MagickImage binary)
        {
            var ink = 100.0 * (1 - binary.Statistics().Composite().Mean / Quantum.Max);
            if (ink < MinInkPercent || ink > MaxInkPercent)
                return false;

            using (var grey = (MagickImage)prepared.Clone())
            {
                grey.Grayscale();
                return Error(grey, binary) < MaxBinarizationError;
            }
        }

        /// <summary>
        /// Measures what the page would actually cost as Group 4 inside a PDF.
        /// The PNG buffer the page travels in is not a usable proxy for this —
        /// it is off by an order of magnitude — so the real encoder is run.
        /// Written to memory, so it leaves no temp file to clean up.
        /// </summary>
        public static long MeasureGroup4Bytes(MagickImage binary, double density, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using (var collection = new MagickImageCollection())
            using (var buffer = new MemoryStream())
            {
                var page = (MagickImage)binary.Clone();
                ApplyGroup4(page, density);
                collection.Add(page);
                collection.Write(buffer, MagickFormat.Pdf);

                return buffer.Length;
            }
        }

        /// <summary>
        /// Marks an image so the PDF writer stores it as CCITT Group 4.
        /// Note that <see cref="IMagickImage.Settings"/> is NOT used: for a
        /// collection the first image's settings act as a document-wide
        /// override, and setting compression there forces every page onto one
        /// codec — measured at 29 MB against 1 MB for the same two pages.
        /// </summary>
        public static void ApplyGroup4(MagickImage page, double density)
        {
            page.ColorType = ColorType.Bilevel;
            page.Density = new Density(density, density, DensityUnit.PixelsPerInch);
            page.SetCompression(CompressionMethod.Group4);
        }

        /// <summary>
        /// Lossless carrier for a 1-bit page. PNG keeps the two-tone pixels
        /// exactly, which is what lets the assembly step re-request Group 4.
        /// </summary>
        public static byte[] ToBuffer(MagickImage binary)
        {
            binary.Format = MagickFormat.Png;
            binary.ColorType = ColorType.Bilevel;

            using (var buffer = new MemoryStream())
            {
                binary.Write(buffer);
                return buffer.ToArray();
            }
        }

        /// <summary>
        /// Compares on the grey channel explicitly. The default overload folds
        /// in the alpha channel and reports roughly three times the error on a
        /// page that still carries one, which silently moves the threshold.
        /// </summary>
        private static double Error(MagickImage grey, MagickImage binary)
        {
            return grey.Compare(binary, ErrorMetric.RootMeanSquared, Channels.Gray);
        }
    }
}

using ImageMagick;
using System;
using System.Collections.Generic;
using System.IO;

namespace opti_pdf
{
    /// <summary>
    /// One page of the output PDF: a single frame of a single source file.
    /// A multi-page TIFF expands to one PageSource per frame, so scanner
    /// output no longer collapses to its first page.
    /// </summary>
    internal sealed class PageSource
    {
        private readonly object _sync = new object();
        private uint _sourceLongEdge;
        private bool _sourceMeasured;

        public PageSource(string path, int frameIndex, int frameCount)
        {
            Path = path;
            FrameIndex = frameIndex;
            FrameCount = frameCount;
        }

        public string Path { get; }

        public int FrameIndex { get; }

        public int FrameCount { get; }

        /// <summary>Stable identity for list and ImageList lookups.</summary>
        public string Key
        {
            get { return FrameCount > 1 ? Path + "|" + FrameIndex : Path; }
        }

        public string DisplayName
        {
            get
            {
                var name = System.IO.Path.GetFileName(Path);
                return FrameCount > 1 ? $"{name} [{FrameIndex + 1}/{FrameCount}]" : name;
            }
        }

        public MagickReadSettings CreateReadSettings()
        {
            return CreateReadSettings(0);
        }

        /// <summary>
        /// Read settings for this page. When <paramref name="maxLongEdge"/> is
        /// positive and the source is bigger than that, the JPEG reader is asked
        /// to scale the image down while decoding, so a 600 DPI scan never
        /// materialises at full size: a 7794x5102 source decoded to 5846x3827 in
        /// 385 ms instead of 593 ms. It is a hint, not a guarantee — the reader
        /// picks its own fraction and only for formats that support it — so
        /// callers must still resize.
        /// </summary>
        public MagickReadSettings CreateReadSettings(int maxLongEdge)
        {
            // Colour space is deliberately not asserted here: that only
            // relabels the pixels. The real conversion happens after the
            // read, while the source ICC profile is still attached.
            var settings = new MagickReadSettings
            {
                FrameIndex = (uint)FrameIndex,
                FrameCount = 1
            };

            // Only ever ask for less than the source has. libjpeg's scaled
            // decode enlarges as readily as it shrinks: asking a 3000x2000
            // source for 3850x3850 decoded it to 5625x3750, a 15/8 enlargement
            // that costs three times the memory and then presents the page
            // model with a resolution the source cannot back. No geometry
            // qualifier suppresses it — '>' , '^' and '!' all measured the same
            // enlargement — so the size has to be checked first.
            if (maxLongEdge > 0 && SourceLongEdge() > (uint)maxLongEdge)
            {
                // MagickReadSettings.Width/Height look like they would do this
                // but are ignored by the JPEG reader; the define is what drives
                // libjpeg's scaled decode.
                settings.SetDefine(MagickFormat.Jpeg, "size", $"{maxLongEdge}x{maxLongEdge}");
            }

            return settings;
        }

        /// <summary>
        /// Long edge of the source in pixels, from a header read done once and
        /// remembered. A page is re-read on every optimization round, and pages
        /// are processed in parallel, so this is both cached and locked. An
        /// unreadable header reports zero, which suppresses the decode hint —
        /// slower, but never wrong.
        /// </summary>
        private uint SourceLongEdge()
        {
            lock (_sync)
            {
                if (!_sourceMeasured)
                {
                    _sourceMeasured = true;

                    try
                    {
                        using (var probe = new MagickImage())
                        {
                            probe.Ping(Path, new MagickReadSettings
                            {
                                FrameIndex = (uint)FrameIndex,
                                FrameCount = 1
                            });

                            _sourceLongEdge = Math.Max(probe.Width, probe.Height);
                        }
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error($"Ping {DisplayName}", ex);
                        _sourceLongEdge = 0;
                    }
                }

                return _sourceLongEdge;
            }
        }

        /// <summary>
        /// Expands a file into its pages, reading headers only.
        /// </summary>
        public static IEnumerable<PageSource> FromFile(string path)
        {
            var count = 1;

            // Only TIFF is expanded. An animated GIF is also multi-frame, but
            // turning each animation frame into a PDF page is never what the
            // user meant.
            if (IsMultiPage(path))
            {
                try
                {
                    using (var probe = new MagickImageCollection())
                    {
                        probe.Ping(path);
                        count = Math.Max(1, probe.Count);
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error($"Ping {System.IO.Path.GetFileName(path)}", ex);
                    count = 1;
                }
            }

            var pages = new List<PageSource>(count);
            for (var i = 0; i < count; i++)
                pages.Add(new PageSource(path, i, count));

            return pages;
        }

        private static bool IsMultiPage(string path)
        {
            var ext = System.IO.Path.GetExtension(path)?.ToLowerInvariant();
            return ext == ".tif" || ext == ".tiff";
        }
    }
}

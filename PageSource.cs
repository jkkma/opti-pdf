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
            // Colour space is deliberately not asserted here: that only
            // relabels the pixels. The real conversion happens after the
            // read, while the source ICC profile is still attached.
            return new MagickReadSettings
            {
                FrameIndex = (uint)FrameIndex,
                FrameCount = 1
            };
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

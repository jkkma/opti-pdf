using ImageMagick;
using System;

namespace opti_pdf
{
    /// <summary>
    /// One output page: a pixel canvas with the exact proportions of the target
    /// sheet, plus the density that makes it measure that sheet in the PDF.
    /// </summary>
    internal struct PageCanvas
    {
        public PageCanvas(int units, bool portrait)
        {
            Units = units;
            Portrait = portrait;
        }

        /// <summary>
        /// Size of the page in grid units. This is the resolution knob: the
        /// page always measures 216 x 330 mm, and <see cref="Units"/> only
        /// decides how many pixels that is.
        /// </summary>
        public int Units { get; }

        public bool Portrait { get; }

        public uint Width
        {
            get { return (Portrait ? PageGeometry.GridShortEdge : PageGeometry.GridLongEdge) * (uint)Units; }
        }

        public uint Height
        {
            get { return (Portrait ? PageGeometry.GridLongEdge : PageGeometry.GridShortEdge) * (uint)Units; }
        }

        public double Density
        {
            get { return PageGeometry.DensityFor(Units); }
        }
    }

    /// <summary>
    /// Fixes every page of the output at Oficio / Folio, 216 x 330 mm — the
    /// paper the documents this app handles are printed on.
    ///
    /// The earlier approach set only the density, which scaled each image to fit
    /// *within* the sheet. That gives identical pages for a batch of
    /// identically shaped scans and nothing of the sort for a mixed batch: a
    /// 2551x3898 scan came out 216x330 mm while a 1200x1800 one came out
    /// 216x324 mm, because the density was pinned by whichever edge ran out
    /// first.
    ///
    /// So the page is chosen first and the image is placed into it. The trick
    /// that makes the box exact is the grid: gcd(216, 330) is 6, so the sheet
    /// is 36:55, and a canvas of 36k x 55k pixels has its proportions exactly.
    /// One square density, 25.4k/6 DPI, then puts the box at
    /// 216/25.4*72 x 330/25.4*72 pt for *every* k — so pages of different
    /// resolutions still agree on the box to the last digit. Measured across
    /// k = 26 to 100: one distinct /MediaBox, [0 0 612.283 935.433].
    /// </summary>
    internal static class PageGeometry
    {
        public const double MillimetresPerInch = 25.4;
        public const double LongEdgeMm = 330.0;
        public const double ShortEdgeMm = 216.0;

        /// <summary>gcd(216, 330). Dividing the sheet by it gives the grid.</summary>
        private const double GridDivisor = 6.0;

        public const uint GridShortEdge = (uint)(ShortEdgeMm / GridDivisor);  // 36
        public const uint GridLongEdge = (uint)(LongEdgeMm / GridDivisor);    // 55

        /// <summary>Density at which a grid unit measures its share of the sheet.</summary>
        public static double DensityFor(int units)
        {
            return MillimetresPerInch * units / GridDivisor;
        }

        /// <summary>
        /// Largest page that stays at or under <paramref name="dpi"/>. Truncated
        /// rather than rounded, so the cap is never exceeded: 300 DPI yields 70
        /// units, which is 296.333 DPI.
        /// </summary>
        public static int MaxUnits(int dpi)
        {
            return Math.Max(1, (int)(dpi * GridDivisor / MillimetresPerInch));
        }

        /// <summary>
        /// Smallest page that stays at or above <paramref name="dpi"/>. Rounded
        /// up for the same reason: 150 DPI yields 36 units, which is 152.4 DPI.
        /// </summary>
        public static int MinUnits(int dpi)
        {
            return Math.Max(1, (int)Math.Ceiling(dpi * GridDivisor / MillimetresPerInch));
        }

        /// <summary>
        /// The page for an image of this size: oriented like the image, and the
        /// smallest grid page that holds the image at its own size so nothing is
        /// upscaled, capped at what <paramref name="maxDpi"/> is worth.
        /// </summary>
        public static PageCanvas Choose(uint width, uint height, int maxDpi)
        {
            // A square image is treated as portrait; it letterboxes either way.
            var portrait = height >= width;
            var gridWidth = portrait ? GridShortEdge : GridLongEdge;
            var gridHeight = portrait ? GridLongEdge : GridShortEdge;

            var fit = Math.Max(
                (int)Math.Ceiling(width / (double)gridWidth),
                (int)Math.Ceiling(height / (double)gridHeight));

            return new PageCanvas(Math.Max(1, Math.Min(fit, MaxUnits(maxDpi))), portrait);
        }

        public static PageCanvas WithUnits(PageCanvas canvas, int units)
        {
            return new PageCanvas(Math.Max(1, units), canvas.Portrait);
        }

        /// <summary>
        /// Scales the image down until it fits inside the page, preserving its
        /// aspect ratio. Never upscales: a small source keeps its own pixels and
        /// simply sits on a low-resolution page.
        /// </summary>
        public static void Fit(MagickImage img, PageCanvas canvas)
        {
            // Resize fits *within* the box and keeps the aspect ratio, so the
            // guard is what stops it enlarging a smaller image.
            if (img.Width > canvas.Width || img.Height > canvas.Height)
                img.Resize(canvas.Width, canvas.Height);
        }

        /// <summary>
        /// Puts the image on the page: scaled to fit, centred, the rest of the
        /// sheet white. Real pixels rather than a page box around a smaller
        /// image — see the note on the alternative below.
        /// </summary>
        public static void Place(MagickImage img, PageCanvas canvas)
        {
            Fit(img, canvas);

            img.BackgroundColor = MagickColors.White;
            img.Extent(canvas.Width, canvas.Height, Gravity.Center, MagickColors.White);

            Apply(img, canvas.Density);
        }

        /// <summary>
        /// Restates what the PDF coder reads the page box off: /MediaBox comes
        /// out as the page geometry divided by the density, so both have to be
        /// right and both get lost along the way. Extent grows the geometry
        /// rather than replacing it — a stale 10199x10388 measured a
        /// 5102 x 5196 pt box — and a JPEG buffer stores density as an integer,
        /// which a Group 4 page does not, so leaving it alone gives one document
        /// two page sizes. Called again at assembly time, where the pixels are
        /// already right but arrive from a buffer that dropped both.
        /// </summary>
        public static void Apply(MagickImage img, double density)
        {
            img.Page = new MagickGeometry(0, 0, img.Width, img.Height);
            img.Density = new Density(density, density, DensityUnit.PixelsPerInch);
        }
    }
}

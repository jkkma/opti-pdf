# opti-pdf

Spanish-language WinForms app (.NET Framework 4.8) that converts images into a
size-optimized PDF using Magick.NET. Single form.

- `Form1.cs` — UI only: the page list, thumbnails, progress, cancellation.
- `PdfOptimizer.cs` — the whole image-to-PDF pipeline. No UI types.
- `PageSource.cs` — one page of the output (a file plus a frame index, so a
  multi-page TIFF expands to one page per frame).
- `PageBinarizer.cs` — 1-bit conversion and the text-vs-photo decision behind
  the CCITT Group 4 path.
- `NaturalFileComparer.cs` — human-order path sort (page2 before page10).
- `AppLog.cs` — the shared `%TEMP%\opti_pdf.log` writer.

## Build

- **Windows (real build + run):** open `opti-pdf.slnx` in Visual Studio, or
  `msbuild opti-pdf.csproj /p:Configuration=Release` after a NuGet restore
  (`nuget restore opti-pdf.csproj -PackagesDirectory packages`).
- **Linux / cloud containers:** `bash scripts/linux-build.sh` — restores
  packages.config from nuget.org and compiles against .NET Framework 4.8
  reference assemblies. **Compile check only**; the WinForms exe cannot run on
  Linux. First run ~15 s (downloads packages), incremental ~3 s.
- Never edit anything under `packages/`, `bin/` or `obj/` — restored/generated.

## Legacy project format (not SDK-style)

`opti-pdf.csproj` is an old-style csproj with `packages.config`. Nothing is
auto-globbed:

- **Every new `.cs` file must be added to `opti-pdf.csproj` as a
  `<Compile Include="..."/>` item** or it silently won't compile.
- New NuGet dependencies require **both** an entry in `packages.config` and a
  `<Reference>` with a `<HintPath>` into `packages\<Id>.<Version>\lib\...` in
  the csproj (copy the pattern of the existing references).
- Do not migrate the project to SDK-style.

## Language convention

- **All user-facing strings (UI text, MessageBox, status messages) are in
  Spanish.** Code identifiers and comments are in English.

## WinForms designer files

There is no designer round-trip here — `Form1.Designer.cs` is edited by hand.
Adding a control means touching all of:

1. Field declaration at the bottom of `Form1.Designer.cs`.
2. Construction + property setup inside `InitializeComponent()`.
3. `this.Controls.Add(...)` in `InitializeComponent()`.
4. Event wiring in the `Form1` constructor in `Form1.cs` (not in the designer).

Keep `Form1.resx` consistent with the designer file. Coordinates/sizes in
`InitializeComponent()` are at **240-DPI scale** (`AutoScaleDimensions =
(240F, 240F)`, `AutoScaleMode.Dpi`); the app is PerMonitorV2 DPI-aware via
`app.manifest`. See `.claude/skills/winforms-ui/SKILL.md` for the full
playbook.

## Domain invariants (do not break)

- Output PDF targets a **5 MB cap** (`PdfOptimizer.SizeLimitBytes`). When the
  cap cannot be met within the quality floors the best attempt is still saved,
  but the user must be told it is over the limit — never report a plain
  success for an oversized file.
- JPEG quality never goes below **40**; effective page resolution never below
  **150 DPI**.
- Images are **never upscaled**. Resolution targets are expressed in pixels
  relative to the output page, *not* as the source's DPI metadata — that field
  is absent or wrong on most inputs, and keying off it means downscaling
  silently never happens.
- Every output page is **exactly 216 x 330 mm (Oficio / Folio)** —
  `/MediaBox [0 0 612.283 935.433]`, or its transpose on a landscape page. That
  is the paper the target documents are printed on — not A4, not US Legal. The
  page is chosen first and the image is placed into it: scaled to fit, centred,
  the rest of the sheet white. All of this lives in `PageGeometry.cs`.
  - Fitting by density alone is what this replaced, and it does **not** give
    uniform pages: whichever edge ran out first pinned the density, so one
    document measured 216x330, 216x324, 188x330 and 324x216 mm across four
    pages.
  - The exactness rests on the **36:55 grid**. gcd(216, 330) is 6, so a canvas
    of `36k x 55k` px at `25.4k/6` DPI measures the sheet exactly for *every*
    `k` — which is what lets pages of different resolutions agree on the box to
    the last digit. Page pixel dimensions must stay on that grid: resize a page
    by changing `k`, never by scaling its pixels off-grid.
  - `k` is the resolution knob, and the only one: `MaxDpi` 300 caps it at
    `k=70` (296.33 DPI) and the 150 DPI floor at `k=36` (152.4 DPI).
  - The page box follows the **image's** orientation, so a landscape scan gets a
    landscape Oficio page rather than letterboxing into a portrait one at 66%
    linear scale. A mixed-orientation batch therefore has two page sizes — the
    same sheet, turned.
- Geometry and compression stay separate concerns — do not re-conflate them.
- The `Prepare` order in `PdfOptimizer` is load-bearing: `AutoOrient` →
  colour-profile conversion → alpha flatten → `Strip`. Stripping first destroys
  the EXIF orientation and the ICC profile the earlier steps need, and PDF has
  no orientation field to fall back on.
- Optimization is: round 0 (gentle — best quality, resolution capped to the
  page) → up to 3 budgeted rounds that split the byte budget across pages and
  correct the budget from the measured output size. Within a page, quality is
  tried **descending**, so the first fit is the highest quality that fits.
- The best attempt is always promoted to the output, even on partial failure —
  **except on cancellation**, which must leave the destination untouched.
- Every temp attempt is registered in the `temps` list and deleted unless it
  was promoted. Temp attempts are named `opti_pdf_attempt_*` in `%TEMP%`.
- Image processing runs in `Parallel.For` with **max degree of parallelism
  4**; PDF assembly (`MagickImageCollection`) is **single-threaded by
  design** — do not parallelize it.
- Encoder state does **not** travel with the pixels through a byte buffer, so
  it is restated at assembly time — but *where* it is restated matters:
  - **Per image** (`page.Quality`, `page.SetCompression(...)`, `page.Density`):
    correct, and required.
  - **On `page.Settings`**: document-level. `MagickImageCollection` reads
    settings from **image[0] only**, and they apply to every page. Never set
    `Settings.Compression` — it forces one codec onto the whole document and
    drops the other pages to RunLengthDecode. Measured: 29 MB versus 1 MB for
    the same two pages. Set the JPEG defines on `i == 0` only; on later pages
    they are silently ignored.
- The page box must be restated on **every** page at assembly time, via
  `PageGeometry.Apply`. The PDF coder emits `/MediaBox` as the image's page
  geometry divided by its density, so both halves matter and both get lost on
  the way: JPEG stores density as an integer where the Group 4 path does not,
  and `Extent` *grows* the page geometry instead of replacing it — a stale
  10199x10388 measured a 5102 x 5196 pt box.
- A text page may be stored as **1-bit CCITT Group 4** instead of JPEG — far
  smaller for scans (measured 16.9 KB against 497 KB at 300 DPI). Two gates
  guard it, and both must stay:
  1. **Faithfulness** — binarizing must barely change the page
     (`PageBinarizer.MaxBinarizationError`). Binarizing a photo destroys it.
  2. **Measured size** — the real Group 4 output is encoded and compared against
     the JPEG that would otherwise ship, and the smaller one wins. This is what
     guarantees a bilevel page is never *larger* than the JPEG it replaces, so
     a noisy scan degrades to JPEG instead of becoming an irreducible cost
     against the size cap. Never replace this with a predicted size — the PNG
     carrier buffer is off by an order of magnitude.
  The thresholds are tuned on synthetic pages; revisit them against real scans.
  Both gates run **before** the page is padded onto the sheet, and must keep
  doing so: they are calibrated against a page's own content, and white margin
  lowers both the ink percentage and the measured error — enough to walk a
  photograph through the faithfulness check and destroy it.
- Source images are decoded with a `jpeg:size` hint so a 600 DPI scan never
  materializes at full resolution. `MagickReadSettings.Width`/`.Height` look
  like they do this but are ignored by the JPEG reader. It is only a hint —
  the reader picks its own fraction — so callers must still resize. **Set it
  only when the source is genuinely larger than the request**: libjpeg's scaled
  decode enlarges as readily as it shrinks, and a 3000x2000 source asked for
  3850x3850 decoded to 5625x3750 — three times the memory, and a resolution the
  source cannot back. No geometry qualifier suppresses it; `>`, `^` and `!` all
  measured the same enlargement, so the source size has to be checked first.
- `MagickImageCollection` takes ownership of images added to it — add directly,
  do not clone defensively, and do not dispose what you added.

## Threading rules

- Worker code must never touch controls directly. UI updates go through
  `SetStatus(...)` (which handles `InvokeRequired`) or the `IProgress<>`
  instances created on the UI thread.
- Long-running work must honor the `CancellationToken` — check it in every
  loop/pass.
- Errors are logged to `%TEMP%\opti_pdf.log` via `AppLog.Error(context, ex)`;
  never let logging throw.
- Anything that mutates the page list must be disabled while a conversion runs
  — including `AllowDrop`.
- Each added batch is sorted with `NaturalFileComparer`, but the list as a
  whole is never re-sorted: a cover page the user placed first stays first.
  List order is PDF order, and the ListView caption carries the page number
  because nothing else on screen conveys it.

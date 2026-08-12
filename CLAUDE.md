# opti-pdf

Spanish-language WinForms app (.NET Framework 4.8) that converts images into a
size-optimized PDF using Magick.NET. Single form.

- `Form1.cs` — UI only: the page list, thumbnails, progress, cancellation.
- `PdfOptimizer.cs` — the whole image-to-PDF pipeline. No UI types.
- `PageSource.cs` — one page of the output (a file plus a frame index, so a
  multi-page TIFF expands to one page per frame).
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
- Output pages are normalized to fit a **216 x 330 mm (Oficio / Folio) box**
  via the density tag, so a mixed batch produces consistent page sizes. That is
  the paper the target documents are printed on — not A4, not US Legal. Geometry (density) and
  compression (pixel count, quality) are separate concerns — do not re-conflate
  them.
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
- JPEG encoder settings live on `MagickImage.Settings` and do **not** travel
  with the pixels through a byte buffer. Restate them on each page before
  writing the collection, or the PDF writer re-encodes with its own defaults.
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
  — including `AllowDrop`, since the worker iterates the list from a background
  thread.

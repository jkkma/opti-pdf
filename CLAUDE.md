# opti-pdf

Spanish-language WinForms app (.NET Framework 4.8) that converts images into a
size-optimized PDF using Magick.NET. Single form; all logic lives in `Form1.cs`.

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

- Output PDF has a **hard 5 MB cap** (`sizeLimitBytes` in `Form1.cs`).
- JPEG quality never goes below **40**; DPI never below **150**.
- Images are **never upscaled** — target DPI is clamped to the source DPI.
- Optimization pipeline order is fixed: gentle pass → JPEG-quality binary
  search → aggressiveness escalation (0–3). The best attempt so far is always
  promoted to the output file, even on partial failure.
- Image processing runs in `Parallel.For` with **max degree of parallelism
  4**; PDF assembly (`MagickImageCollection`) is **single-threaded by
  design** — do not parallelize it.
- Temp attempts are named `opti_pdf_attempt_*` in `%TEMP%` and must always be
  deleted or promoted to the output.

## Threading rules

- Worker code must never touch controls directly. UI updates go through
  `SetStatus(...)` (which handles `InvokeRequired`) or the `IProgress<>`
  instances created on the UI thread.
- Long-running work must honor the `CancellationToken` — check it in every
  loop/pass.
- Errors are logged to `%TEMP%\opti_pdf.log` via `LogError(context, ex)`;
  never let logging throw.

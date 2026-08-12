---
name: winforms-ui
description: Playbook for adding or modifying controls in opti-pdf's hand-edited WinForms designer files. Use whenever a change touches Form1.Designer.cs, Form1.resx, or the form's layout/controls.
---

# opti-pdf WinForms UI playbook

There is no Visual Studio designer round-trip in this repo —
`Form1.Designer.cs` is edited by hand. Follow this playbook exactly.

## Adding a control: the four-place checklist

Every new control must be wired in **all four** places:

1. **Field declaration** at the bottom of `Form1.Designer.cs`:
   `private System.Windows.Forms.Button btnFoo;`
2. **Construction + property setup** inside `InitializeComponent()`:
   instantiate near the top (`this.btnFoo = new System.Windows.Forms.Button();`),
   then a `// btnFoo` property block (Anchor, Location, Name, Size, TabIndex,
   Text, UseVisualStyleBackColor) matching the style of the existing blocks.
3. **`this.Controls.Add(this.btnFoo);`** in the Form1 section of
   `InitializeComponent()`.
4. **Event wiring in the `Form1` constructor** in `Form1.cs`
   (`btnFoo.Click += BtnFoo_Click;`) — events are wired in the constructor,
   NOT in the designer file.

Modifying a control usually means touching places 2 (properties) and 4
(handlers); removing one means cleaning up all four.

Keep `Form1.resx` consistent with the designer file — it currently carries no
per-control resources, so don't introduce designer-serialized resources
unless you also add them there.

## Layout conventions

- **Coordinates and sizes are at 240-DPI scale** — the form declares
  `AutoScaleDimensions = new SizeF(240F, 240F)` with `AutoScaleMode.Dpi`
  (app is PerMonitorV2 via `app.manifest`). A "normal" 96-DPI pixel value
  must be multiplied by 2.5. Reference sizes: buttons ≈ 180×78, form client
  area 1248×712.
- **Anchoring scheme:**
  - List-management buttons (`btnAdd`, `btnRemove`, `btnClear`):
    `AnchorStyles.Bottom | AnchorStyles.Left`, laid out left-to-right along
    the bottom edge starting at x=20.
  - Action buttons (`btnCancel`, `btnCreate`): `AnchorStyles.Bottom |
    AnchorStyles.Right`, laid out toward the bottom-right corner
    (`btnCreate` is the rightmost, bold caption).
  - `lvImages` (the image list): anchored on **all four sides**
    (Top | Bottom | Left | Right) so it stretches with the form.
  - `progressBar` spans the width above the docked `lblStatus`
    (Dock = Bottom).
- **Form background is `Color.SeaShell`**; buttons keep
  `UseVisualStyleBackColor = true`.
- **All captions/labels are Spanish** ("Agregar imágenes",
  "Eliminar seleccionados", "Borrar todo", "Cancelar", "CREAR PDF",
  status "LISTO"). Keep new UI text Spanish; keep identifiers English.
- Keep `TabIndex` values coherent with the existing order when inserting.

## After any designer change

Run `bash scripts/linux-build.sh` (Linux compile check) — designer files are
plain C#, so the compiler catches a missed field/registration immediately.

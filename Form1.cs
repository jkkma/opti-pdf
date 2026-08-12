using ImageMagick;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace opti_pdf
{
    public partial class Form1 : Form
    {
        private readonly List<PageSource> _pages = new List<PageSource>();
        private CancellationTokenSource _cts;
        private string _lastDetailStatus = "LISTO";
        private bool _closeRequested;

        public Form1()
        {
            InitializeComponent();

            btnAdd.Click += BtnAdd_Click;
            btnRemove.Click += BtnRemove_Click;
            btnClear.Click += BtnClear_Click;
            btnCreate.Click += BtnCreate_Click;
            btnCancel.Click += BtnCancel_Click;

            this.AllowDrop = true;
            this.DragEnter += Form1_DragEnter;
            this.DragDrop += Form1_DragDrop;
            this.FormClosing += Form1_FormClosing;

            lvImages.AllowDrop = true;
            lvImages.DragEnter += Form1_DragEnter;
            lvImages.DragDrop += Form1_DragDrop;
            lvImages.View = View.LargeIcon;
        }

        private bool IsBusy
        {
            get { return _cts != null; }
        }

        private async void BtnCreate_Click(object sender, EventArgs e)
        {
            if (_pages.Count == 0)
            {
                MessageBox.Show("No se han agregado imágenes.", "Opti-PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            using (var dlg = new SaveFileDialog())
            {
                dlg.Filter = "Archivo PDF|*.pdf";
                dlg.DefaultExt = "pdf";
                dlg.FileName = "optimizadas.pdf";
                if (dlg.ShowDialog() != DialogResult.OK)
                    return;

                _cts = new CancellationTokenSource();
                _lastDetailStatus = "Procesando...";
                SetInputEnabled(false);
                progressBar.Value = 0;
                progressBar.Visible = true;
                lblStatus.Text = "Procesando...";

                var progress = new Progress<int>(p =>
                {
                    var baseText = string.IsNullOrEmpty(_lastDetailStatus) ? "Procesando..." : _lastDetailStatus;

                    if (p < 0)
                    {
                        progressBar.Style = ProgressBarStyle.Marquee;
                        progressBar.MarqueeAnimationSpeed = 30;
                        progressBar.Visible = true;
                        SetStatus(baseText);
                        return;
                    }

                    progressBar.MarqueeAnimationSpeed = 0;
                    progressBar.Style = ProgressBarStyle.Continuous;
                    var clamped = Math.Max(0, Math.Min(100, p));
                    progressBar.Value = clamped;
                    SetStatus($"{baseText} ({clamped}%)");
                });

                var detailProgress = new Progress<string>(msg =>
                {
                    _lastDetailStatus = msg;
                    SetStatus(msg);
                });

                try
                {
                    var pages = _pages.ToArray();
                    var result = await Task.Run(
                        () => PdfOptimizer.Create(pages, dlg.FileName, progress, detailProgress, _cts.Token),
                        _cts.Token);

                    ReportResult(result);
                }
                catch (OperationCanceledException)
                {
                    lblStatus.Text = "Operación cancelada";
                }
                catch (Exception ex)
                {
                    AppLog.Error("BtnCreate_Click", ex);
                    var message = $"Error al crear PDF: {ex.GetBaseException()?.Message ?? ex.Message}";
                    SetStatus("Error al crear PDF");
                    if (!_closeRequested)
                        MessageBox.Show(message, "Opti-PDF", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                finally
                {
                    progressBar.Visible = false;
                    progressBar.MarqueeAnimationSpeed = 0;
                    progressBar.Style = ProgressBarStyle.Continuous;

                    _cts?.Dispose();
                    _cts = null;
                    SetInputEnabled(true);

                    if (_closeRequested)
                        Close();
                }
            }
        }

        private void BtnCancel_Click(object sender, EventArgs e)
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                lblStatus.Text = "Cancelando...";
                _cts.Cancel();
                btnCancel.Enabled = false;
            }
        }

        /// <summary>
        /// Enables or disables every route into <see cref="_pages"/>.
        /// Drag-and-drop is included: the worker iterates the list from a
        /// background thread and List&lt;T&gt; is not thread-safe, so a drop
        /// during conversion would race the pipeline.
        /// </summary>
        private void SetInputEnabled(bool enabled)
        {
            btnCreate.Enabled = enabled;
            btnAdd.Enabled = enabled;
            btnRemove.Enabled = enabled;
            btnClear.Enabled = enabled;
            btnCancel.Enabled = !enabled;

            AllowDrop = enabled;
            lvImages.AllowDrop = enabled;
        }

        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (!IsBusy)
                return;

            // Never leave the worker running against a disposed form: cancel
            // it and close once BtnCreate_Click has unwound.
            e.Cancel = true;
            _closeRequested = true;

            if (!_cts.IsCancellationRequested)
            {
                _cts.Cancel();
                btnCancel.Enabled = false;
                lblStatus.Text = "Cancelando...";
            }
        }

        /// <summary>
        /// Reports the outcome honestly: a PDF that could not be brought under
        /// the cap is still saved, but is never announced as a plain success.
        /// </summary>
        private void ReportResult(OptimizationResult result)
        {
            var sizeMb = result.SizeBytes / (1024.0 * 1024.0);
            var limitMb = PdfOptimizer.SizeLimitBytes / (1024.0 * 1024.0);

            if (result.WithinLimit)
            {
                lblStatus.Text = $"PDF creado exitosamente ({sizeMb:0.##} MB)";

                if (!_closeRequested)
                {
                    MessageBox.Show(
                        $"PDF creado exitosamente.{Environment.NewLine}Tamaño final: {sizeMb:0.##} MB.",
                        "Opti-PDF",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                }

                return;
            }

            lblStatus.Text = $"PDF creado, pero supera el límite ({sizeMb:0.##} MB)";

            if (!_closeRequested)
            {
                MessageBox.Show(
                    $"No fue posible bajar de {limitMb:0.##} MB sin cruzar los límites de calidad "
                        + $"(calidad {PdfOptimizer.MinQuality}, {PdfOptimizer.MinDpi} PPP)."
                        + $"{Environment.NewLine}Se guardó la mejor versión obtenida: {sizeMb:0.##} MB.",
                    "Opti-PDF",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void BtnAdd_Click(object sender, EventArgs e)
        {
            using (var dlg = new OpenFileDialog())
            {
                dlg.Multiselect = true;
                dlg.Filter = "Imágenes|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff;*.gif";
                if (dlg.ShowDialog() != DialogResult.OK)
                    return;

                AddFiles(dlg.FileNames);
            }
        }

        private void BtnRemove_Click(object sender, EventArgs e)
        {
            var items = lvImages.SelectedItems.Cast<ListViewItem>().ToArray();
            foreach (var it in items)
            {
                var key = it.Tag as string;
                if (!string.IsNullOrEmpty(key))
                {
                    _pages.RemoveAll(p => string.Equals(p.Key, key, StringComparison.OrdinalIgnoreCase));
                    if (imgListSmall.Images.ContainsKey(key))
                        imgListSmall.Images.RemoveByKey(key);
                    if (imgListLarge.Images.ContainsKey(key))
                        imgListLarge.Images.RemoveByKey(key);
                }

                lvImages.Items.Remove(it);
            }

            lblStatus.Text = $"Se eliminaron {items.Length} elemento(s)";
        }

        private void BtnClear_Click(object sender, EventArgs e)
        {
            _pages.Clear();
            lvImages.Items.Clear();
            imgListSmall.Images.Clear();
            imgListLarge.Images.Clear();
            lblStatus.Text = "Todas las imágenes eliminadas";
        }

        private void Form1_DragEnter(object sender, DragEventArgs e)
        {
            if (!IsBusy && e.Data != null && e.Data.GetDataPresent(DataFormats.FileDrop))
                e.Effect = DragDropEffects.Copy;
            else
                e.Effect = DragDropEffects.None;
        }

        private void Form1_DragDrop(object sender, DragEventArgs e)
        {
            if (IsBusy || e.Data == null || !e.Data.GetDataPresent(DataFormats.FileDrop))
                return;

            var files = (string[])e.Data.GetData(DataFormats.FileDrop);
            var images = files.Where(f =>
            {
                var ext = Path.GetExtension(f)?.ToLowerInvariant();
                return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".tif" || ext == ".tiff" || ext == ".gif";
            }).ToArray();

            if (images.Length > 0)
                AddFiles(images);
        }

        private void AddFiles(IEnumerable<string> files)
        {
            var added = 0;

            foreach (var file in files)
            {
                if (string.IsNullOrWhiteSpace(file))
                    continue;

                IEnumerable<PageSource> pages;
                try
                {
                    pages = PageSource.FromFile(file);
                }
                catch (Exception ex)
                {
                    AppLog.Error($"Enumerar {Path.GetFileName(file)}", ex);
                    lblStatus.Text = $"Advertencia: no se pudo leer {Path.GetFileName(file)} ({ex.Message})";
                    continue;
                }

                foreach (var page in pages)
                {
                    if (_pages.Any(p => string.Equals(p.Key, page.Key, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    try
                    {
                        AddThumbnails(page);

                        var item = new ListViewItem
                        {
                            Text = page.DisplayName,
                            ImageKey = page.Key,
                            Tag = page.Key
                        };

                        lvImages.Items.Add(item);
                        _pages.Add(page);
                        added++;
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error($"Miniatura {page.DisplayName}", ex);
                        lblStatus.Text = $"Advertencia: no se pudo cargar {page.DisplayName} ({ex.Message})";
                    }
                }
            }

            lblStatus.Text = $"Se agregaron {added} página(s)";
        }

        private void AddThumbnails(PageSource page)
        {
            using (var original = new MagickImage(page.Path, page.CreateReadSettings()))
            {
                // Match what the output will look like: without this the
                // thumbnail of a rotated phone photo disagrees with the page.
                original.AutoOrient();

                AddThumbnail(imgListSmall, original, page.Key);
                AddThumbnail(imgListLarge, original, page.Key);
            }
        }

        private static void AddThumbnail(ImageList list, MagickImage source, string key)
        {
            using (var thumb = (MagickImage)source.Clone())
            {
                thumb.Resize((uint)list.ImageSize.Width, (uint)list.ImageSize.Height);

                using (var ms = new MemoryStream())
                {
                    thumb.Write(ms, MagickFormat.Bmp);
                    ms.Position = 0;

                    // ImageList copies the bitmap into its own handle, so the
                    // temporary has to be disposed here or it leaks a GDI object.
                    using (var bmp = new Bitmap(ms))
                    {
                        list.Images.Add(key, bmp);
                    }
                }
            }
        }

        private void Form1_Load(object sender, EventArgs e)
        {
            AllowDrop = true;
            lvImages.AllowDrop = true;
            progressBar.Visible = false;
            progressBar.MarqueeAnimationSpeed = 0;
            progressBar.Style = ProgressBarStyle.Continuous;
            lblStatus.Text = "LISTO";
        }

        private void SetStatus(string text)
        {
            if (InvokeRequired)
            {
                BeginInvoke((Action)(() => lblStatus.Text = text));
                return;
            }

            lblStatus.Text = text;
        }
    }
}

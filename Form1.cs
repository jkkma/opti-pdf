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

                const int automaticQuality = 80;
                const int automaticMaxDpi = 300;

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
                    await Task.Run(
                        () => CreateOptimizedPdfWithImageMagick(
                            dlg.FileName,
                            automaticQuality,
                            automaticMaxDpi,
                            progress,
                            detailProgress,
                            _cts.Token),
                        _cts.Token);

                    lblStatus.Text = "PDF creado exitosamente";
                    if (!_closeRequested)
                        MessageBox.Show("PDF creado exitosamente.", "Opti-PDF", MessageBoxButtons.OK, MessageBoxIcon.Information);
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

        private void CreateOptimizedPdfWithImageMagick(
            string outputPath,
            int initialQuality,
            int requestedMaxDpi,
            IProgress<int> progress,
            IProgress<string> detailProgress,
            CancellationToken cancellationToken)
        {
            const long sizeLimitBytes = 5L * 1024 * 1024;
            const int minDpi = 150;
            const int minQuality = 40;

            if (_pages.Count == 0)
                throw new InvalidOperationException("No hay imágenes para procesar.");

            progress?.Report(0);

            var effectiveDpi = Math.Max(requestedMaxDpi, 300);
            var quality = Math.Max(minQuality, Math.Min(100, initialQuality));

            var tempDir = Path.GetTempPath();
            var tempFileBase = Path.Combine(tempDir, "opti_pdf_attempt_" + Guid.NewGuid().ToString("N"));
            string bestTemp = null;
            long bestSize = long.MaxValue;
            var outputWritten = false;

            void PromoteToOutput(string tempPath)
            {
                if (string.IsNullOrEmpty(tempPath) || !File.Exists(tempPath))
                    return;

                if (File.Exists(outputPath))
                    File.Delete(outputPath);

                File.Move(tempPath, outputPath);
                outputWritten = true;
                progress?.Report(100);
            }

            long RunPdfPass(string tempPath, int effectiveDpiParam, int baseQuality, int aggressiveness, bool gentle)
            {
                var total = _pages.Count;
                var processed = 0;
                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = Math.Max(1, Math.Min(4, Environment.ProcessorCount)),
                    CancellationToken = cancellationToken
                };
                var localFirstError = default(Exception);
                var pageBuffers = new byte[total][];

                Parallel.For(0, total, options, idx =>
                {
                    options.CancellationToken.ThrowIfCancellationRequested();
                    var page = _pages[idx];
                    try
                    {
                        var readSettings = page.CreateReadSettings();
                        readSettings.ColorSpace = ColorSpace.sRGB;
                        var originalLength = new FileInfo(page.Path).Length;

                        using (var src = new MagickImage(page.Path, readSettings))
                        {
                            var srcDpiX = src.Density?.X ?? 72;
                            var srcDpiY = src.Density?.Y ?? 72;
                            var srcDpi = Math.Max(srcDpiX, srcDpiY);
                            if (srcDpi <= 0)
                                srcDpi = 72;

                            var desiredDpi = gentle
                                ? requestedMaxDpi
                                : Math.Max(minDpi, effectiveDpiParam - aggressiveness * 50);
                            var targetDpi = (int)Math.Max(1, Math.Min(desiredDpi, srcDpi));
                            var targetWasClamped = targetDpi < desiredDpi;

                            double scale = 1.0;
                            if (!gentle && targetDpi < srcDpi)
                            {
                                scale = (double)targetDpi / srcDpi;
                            }

                            var newWidth = (int)Math.Max(1, Math.Round(src.Width * scale));
                            var newHeight = (int)Math.Max(1, Math.Round(src.Height * scale));

                            using (var img = (MagickImage)src.Clone())
                            {
                                if (!gentle && (scale < 0.999 || scale > 1.001))
                                {
                                    img.FilterType = FilterType.Cubic;
                                    img.Resize((uint)newWidth, (uint)newHeight);
                                }

                                if (gentle)
                                {
                                    img.Strip();
                                    img.ColorSpace = ColorSpace.sRGB;
                                    img.Density = new Density(targetDpi);
                                    img.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "4:2:0");
                                    img.Settings.SetDefine(MagickFormat.Jpeg, "interlace", "none");
                                    img.Quality = (uint)baseQuality;
                                }
                                else
                                {
                                    var attemptQuality = Math.Max(minQuality, baseQuality - aggressiveness * 12);
                                    ApplyLegalOptimizations(img, attemptQuality, targetDpi, aggressiveness);
                                }

                                using (var ms = new MemoryStream())
                                {
                                    img.Format = MagickFormat.Jpeg;
                                    img.Write(ms);
                                    pageBuffers[idx] = ms.ToArray();

                                    var newLength = ms.Length;
                                    var reductionPct = originalLength > 0
                                        ? (100.0 * (originalLength - newLength) / originalLength)
                                        : 0.0;
                                    var aggressivenessLabel = gentle
                                        ? "Nivel 0 (suave)"
                                        : $"Nivel {Math.Max(1, aggressiveness + 1)}";
                                    var clampSuffix = targetWasClamped ? " (limitado por DPI de origen)" : string.Empty;
                                    detailProgress?.Report(
                                        $"[{idx + 1}/{total}] Agresividad: {aggressivenessLabel} - {page.DisplayName} - DPI {srcDpi:0}->{targetDpi}{clampSuffix} - {originalLength / 1024.0:0.#}KB -> {newLength / 1024.0:0.#}KB ({reductionPct:0.#}% ahorro)");
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Interlocked.CompareExchange(ref localFirstError, ex, null);
                        AppLog.Error($"Imagen {page.DisplayName}", ex);
                        detailProgress?.Report(
                            $"[{idx + 1}/{total}] Error en {page.DisplayName}: {ex.Message}");
                    }
                    finally
                    {
                        var done = Interlocked.Increment(ref processed);
                        var percent = (int)Math.Round(done * 80.0 / total);
                        progress?.Report(percent);
                    }
                });

                if (pageBuffers.All(b => b == null))
                    throw localFirstError ?? new InvalidOperationException("No se pudieron procesar las imágenes.");

                progress?.Report(-1);
                using (var collection = new MagickImageCollection())
                {
                    for (var i = 0; i < total; i++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var buffer = pageBuffers[i];
                        if (buffer == null)
                            throw localFirstError ?? new InvalidOperationException($"No se pudo procesar la imagen {i + 1}.");

                        using (var page = new MagickImage(buffer))
                        {
                            collection.Add(page.Clone());
                        }
                    }

                    collection.Write(tempPath, MagickFormat.Pdf);
                }

                progress?.Report(85);

                var fi = new FileInfo(tempPath);
                return fi.Exists ? fi.Length : 0;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // First gentle pass
                var gentleTemp = tempFileBase + "_gentle.pdf";
                var gentleSize = RunPdfPass(gentleTemp, Math.Max(minDpi, requestedMaxDpi), quality, 0, true);
                bestTemp = gentleTemp;
                bestSize = gentleSize;

                if (bestSize <= sizeLimitBytes)
                {
                    PromoteToOutput(bestTemp);
                    return;
                }

                // Quality search at current DPI (find highest quality that fits)
                var lowQ = minQuality;
                var highQ = quality;
                while (lowQ <= highQ && bestSize > sizeLimitBytes)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var midQ = (lowQ + highQ) / 2;
                    var attemptTemp = tempFileBase + $"_q{midQ}.pdf";
                    var attemptSize = RunPdfPass(attemptTemp, effectiveDpi, midQ, 0, false);

                    if (attemptSize <= sizeLimitBytes)
                    {
                        // Fits: keep as best and try higher quality
                        if (attemptSize < bestSize)
                        {
                            if (!string.IsNullOrEmpty(bestTemp) && File.Exists(bestTemp))
                                File.Delete(bestTemp);

                            bestTemp = attemptTemp;
                            bestSize = attemptSize;
                        }
                        else if (File.Exists(attemptTemp))
                        {
                            File.Delete(attemptTemp);
                        }

                        lowQ = midQ + 1;
                    }
                    else
                    {
                        // Too big: try lower quality
                        if (File.Exists(attemptTemp))
                            File.Delete(attemptTemp);

                        highQ = midQ - 1;
                    }
                }

                if (bestSize <= sizeLimitBytes)
                {
                    PromoteToOutput(bestTemp);
                    bestTemp = null;
                    return;
                }

                // Progressive optimizations only if still over 5 MB
                for (var aggressiveness = 0; aggressiveness < 4 && bestSize > sizeLimitBytes; aggressiveness++)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var attemptTemp = tempFileBase + $"_{aggressiveness + 1}.pdf";
                    var attemptSize = RunPdfPass(attemptTemp, effectiveDpi, quality, aggressiveness, false);

                    if (attemptSize < bestSize)
                    {
                        if (!string.IsNullOrEmpty(bestTemp) && File.Exists(bestTemp))
                            File.Delete(bestTemp);

                        bestTemp = attemptTemp;
                        bestSize = attemptSize;
                    }
                    else if (File.Exists(attemptTemp))
                    {
                        File.Delete(attemptTemp);
                    }

                    if (bestSize <= sizeLimitBytes)
                    {
                        PromoteToOutput(bestTemp);
                        bestTemp = null;
                        return;
                    }

                    quality = Math.Max(minQuality, quality - 8);
                    progress?.Report(Math.Min(95, 80 + aggressiveness * 5));
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                AppLog.Error("CreateOptimizedPdfWithImageMagick", ex);
                throw new InvalidOperationException("Fallo al crear el PDF.", ex);
            }
            finally
            {
                if (!outputWritten && !string.IsNullOrEmpty(bestTemp) && File.Exists(bestTemp))
                {
                    PromoteToOutput(bestTemp);
                }
            }
        }

        private static void ApplyLegalOptimizations(MagickImage img, int jpegQuality, int targetDpi, int aggressiveness)
        {
            img.Strip();
            img.ColorSpace = ColorSpace.sRGB;

            if (aggressiveness >= 2)
            {
                try { img.AutoLevel(); } catch { }
                try { img.Normalize(); } catch { }
                for (var i = 0; i < 1 + aggressiveness; i++)
                {
                    try { img.Despeckle(); } catch { }
                }
            }
            else
            {
                try { img.Despeckle(); } catch { }
            }

            img.Density = new Density(targetDpi);
            img.Settings.SetDefine(MagickFormat.Jpeg, "sampling-factor", "4:2:0");
            img.Settings.SetDefine(MagickFormat.Jpeg, "interlace", "none");
            img.Quality = (uint)jpegQuality;
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

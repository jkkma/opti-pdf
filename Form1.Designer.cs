namespace opti_pdf
{
    partial class Form1
    {
        /// <summary>
        /// Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        /// Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        /// Required method for Designer support - do not modify
        /// the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            this.components = new System.ComponentModel.Container();
            this.lvImages = new System.Windows.Forms.ListView();
            this.imgListSmall = new System.Windows.Forms.ImageList(this.components);
            this.imgListLarge = new System.Windows.Forms.ImageList(this.components);
            this.btnAdd = new System.Windows.Forms.Button();
            this.btnRemove = new System.Windows.Forms.Button();
            this.btnClear = new System.Windows.Forms.Button();
            this.lblQuality = new System.Windows.Forms.Label();
            this.tbQuality = new System.Windows.Forms.TrackBar();
            this.numMaxDpi = new System.Windows.Forms.NumericUpDown();
            this.lblDpi = new System.Windows.Forms.Label();
            this.progressBar = new System.Windows.Forms.ProgressBar();
            this.lblStatus = new System.Windows.Forms.Label();
            this.btnCreate = new System.Windows.Forms.Button();
            ((System.ComponentModel.ISupportInitialize)(this.tbQuality)).BeginInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxDpi)).BeginInit();
            this.SuspendLayout();
            // 
            // lvImages
            // 
            this.lvImages.HideSelection = false;
            this.lvImages.LargeImageList = this.imgListLarge;
            this.lvImages.Location = new System.Drawing.Point(18, 238);
            this.lvImages.Name = "lvImages";
            this.lvImages.Size = new System.Drawing.Size(1761, 440);
            this.lvImages.SmallImageList = this.imgListSmall;
            this.lvImages.TabIndex = 0;
            this.lvImages.UseCompatibleStateImageBehavior = false;
            // 
            // imgListSmall
            // 
            this.imgListSmall.ColorDepth = System.Windows.Forms.ColorDepth.Depth8Bit;
            this.imgListSmall.ImageSize = new System.Drawing.Size(64, 64);
            this.imgListSmall.TransparentColor = System.Drawing.Color.Transparent;
            // 
            // imgListLarge
            // 
            this.imgListLarge.ColorDepth = System.Windows.Forms.ColorDepth.Depth8Bit;
            this.imgListLarge.ImageSize = new System.Drawing.Size(128, 128);
            this.imgListLarge.TransparentColor = System.Drawing.Color.Transparent;
            // 
            // btnAdd
            // 
            this.btnAdd.Location = new System.Drawing.Point(18, 90);
            this.btnAdd.Name = "btnAdd";
            this.btnAdd.Size = new System.Drawing.Size(210, 86);
            this.btnAdd.TabIndex = 1;
            this.btnAdd.Text = "Agregar imagenes";
            this.btnAdd.UseVisualStyleBackColor = true;
            // 
            // btnRemove
            // 
            this.btnRemove.Location = new System.Drawing.Point(293, 90);
            this.btnRemove.Name = "btnRemove";
            this.btnRemove.Size = new System.Drawing.Size(233, 86);
            this.btnRemove.TabIndex = 2;
            this.btnRemove.Text = "Remover seleccionados";
            this.btnRemove.UseVisualStyleBackColor = true;
            // 
            // btnClear
            // 
            this.btnClear.Location = new System.Drawing.Point(594, 90);
            this.btnClear.Name = "btnClear";
            this.btnClear.Size = new System.Drawing.Size(227, 86);
            this.btnClear.TabIndex = 3;
            this.btnClear.Text = "Borrar todos";
            this.btnClear.UseVisualStyleBackColor = true;
            // 
            // lblQuality
            // 
            this.lblQuality.AutoSize = true;
            this.lblQuality.Location = new System.Drawing.Point(1014, 710);
            this.lblQuality.Name = "lblQuality";
            this.lblQuality.Size = new System.Drawing.Size(290, 32);
            this.lblQuality.TabIndex = 4;
            this.lblQuality.Text = "Calidad de imagenes:";
            // 
            // tbQuality
            // 
            this.tbQuality.Location = new System.Drawing.Point(1310, 710);
            this.tbQuality.Maximum = 100;
            this.tbQuality.Minimum = 10;
            this.tbQuality.Name = "tbQuality";
            this.tbQuality.Size = new System.Drawing.Size(446, 114);
            this.tbQuality.TabIndex = 5;
            this.tbQuality.Value = 75;
            // 
            // numMaxDpi
            // 
            this.numMaxDpi.Location = new System.Drawing.Point(1236, 118);
            this.numMaxDpi.Maximum = new decimal(new int[] {
            300,
            0,
            0,
            0});
            this.numMaxDpi.Minimum = new decimal(new int[] {
            72,
            0,
            0,
            0});
            this.numMaxDpi.Name = "numMaxDpi";
            this.numMaxDpi.Size = new System.Drawing.Size(120, 38);
            this.numMaxDpi.TabIndex = 6;
            this.numMaxDpi.Value = new decimal(new int[] {
            150,
            0,
            0,
            0});
            // 
            // lblDpi
            // 
            this.lblDpi.AutoSize = true;
            this.lblDpi.Location = new System.Drawing.Point(899, 118);
            this.lblDpi.Name = "lblDpi";
            this.lblDpi.Size = new System.Drawing.Size(315, 32);
            this.lblDpi.TabIndex = 7;
            this.lblDpi.Text = "DPI maximo (comprimir)";
            // 
            // progressBar
            // 
            this.progressBar.Location = new System.Drawing.Point(32, 856);
            this.progressBar.Name = "progressBar";
            this.progressBar.Size = new System.Drawing.Size(936, 97);
            this.progressBar.TabIndex = 8;
            this.progressBar.Visible = false;
            // 
            // lblStatus
            // 
            this.lblStatus.Dock = System.Windows.Forms.DockStyle.Bottom;
            this.lblStatus.Location = new System.Drawing.Point(0, 1016);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(1791, 43);
            this.lblStatus.TabIndex = 9;
            this.lblStatus.Text = "LISTO";
            // 
            // btnCreate
            // 
            this.btnCreate.Font = new System.Drawing.Font("Microsoft Sans Serif", 9.900001F, System.Drawing.FontStyle.Bold, System.Drawing.GraphicsUnit.Point, ((byte)(0)));
            this.btnCreate.Location = new System.Drawing.Point(18, 710);
            this.btnCreate.Name = "btnCreate";
            this.btnCreate.Size = new System.Drawing.Size(320, 86);
            this.btnCreate.TabIndex = 10;
            this.btnCreate.Text = "CREAR PDF OPTIMIZADO";
            this.btnCreate.UseVisualStyleBackColor = true;
            // 
            // Form1
            // 
            this.AllowDrop = true;
            this.AutoScaleDimensions = new System.Drawing.SizeF(16F, 31F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(250)))), ((int)(((byte)(243)))), ((int)(((byte)(235)))));
            this.ClientSize = new System.Drawing.Size(1791, 1059);
            this.Controls.Add(this.btnCreate);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.progressBar);
            this.Controls.Add(this.lblDpi);
            this.Controls.Add(this.numMaxDpi);
            this.Controls.Add(this.tbQuality);
            this.Controls.Add(this.lblQuality);
            this.Controls.Add(this.btnClear);
            this.Controls.Add(this.btnRemove);
            this.Controls.Add(this.btnAdd);
            this.Controls.Add(this.lvImages);
            this.Name = "Form1";
            this.StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            this.Text = "Opti-PDF - Imagen a PDF optimizado";
            this.Load += new System.EventHandler(this.Form1_Load);
            ((System.ComponentModel.ISupportInitialize)(this.tbQuality)).EndInit();
            ((System.ComponentModel.ISupportInitialize)(this.numMaxDpi)).EndInit();
            this.ResumeLayout(false);
            this.PerformLayout();

        }

        #endregion

        private System.Windows.Forms.ListView lvImages;
        private System.Windows.Forms.ImageList imgListSmall;
        private System.Windows.Forms.ImageList imgListLarge;
        private System.Windows.Forms.Button btnAdd;
        private System.Windows.Forms.Button btnRemove;
        private System.Windows.Forms.Button btnClear;
        private System.Windows.Forms.Label lblQuality;
        private System.Windows.Forms.TrackBar tbQuality;
        private System.Windows.Forms.NumericUpDown numMaxDpi;
        private System.Windows.Forms.Label lblDpi;
        private System.Windows.Forms.ProgressBar progressBar;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Button btnCreate;
    }
}


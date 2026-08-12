using System;
using System.Windows.Forms;

namespace opti_pdf
{
    internal static class Program
    {
        /// <summary>
        /// The main entry point for the application.
        /// </summary>
        [STAThread]
        static void Main()
        {
            // Route anything that escapes an event handler or a background
            // thread to the log instead of losing it to a silent process exit.
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (sender, e) => ReportFatal("ThreadException", e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                if (e.ExceptionObject is Exception ex)
                    AppLog.Error("UnhandledException", ex);
            };

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new Form1());
        }

        private static void ReportFatal(string context, Exception ex)
        {
            AppLog.Error(context, ex);

            MessageBox.Show(
                $"Error inesperado: {ex.GetBaseException()?.Message ?? ex.Message}"
                    + $"{Environment.NewLine}{Environment.NewLine}Detalles en: {AppLog.LogPath}",
                "Opti-PDF",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}

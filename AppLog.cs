using System;
using System.IO;

namespace opti_pdf
{
    /// <summary>
    /// Error log shared by the UI and the conversion pipeline.
    /// Writes to %TEMP%\opti_pdf.log and never throws.
    /// </summary>
    internal static class AppLog
    {
        private static readonly object Sync = new object();

        public static string LogPath
        {
            get { return Path.Combine(Path.GetTempPath(), "opti_pdf.log"); }
        }

        public static void Error(string context, Exception ex)
        {
            try
            {
                var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{context}] {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}{Environment.NewLine}";
                lock (Sync)
                {
                    File.AppendAllText(LogPath, line);
                }
            }
            catch
            {
                // Intentionally ignore logging failures.
            }
        }
    }
}

using System;
using System.IO;

namespace AdminInfoTools.Services
{
    public class LogService
    {
        private readonly string _logDirectory;
        private readonly string _logFilePath;
        private static readonly object _lockObj = new object();

        public LogService()
        {
            // Resolves to %UserProfile%\Documents\Log
            string documentsPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            _logDirectory = Path.Combine(documentsPath, "Log");

            // Note: If you want to use C:\Temp initially, swap the above to:
            // _logDirectory = @"C:\Temp";

            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }

            // Creates a daily rollover log file (e.g., AdOperations_20260426.log)
            _logFilePath = Path.Combine(_logDirectory, $"AdOperations_{DateTime.Now:yyyyMMdd}.log");
        }

        /// <summary>
        /// Logs a structured Active Directory operation.
        /// </summary>
        public void LogAdOperation(string operation, string target, string status, string details = "")
        {
            lock (_lockObj) // Thread-safe for async AD calls
            {
                try
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    string logMessage = $"[{timestamp}] | OP: {operation,-15} | TARGET: {target,-20} | STATUS: {status,-10}";
                    
                    if (!string.IsNullOrEmpty(details))
                    {
                        logMessage += $" | DETAILS: {details}";
                    }
                    
                    File.AppendAllText(_logFilePath, logMessage + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    // Failsafe: prevents a logging failure from crashing the main AD operation
                    System.Diagnostics.Debug.WriteLine($"Logging failed: {ex.Message}");
                }
            }
        }
    }
}
using System;
using System.IO;

namespace AdminInfoTools.Services
{
    public class LogService
    {
        private readonly string _logDirectory;
        private readonly string _logFilePath;
        private readonly string _ouLogDirectory;
        private readonly string _ouLogFilePath;
        private readonly string _actionLogDirectory;
        private readonly string _actionLogFilePath;
        private static readonly object _lockObj = new object();

        public LogService()
        {
            string logPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Logs"));
            _logDirectory = Path.Combine(logPath, "ActiveDirectoryOperations");
            _ouLogDirectory = Path.Combine(logPath, "OrganizationalUnitOperations");
            _actionLogDirectory = Path.Combine(logPath, "ComputerActions");

            if (!Directory.Exists(_logDirectory))
            {
                Directory.CreateDirectory(_logDirectory);
            }

            if (!Directory.Exists(_ouLogDirectory))
            {
                Directory.CreateDirectory(_ouLogDirectory);
            }
            
            if (!Directory.Exists(_actionLogDirectory))
            {
                Directory.CreateDirectory(_actionLogDirectory);
            }

            // Creates a daily rollover log file (e.g., ActiveDirectoryOperations_20260426.log)
            _logFilePath = Path.Combine(_logDirectory, $"ActiveDirectoryOperations_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            _ouLogFilePath = Path.Combine(_ouLogDirectory, $"OrganizationalUnitOperations_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            _actionLogFilePath = Path.Combine(_actionLogDirectory, $"ComputerActions_{DateTime.Now:yyyyMMdd_HHmmss}.log");
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

        /// <summary>
        /// Logs a structured Organizational Unit operation.
        /// </summary>
        public void LogOuOperation(string operation, string target, string status, string details = "")
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
                    File.AppendAllText(_ouLogFilePath, logMessage + Environment.NewLine);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Logging failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Logs a general computer action message to the ComputerActions log folder.
        /// </summary>
        public void LogComputerAction(string message)
        {
            lock (_lockObj)
            {
                try
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    File.AppendAllText(_actionLogFilePath, $"[{timestamp}] | {message}{Environment.NewLine}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Logging failed: {ex.Message}");
                }
            }
        }
    }
}
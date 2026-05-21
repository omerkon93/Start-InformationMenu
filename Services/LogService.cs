using System;
using System.Collections.Concurrent;
using System.IO;

namespace AdminInfoTools.Services
{
    public enum LogCategory
    {
        ActiveDirectoryOperations,
        ActiveDirectoryObjectQuery,
        ComputerObjectGenerated,
        ComputerObjectList,
        ComputerObjectModified,
        UserObjectModified,
        OrganizationalUnitOperations,
        RemoteComputerActions,
        RemoteComputerActionsDetailed
    }

    public class LogService
    {
        private readonly string _baseLogPath;
        private static readonly object _lockObj = new object();
        private readonly ConcurrentDictionary<LogCategory, string> _sessionLogFiles = new ConcurrentDictionary<LogCategory, string>();

        public LogService()
        {
            _baseLogPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Logs"));
        }

        public string GetLogDirectory(LogCategory category)
        {
            string dir = Path.Combine(_baseLogPath, category.ToString());
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            return dir;
        }

        public string GetSessionLogFilePath(LogCategory category)
        {
            return _sessionLogFiles.GetOrAdd(category, cat => 
            {
                string dir = GetLogDirectory(cat);
                return Path.Combine(dir, $"{cat}_{DateTime.Now:yyyyMMdd_HHmmss}.log");
            });
        }

        public string GetNewFilePath(LogCategory category, string prefix, string extension = ".txt")
        {
            string dir = GetLogDirectory(category);
            return Path.Combine(dir, $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}{extension}");
        }

        /// <summary>
        /// Logs a structured Active Directory operation.
        /// </summary>
        public void LogAdOperation(string operation, string target, string status, string details = "")
        {
            string filePath = GetSessionLogFilePath(LogCategory.ActiveDirectoryOperations);
            AppendToLog(filePath, operation, target, status, details);
        }

        /// <summary>
        /// Logs a structured Organizational Unit operation.
        /// </summary>
        public void LogOuOperation(string operation, string target, string status, string details = "")
        {
            string adFilePath = GetSessionLogFilePath(LogCategory.ActiveDirectoryOperations);
            string ouFilePath = GetSessionLogFilePath(LogCategory.OrganizationalUnitOperations);
            
            string logMessage = BuildLogMessage(operation, target, status, details);
            
            lock (_lockObj) // Thread-safe for async AD calls
            {
                try
                {
                    File.AppendAllText(adFilePath, logMessage);
                    File.AppendAllText(ouFilePath, logMessage);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Logging failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Logs a structured User Object Modification operation to both the general AD log and the specific User log.
        /// </summary>
        public void LogUserModified(string operation, string target, string status, string details = "")
        {
            string adFilePath = GetSessionLogFilePath(LogCategory.ActiveDirectoryOperations);
            string userFilePath = GetSessionLogFilePath(LogCategory.UserObjectModified);
            
            string logMessage = BuildLogMessage(operation, target, status, details);
            
            lock (_lockObj)
            {
                try
                {
                    File.AppendAllText(adFilePath, logMessage);
                    File.AppendAllText(userFilePath, logMessage);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Logging failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Logs a structured Computer Object Modification operation to both the general AD log and the specific Computer log.
        /// </summary>
        public void LogComputerModified(string operation, string target, string status, string details = "")
        {
            string adFilePath = GetSessionLogFilePath(LogCategory.ActiveDirectoryOperations);
            string compFilePath = GetSessionLogFilePath(LogCategory.ComputerObjectModified);
            
            string logMessage = BuildLogMessage(operation, target, status, details);
            
            lock (_lockObj)
            {
                try
                {
                    File.AppendAllText(adFilePath, logMessage);
                    File.AppendAllText(compFilePath, logMessage);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Logging failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Logs a general computer action message to the RemoteComputerActions log folder.
        /// </summary>
        public void LogComputerAction(string message)
        {
            string filePath = GetSessionLogFilePath(LogCategory.RemoteComputerActions);
            lock (_lockObj)
            {
                try
                {
                    string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
                    File.AppendAllText(filePath, $"[{timestamp}] | {message}{Environment.NewLine}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Logging failed: {ex.Message}");
                }
            }
        }

        /// <summary>
        /// Logs detailed terminal output for remote computer actions.
        /// </summary>
        public void LogComputerActionDetailed(string message)
        {
            string filePath = GetSessionLogFilePath(LogCategory.RemoteComputerActionsDetailed);
            lock (_lockObj)
            {
                try
                {
                    File.AppendAllText(filePath, $"{message}{Environment.NewLine}");
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Logging failed: {ex.Message}");
                }
            }
        }

        private string BuildLogMessage(string operation, string target, string status, string details)
        {
            string timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");
            string logMessage = $"[{timestamp}] | OP: {operation,-15} | TARGET: {target,-20} | STATUS: {status,-10}";
            
            if (!string.IsNullOrEmpty(details))
            {
                logMessage += $" | DETAILS: {details}";
            }
            return logMessage + Environment.NewLine;
        }

        private void AppendToLog(string filePath, string operation, string target, string status, string details)
        {
            string logMessage = BuildLogMessage(operation, target, status, details);
            lock (_lockObj)
            {
                try
                {
                    File.AppendAllText(filePath, logMessage);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"Logging failed: {ex.Message}");
                }
            }
        }

        public string SaveBulkLog(LogCategory category, string[] lines, string prefix)
        {
            string filePath = GetNewFilePath(category, prefix);
            File.WriteAllLines(filePath, lines);
            return filePath;
        }

        public string SaveTextLog(LogCategory category, string content, string prefix)
        {
            string filePath = GetNewFilePath(category, prefix);
            File.WriteAllText(filePath, content);
            return filePath;
        }
    }
}
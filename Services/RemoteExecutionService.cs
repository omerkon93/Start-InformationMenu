using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace AdminInfoTools.Services
{
    public class RemoteExecutionService
    {
        private readonly LogService _logger;
        private readonly string _psExecPath;
        private readonly string _username;
        private readonly string _password;

        // Pass in the configured path from your ConfigurationService
        public RemoteExecutionService(LogService logger, string psExecPath, string username = null, string password = null)
        {
            _logger = logger;
            
            // Fallback to system PATH if the config path doesn't exist
            _psExecPath = File.Exists(psExecPath) ? psExecPath : "psexec.exe";
            _username = username;
            _password = password;
        }

        /// <summary>
        /// Equivalent to Invoke-PsExecCommand: Runs an ad-hoc command remotely.
        /// </summary>
        public async Task<string> RunRemoteCommandAsync(string computerName, string command, bool usePowerShell = false)
        {
            string args = $"\\\\{computerName} -accepteula -h";

            if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password))
            {
                args += $" -u \"{_username}\" -p \"{_password}\"";
            }

            args += usePowerShell 
                ? $" powershell -NoProfile -ExecutionPolicy Bypass -Command \"{command.Replace("\"", "\\\"")}\""
                : $" cmd /c {command}";

            return await ExecuteProcessAsync(_psExecPath, args, computerName, "AdHocCommand");
        }

        /// <summary>
        /// Equivalent to Install-Script: Copies and executes a .bat, or remotely invokes a .ps1
        /// </summary>
        public async Task<string> RunRemoteScriptAsync(string computerName, string localFilePath)
        {
            if (!File.Exists(localFilePath))
                throw new FileNotFoundException($"Script not found: {localFilePath}");

            string extension = Path.GetExtension(localFilePath).ToLower();
            string output = "";

            if (extension == ".ps1")
            {
                // Native remote execution for PowerShell (no PsExec needed)
                string psArgs = "-NoProfile -Command \"";
                
                // If credentials are provided, build a PSCredential object inline
                if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password))
                {
                    psArgs += $"$pw = ConvertTo-SecureString '{_password.Replace("'", "''")}' -AsPlainText -Force; ";
                    psArgs += $"$cred = New-Object System.Management.Automation.PSCredential ('{_username.Replace("'", "''")}', $pw); ";
                }
                
                psArgs += $"Invoke-Command -ComputerName {computerName} -FilePath '{localFilePath}'";
                
                if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password))
                {
                    psArgs += " -Credential $cred";
                }
                psArgs += "\"";

                output = await ExecuteProcessAsync("powershell.exe", psArgs, computerName, "InvokePs1");
            }
            else
            {
                // PsExec -c copies the file, runs it, and deletes it from the target
                string args = $"\\\\{computerName} -accepteula -h";
                if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password))
                {
                    args += $" -u \"{_username}\" -p \"{_password}\"";
                }
                args += $" -c \"{localFilePath}\"";
                
                output = await ExecuteProcessAsync(_psExecPath, args, computerName, "InvokeBat");
            }

            return output;
        }

        /// <summary>
        /// Executes a native PowerShell script remotely using WinRM (Invoke-Command)
        /// </summary>
        public async Task<string> RunRemoteScriptWinRMAsync(string computerName, string localFilePath, string username, string password)
        {
            if (!System.IO.File.Exists(localFilePath))
                return $"ERROR: Script not found at {localFilePath}";

            string psArgs;

            // If credentials are provided (Cross-Domain or explicit Admin)
            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                // Escape single quotes in password just in case
                string safePassword = password.Replace("'", "''"); 
                
                psArgs = $"-NoProfile -ExecutionPolicy Bypass -Command \"$secPass = ConvertTo-SecureString '{safePassword}' -AsPlainText -Force; $cred = New-Object System.Management.Automation.PSCredential ('{username}', $secPass); Invoke-Command -ComputerName {computerName} -Credential $cred -FilePath '{localFilePath}'\"";
            }
            else
            {
                // Implicit credentials (if app is run as Domain Admin on a Domain PC)
                psArgs = $"-NoProfile -ExecutionPolicy Bypass -Command \"Invoke-Command -ComputerName {computerName} -FilePath '{localFilePath}'\"";
            }

            return await ExecuteProcessAsync("powershell.exe", psArgs, computerName, "WinRM-Script");
        }

        /// <summary>
        /// Executes an Ad-Hoc command remotely using WinRM (Invoke-Command)
        /// </summary>
        public async Task<string> RunRemoteCommandWinRMAsync(string computerName, string command, string username, string password)
        {
            string psArgs;

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
            {
                string safePassword = password.Replace("'", "''");
                
                psArgs = $"-NoProfile -ExecutionPolicy Bypass -Command \"$secPass = ConvertTo-SecureString '{safePassword}' -AsPlainText -Force; $cred = New-Object System.Management.Automation.PSCredential ('{username}', $secPass); Invoke-Command -ComputerName {computerName} -Credential $cred -ScriptBlock {{ {command} }}\"";
            }
            else
            {
                psArgs = $"-NoProfile -ExecutionPolicy Bypass -Command \"Invoke-Command -ComputerName {computerName} -ScriptBlock {{ {command} }}\"";
            }

            return await ExecuteProcessAsync("powershell.exe", psArgs, computerName, "WinRM-Command");
        }

        private async Task<string> ExecuteProcessAsync(string fileName, string arguments, string target, string operationName)
        {
            _logger.LogAdOperation(operationName, target, "STARTED", arguments);

            try
            {
                using Process process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName,
                        Arguments = arguments,
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        RedirectStandardInput = true,
                        CreateNoWindow = true
                    }
                };

                process.Start();

                // Close the input immediately so PsExec knows we aren't sending keystrokes
                process.StandardInput.Close();

                // Read output asynchronously to prevent freezing
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();

                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    _logger.LogAdOperation(operationName, target, "SUCCESS");
                    return output;
                }
                else
                {
                    string errorMsg = string.IsNullOrWhiteSpace(error) ? output : error;
                    _logger.LogAdOperation(operationName, target, "FAILED", $"Exit Code {process.ExitCode}: {errorMsg.Trim()}");
                    return $"ERROR: {errorMsg}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogAdOperation(operationName, target, "ERROR", ex.Message);
                return $"EXCEPTION: {ex.Message}";
            }
        }
    }
}
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using AdminInfoTools.Helpers;
using AdminInfoTools.Services;

namespace AdminInfoTools.ViewModels
{
    public class ComputerActionsViewModel : ViewModelBase, IDisposable
    {
        private readonly ActiveDirectoryService _adService;
        private readonly ConfigurationService _configService;
        private readonly CredentialService _credentialService;
        private readonly LogService _logger;

        public Action<string> UpdateStatus { get; set; }

        public Dictionary<string, string> SpecialOUs => _configService.CurrentSettings?.ActiveDirectory?.SpecialOUs;

        private string _targetHostnamesText;
        public string TargetHostnamesText
        {
            get => _targetHostnamesText;
            set { _targetHostnamesText = value; OnPropertyChanged(); }
        }

        private string _selectedLoadOuValue;
        public string SelectedLoadOuValue
        {
            get => _selectedLoadOuValue;
            set { _selectedLoadOuValue = value; OnPropertyChanged(); }
        }

        private string _outputText;
        public string OutputText
        {
            get => _outputText;
            set { _outputText = value; OnPropertyChanged(); }
        }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        public ICommand LoadFromOuCommand { get; }
        public ICommand LoadHostsCommand { get; }
        public ICommand SaveHostsCommand { get; }
        public ICommand PsExecTerminalCommand { get; }
        public ICommand PsExecCommandCommand { get; }
        public ICommand PsTerminalCommand { get; }
        public ICommand PsCommandCommand { get; }
        public ICommand InvokeScriptCommand { get; }

        private Process _terminalProcess;
        private StreamWriter _terminalInput;

        public ComputerActionsViewModel(ActiveDirectoryService adService, ConfigurationService configService, CredentialService credentialService)
        {
            _adService = adService;
            _configService = configService;
            _credentialService = credentialService;
            _logger = new LogService();

            LoadFromOuCommand = new RelayCommand(_ => ExecuteLoadFromOu());
            LoadHostsCommand = new RelayCommand(_ => ExecuteLoadHosts());
            SaveHostsCommand = new RelayCommand(_ => ExecuteSaveHosts());
            
            PsExecTerminalCommand = new RelayCommand(async _ => await ExecuteTerminalSession(false));
            PsTerminalCommand = new RelayCommand(async _ => await ExecuteTerminalSession(true));
            
            PsExecCommandCommand = new RelayCommand(async _ => await ExecuteRoutedRemoteCommand(false));
            PsCommandCommand = new RelayCommand(async _ => await ExecuteRoutedRemoteCommand(true));
            
            InvokeScriptCommand = new RelayCommand(async _ => await ExecuteInvokeScript());
        }

        private void LogMessage(string msg)
        {
            Application.Current.Dispatcher.Invoke(() => Logs.Add($"[{DateTime.Now:HH:mm:ss}] {msg}"));
            _logger.LogComputerAction(msg);
        }
        private void AppendOutput(string msg)
        {
            Application.Current.Dispatcher.Invoke(() => OutputText += msg + Environment.NewLine);
            _logger.LogComputerActionDetailed(msg);
        }

        public void StartTerminalSession(bool usePowerShell = true)
        {
            if (_terminalProcess != null && !_terminalProcess.HasExited)
                return;

            _terminalProcess = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = usePowerShell ? "powershell.exe" : "cmd.exe",
                    Arguments = usePowerShell ? "-NoExit -NoProfile" : "",
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };

            _terminalProcess.OutputDataReceived += (s, e) => { if (e.Data != null) AppendOutput(e.Data); };
            _terminalProcess.ErrorDataReceived += (s, e) => { if (e.Data != null) AppendOutput(e.Data); };

            _terminalProcess.Start();
            _terminalInput = _terminalProcess.StandardInput;
            _terminalProcess.BeginOutputReadLine();
            _terminalProcess.BeginErrorReadLine();
            
            LogMessage($"Started background {(usePowerShell ? "PowerShell" : "Command Prompt")} session.");
        }

        public void SendCommandToTerminal(string command)
        {
            if (_terminalProcess == null || _terminalProcess.HasExited) StartTerminalSession(true);
            _terminalInput.WriteLine(command);
        }

        private async Task<string[]> GetResolvedHostnamesAsync()
        {
            var (resolved, unresolved) = await Task.Run(() => HostResolverHelper.ResolveTargetHosts(TargetHostnamesText));
            
            foreach (var unres in unresolved)
            {
                LogMessage($"Warning: Could not resolve hostname '{unres}'. Proceeding anyway...");
            }
            
            // Return all so execution can still attempt them via IP/NetBIOS natively if DNS failed.
            return resolved.Concat(unresolved).ToArray();
        }

        private string GetFormattedUsername()
        {
            string username = _credentialService.Username;
            if (string.IsNullOrWhiteSpace(username)) return username;

            if (!username.Contains("\\") && !username.Contains("@"))
            {
                string domain = _configService.CurrentSettings?.ActiveDirectory?.DomainName;
                string shortDomain = string.IsNullOrWhiteSpace(domain) ? "DOMAIN" : domain.Split('.')[0].ToUpper();
                username = $"{shortDomain}\\{username}";
            }
            return username;
        }

        private void ExecuteLoadHosts()
        {
            var dlg = new OpenFileDialog { Filter = "Text Files|*.txt" };
            if (dlg.ShowDialog() == true) TargetHostnamesText = File.ReadAllText(dlg.FileName);
        }

        private void ExecuteSaveHosts()
        {
            try
            {
                _logger.SaveTextLog(LogCategory.ComputerObjectList, TargetHostnamesText, "Hosts");
                LogMessage("Hostnames saved successfully.");
            }
            catch (Exception ex) { MessageBox.Show($"Failed to save hostnames: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }

        private void ExecuteLoadFromOu()
        {
            if (string.IsNullOrWhiteSpace(SelectedLoadOuValue)) { MessageBox.Show("Please select an OU.", "Missing Input", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            TargetHostnamesText = string.Join("\r\n", _adService.GetComputersFromOu(_configService.CurrentSettings.ActiveDirectory.DomainName, SelectedLoadOuValue, _credentialService.Username, _credentialService.Password));
        }

        private async Task ExecuteTerminalSession(bool usePowerShell)
        {
            var hosts = await GetResolvedHostnamesAsync();
            if (hosts.Length == 0) return;

            string psExecPath = _configService.CurrentSettings?.ExternalTools?.PsExecPath ?? "psexec.exe";
            
            // Fallback to system PATH if the config path doesn't exist
            if (!File.Exists(psExecPath))
            {
                psExecPath = "psexec.exe";
            }

            string formattedUser = GetFormattedUsername();
            foreach (var pc in hosts)
            {
                LogMessage($"Starting interactive terminal for {pc} in a new window...");
                bool useNative = string.IsNullOrWhiteSpace(_credentialService.Username);
                
                string args = $"\\\\{pc} -accepteula -h" + (!useNative ? $" -u \"{formattedUser}\" -p \"{_credentialService.Password}\"" : "") + (usePowerShell ? " powershell.exe" : " cmd.exe");
                string cmdArgs = $"/k \"\"{psExecPath}\" {args}\"";
                
                try 
                { 
                    Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = cmdArgs, UseShellExecute = true, CreateNoWindow = false }); 
                }
                catch (Exception ex) 
                { 
                    LogMessage($"Failed to start terminal for {pc}: {ex.Message}"); 
                }
            }
        }

        private async Task ExecuteRoutedRemoteCommand(bool usePowerShell)
        {
            var hosts = await GetResolvedHostnamesAsync();
            if (hosts.Length == 0) return;
            string command = DialogHelper.ShowInputDialog($"Enter {(usePowerShell ? "PowerShell" : "Command Prompt")} command to execute:", "Remote Command");
            if (string.IsNullOrWhiteSpace(command))
            {
                LogMessage("Remote command cancelled by user.");
                return;
            }
            bool useNative = string.IsNullOrWhiteSpace(_credentialService.Username);
            string formattedUser = GetFormattedUsername();
            string psExecPath = _configService.CurrentSettings?.ExternalTools?.PsExecPath ?? "psexec.exe";
            
            string credBlock = "";
            if (!useNative && usePowerShell)
            {
                credBlock = $"$pw = ConvertTo-SecureString '{_credentialService.Password}' -AsPlainText -Force; $cred = New-Object System.Management.Automation.PSCredential ('{formattedUser}', $pw); ";
            }

            foreach (var pc in hosts)
            {
                AppendOutput($"--- Routing Command to {pc} ---");
                LogMessage($"Routing {(usePowerShell ? "PS" : "CMD")} command to {pc}...");

                if (usePowerShell)
                {
                    // PowerShell's Invoke-Command is stream-friendly and can be routed to the interactive terminal.
                    StartTerminalSession(true);
                    string targetCmd = $"{credBlock}Invoke-Command -ComputerName {pc} {(useNative ? "" : "-Credential $cred ")} -ScriptBlock {{ {command} }}";
                    SendCommandToTerminal(targetCmd);
                }
                else
                {
                    // PsExec is not stream-friendly. Run it in a dedicated process to capture all output reliably.
                    string args = $"\\\\{pc} -accepteula -h";
                    if (!useNative)
                    {
                        args += $" -u \"{formattedUser}\" -p \"{_credentialService.Password}\"";
                    }
                    args += $" cmd /c \"{command}\"";

                    string result = await RunProcessAndGetOutputAsync(psExecPath, args);
                    AppendOutput(result);
                    LogMessage($"PsExec command on {pc} completed.");
                }
            }
        }

        private async Task<string> RunProcessAndGetOutputAsync(string fileName, string arguments)
        {
            try
            {
                using var process = new Process
                {
                    StartInfo = new ProcessStartInfo
                    {
                        FileName = fileName, Arguments = arguments, UseShellExecute = false,
                        RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true
                    }
                };
                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                string error = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                return string.IsNullOrWhiteSpace(output) ? error : output;
            }
            catch (Exception ex)
            {
                LogMessage($"ERROR running '{Path.GetFileName(fileName)}': {ex.Message}");
                return $"EXCEPTION: Failed to run '{Path.GetFileName(fileName)}'. {ex.Message}";
            }
        }

        private async Task ExecuteInvokeScript()
        {
            var hosts = await GetResolvedHostnamesAsync();
            if (hosts.Length == 0) return;
            
            // 1. UI: Select the Entry Point Script. Its parent folder becomes the Deployment Container.
            var dlg = new OpenFileDialog { Title = "Select Entry Point Script (Parent folder will be deployed)", Filter = "Scripts (*.bat;*.ps1)|*.bat;*.ps1" };
            if (dlg.ShowDialog() != true)
            {
                LogMessage("Script invocation cancelled by user.");
                return;
            }
            
            string entryPointPath = dlg.FileName;
            string entryPointScript = Path.GetFileName(entryPointPath);
            string localFolderPath = Path.GetDirectoryName(entryPointPath);

            // 2. UI: Choose Context
            var runAsResult = MessageBox.Show(
                $"Deploy '{entryPointScript}' to {hosts.Length} host(s) as SYSTEM?\n\nYes = SYSTEM\nNo = Admin (Current User)", 
                "Execution Context", MessageBoxButton.YesNoCancel, MessageBoxImage.Question);
            
            if (runAsResult == MessageBoxResult.Cancel) return;
            string runAsContext = runAsResult == MessageBoxResult.Yes ? "System" : "Admin";

            string formattedUser = GetFormattedUsername();
            bool useNative = string.IsNullOrWhiteSpace(_credentialService.Username);
            
            StartTerminalSession(true); // PowerShell is required for secure script routing
            
            string credBlock = "";
            if (!useNative)
            {
                credBlock = $"$pw = ConvertTo-SecureString '{_credentialService.Password}' -AsPlainText -Force; $cred = New-Object System.Management.Automation.PSCredential ('{formattedUser}', $pw); ";
            }
            
            foreach (var pc in hosts) 
            { 
                LogMessage($"Deploying container to {pc} (RunAs: {runAsContext})...");
                AppendOutput($"--- Deploying Container to {pc} ---");

                string deployGuid = Guid.NewGuid().ToString("N");
                string remoteTempPath = $@"C:\Windows\Temp\Deploy_{deployGuid}";
                string remoteUncPath = $@"\\{pc}\c$\Windows\Temp\Deploy_{deployGuid}";
                string entryPointRemotePath = $@"{remoteTempPath}\{entryPointScript}";
                string localTempScriptPath = Path.Combine(Path.GetTempPath(), $"Deploy_{deployGuid}.ps1");

                // 3. The Deployment Phases (C# to PowerShell generation)
                string psScript = $$"""
                {{credBlock}}
                $ErrorActionPreference = 'Stop'
                $pc = '{{pc}}'
                $localFolder = '{{localFolderPath}}'
                $remoteUncPath = '{{remoteUncPath}}'
                $remoteTempPath = '{{remoteTempPath}}'
                $entryPointPath = '{{entryPointRemotePath}}'
                $runAs = '{{runAsContext}}'
                $useNative = ${{useNative.ToString().ToLower()}}

                try {
                    Write-Host "Phase 1: Staging files to $remoteUncPath..."
                    if (-not $useNative) {
                        New-PSDrive -Name "DeployDrive_{{deployGuid}}" -PSProvider FileSystem -Root "\\$pc\c$" -Credential $cred | Out-Null
                        $mappedUnc = "DeployDrive_{{deployGuid}}:\Windows\Temp\Deploy_{{deployGuid}}"
                        if (-not (Test-Path $mappedUnc)) { New-Item -ItemType Directory -Path $mappedUnc | Out-Null }
                        Copy-Item -Path "$localFolder\*" -Destination $mappedUnc -Recurse -Force
                    } else {
                        if (-not (Test-Path $remoteUncPath)) { New-Item -ItemType Directory -Path $remoteUncPath | Out-Null }
                        Copy-Item -Path "$localFolder\*" -Destination $remoteUncPath -Recurse -Force
                    }

                    Write-Host "Phase 2: Executing ($runAs context)..."
                    if ($runAs -eq 'Admin') {
                        $invokeCmd = {
                            param($scriptPath)
                            & $scriptPath
                        }
                        if ($useNative) {
                            Invoke-Command -ComputerName $pc -ScriptBlock $invokeCmd -ArgumentList $entryPointPath
                        } else {
                            Invoke-Command -ComputerName $pc -Credential $cred -ScriptBlock $invokeCmd -ArgumentList $entryPointPath
                        }
                    }
                    elseif ($runAs -eq 'System') {
                        $sysScriptBlock = {
                            param($scriptPath)
                            $taskName = "DeployTask_$(New-Guid)";
                            $containerPath = Split-Path -Path $scriptPath -Parent;
                            $logPath = Join-Path -Path $containerPath -ChildPath '_deployment.log';
                            $runnerPath = Join-Path -Path $containerPath -ChildPath '_runner.bat';

                            # Create a batch file to execute the PowerShell script and capture all of its output
                            $runnerContent = "@echo off`r`npowershell.exe -ExecutionPolicy Bypass -NoProfile -File `"$scriptPath`" > `"$logPath`" 2>&1";
                            Set-Content -Path $runnerPath -Value $runnerContent;

                            $action = New-ScheduledTaskAction -Execute $runnerPath;
                            $principal = New-ScheduledTaskPrincipal -UserId "NT AUTHORITY\SYSTEM" -LogonType ServiceAccount -RunLevel Highest;
                            $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -StartWhenAvailable;
                            
                            Register-ScheduledTask -TaskName $taskName -Action $action -Principal $principal -Settings $settings | Out-Null;
                            Start-ScheduledTask -TaskName $taskName;
                            
                            # Wait for task to complete, with a timeout
                            $timeout = New-TimeSpan -Minutes 30;
                            $sw = [System.Diagnostics.Stopwatch]::StartNew();
                            Write-Host "Waiting for SYSTEM task to complete (timeout: $($timeout.TotalMinutes) minutes)...";
                            
                            while ($sw.Elapsed -lt $timeout) {
                                $task = Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue;
                                if ($null -eq $task -or $task.State -ne 'Running') {
                                    break;
                                }
                                Start-Sleep -Seconds 5;
                            }
                            
                            if ($sw.Elapsed -ge $timeout) {
                                Write-Warning "Task timed out after $($timeout.TotalMinutes) minutes.";
                                try { Stop-ScheduledTask -TaskName $taskName -Confirm:$false } catch {};
                            }
                            
                            Start-Sleep -Seconds 2; # Allow time for log file to be written

                            $taskResult = Get-ScheduledTaskInfo -TaskName $taskName;
                            Write-Host "Task finished. Last result code: $($taskResult.LastTaskResult)";

                            if (Test-Path $logPath) {
                                Write-Host "--- Remote Execution Log from '$logPath' ---";
                                Get-Content $logPath -ErrorAction SilentlyContinue;
                                Write-Host "--- End Remote Log ---";
                            } else {
                                Write-Warning "Execution log file not found.";
                            }
                            
                            Unregister-ScheduledTask -TaskName $taskName -Confirm:$false;
                        }
                        
                        if ($useNative) {
                            Invoke-Command -ComputerName $pc -ScriptBlock $sysScriptBlock -ArgumentList $entryPointPath
                        } else {
                            Invoke-Command -ComputerName $pc -Credential $cred -ScriptBlock $sysScriptBlock -ArgumentList $entryPointPath
                        }
                    }
                }
                catch {
                    Write-Error "Deployment failed on $pc : $_"
                }
                finally {
                    Write-Host "Phase 3: Cleanup..."
                    Start-Sleep -Seconds 2
                    $retry = 0
                    $cleaned = $false
                    while ($retry -lt 3 -and -not $cleaned) {
                        try {
                            if (-not $useNative) {
                                $mappedUnc = "DeployDrive_{{deployGuid}}:\Windows\Temp\Deploy_{{deployGuid}}"
                                if (Test-Path $mappedUnc) { Remove-Item -Path $mappedUnc -Recurse -Force -ErrorAction Stop }
                                Remove-PSDrive -Name "DeployDrive_{{deployGuid}}" -ErrorAction SilentlyContinue
                            } else {
                                if (Test-Path $remoteUncPath) { Remove-Item -Path $remoteUncPath -Recurse -Force -ErrorAction Stop }
                            }
                            $cleaned = $true
                        } catch {
                            $retry++
                            Start-Sleep -Seconds 3
                        }
                    }
                    if (-not $cleaned) {
                        Write-Warning "Failed to cleanup $remoteUncPath after multiple attempts. A file may still be in use."
                    }
                    
                    try {
                        $localTemp = '{{localTempScriptPath}}'
                        if (Test-Path $localTemp) { Remove-Item -Path $localTemp -Force -ErrorAction SilentlyContinue }
                    } catch {}
                }
                """;

                // 4. Save to a temporary script file to bypass the 8192-character stdin limit
                File.WriteAllText(localTempScriptPath, psScript);
                SendCommandToTerminal($"powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"{localTempScriptPath}\"");
            }
        }

        public void Dispose()
        {
            if (_terminalProcess != null && !_terminalProcess.HasExited)
            {
                try
                {
                    _terminalProcess.Kill();
                    _terminalProcess.Dispose();
                }
                catch { }
            }
        }
    }
}
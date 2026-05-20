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
        private void AppendOutput(string msg) => Application.Current.Dispatcher.Invoke(() => OutputText += msg + Environment.NewLine);

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
                Directory.CreateDirectory(@"C:\Projects\Start-InformationMenu\cs\Logs\ComputerObjectList\");
                File.WriteAllText($@"C:\Projects\Start-InformationMenu\cs\Logs\ComputerObjectList\Hosts_{DateTime.Now:yyyyMMdd_HHmmss}.txt", TargetHostnamesText);
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
            var dlg = new OpenFileDialog { Filter = "Scripts (*.bat;*.ps1)|*.bat;*.ps1" };
            if (dlg.ShowDialog() != true)
            {
                LogMessage("Script invocation cancelled by user.");
                return;
            }
            
            string scriptName = Path.GetFileName(dlg.FileName);
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
                LogMessage($"Routing script '{scriptName}' to {pc}...");
                string targetCmd = $"{credBlock}Invoke-Command -ComputerName {pc} {(useNative ? "" : "-Credential $cred ")} -FilePath \"{dlg.FileName}\"";
                AppendOutput($"--- Routing Script to {pc} ---");
                SendCommandToTerminal(targetCmd);
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
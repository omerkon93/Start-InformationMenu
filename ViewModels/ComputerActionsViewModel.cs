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
    public class ComputerActionsViewModel : ViewModelBase
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

        public ComputerActionsViewModel(ActiveDirectoryService adService, ConfigurationService configService, CredentialService credentialService)
        {
            _adService = adService;
            _configService = configService;
            _credentialService = credentialService;
            _logger = new LogService();

            LoadFromOuCommand = new RelayCommand(_ => ExecuteLoadFromOu());
            LoadHostsCommand = new RelayCommand(_ => ExecuteLoadHosts());
            SaveHostsCommand = new RelayCommand(_ => ExecuteSaveHosts());
            
            PsExecTerminalCommand = new RelayCommand(async _ => await ExecuteInteractiveTerminal(false));
            PsTerminalCommand = new RelayCommand(async _ => await ExecuteInteractiveTerminal(true));
            
            PsExecCommandCommand = new RelayCommand(async _ => await ExecuteRemoteCommand(false));
            PsCommandCommand = new RelayCommand(async _ => await ExecuteRemoteCommand(true));
            
            InvokeScriptCommand = new RelayCommand(async _ => await ExecuteInvokeScript());
        }

        private void LogMessage(string msg) => Application.Current.Dispatcher.Invoke(() => Logs.Add($"[{DateTime.Now:HH:mm:ss}] {msg}"));
        private void AppendOutput(string msg) => Application.Current.Dispatcher.Invoke(() => OutputText += msg + Environment.NewLine);

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

        private async Task ExecuteInteractiveTerminal(bool usePowerShell)
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
                LogMessage($"Starting interactive terminal for {pc}...");
                bool useNative = string.IsNullOrWhiteSpace(_credentialService.Username);
                
                string maskedArgs = $"\\\\{pc} -accepteula -h" + (!useNative ? $" -u \"{formattedUser}\" -p \"********\"" : "") + (usePowerShell ? " powershell.exe" : " cmd.exe");
                LogMessage($"Command: psexec {maskedArgs}");

                string args = $"\\\\{pc} -accepteula -h" + (!useNative ? $" -u \"{formattedUser}\" -p \"{_credentialService.Password}\"" : "") + (usePowerShell ? " powershell.exe" : " cmd.exe");
                string cmdArgs = $"/k \"\"{psExecPath}\" {args}\"";
                try { Process.Start(new ProcessStartInfo { FileName = "cmd.exe", Arguments = cmdArgs, UseShellExecute = true, CreateNoWindow = false }); }
                catch (Exception ex) { LogMessage($"Failed to start terminal for {pc}: {ex.Message}"); }
            }
        }

        private async Task ExecuteRemoteCommand(bool usePowerShell)
        {
            var hosts = await GetResolvedHostnamesAsync();
            if (hosts.Length == 0) return;
            string command = DialogHelper.ShowInputDialog($"Enter {(usePowerShell ? "PowerShell" : "Command Prompt")} command to execute:", "Remote Command");
            if (string.IsNullOrWhiteSpace(command)) return;
            bool useNative = string.IsNullOrWhiteSpace(_credentialService.Username);
            string formattedUser = GetFormattedUsername();
            var svc = new RemoteExecutionService(_logger, _configService.CurrentSettings?.ExternalTools?.PsExecPath ?? "psexec.exe", useNative ? null : formattedUser, useNative ? null : _credentialService.Password);
            OutputText = ""; 
            foreach (var pc in hosts)
            {
                LogMessage($"Executing command on {pc}..."); AppendOutput($"--- Execution on {pc} ---"); UpdateStatus?.Invoke($"Running command on {pc}...");
                string result = await svc.RunRemoteCommandAsync(pc, command, usePowerShell);
                AppendOutput(result); LogMessage($"Command on {pc}: {(result.StartsWith("ERROR") ? "FAILED" : "SUCCESS")}");
            }
            UpdateStatus?.Invoke("Command execution completed.");
        }

        private async Task ExecuteInvokeScript()
        {
            var hosts = await GetResolvedHostnamesAsync();
            if (hosts.Length == 0) return;
            var dlg = new OpenFileDialog { Filter = "Scripts (*.bat;*.ps1)|*.bat;*.ps1" };
            if (dlg.ShowDialog() != true) return;
            string formattedUser = GetFormattedUsername();
            var svc = new RemoteExecutionService(_logger, _configService.CurrentSettings?.ExternalTools?.PsExecPath ?? "psexec.exe", formattedUser, _credentialService.Password);
            OutputText = "";
            foreach (var pc in hosts) { LogMessage($"Invoking script on {pc}..."); AppendOutput($"--- Script Execution on {pc} ---"); UpdateStatus?.Invoke($"Running script on {pc}..."); string result = await svc.RunRemoteScriptAsync(pc, dlg.FileName); AppendOutput(result); LogMessage($"Script on {pc}: {(result.StartsWith("ERROR") ? "FAILED" : "SUCCESS")}"); }
            UpdateStatus?.Invoke("Script execution completed.");
        }
    }
}
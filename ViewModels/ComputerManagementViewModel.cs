using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Microsoft.Win32;
using AdminInfoTools.Models;
using AdminInfoTools.Services;
using AdminInfoTools.Helpers;

namespace AdminInfoTools.ViewModels
{
    public class ComputerManagementViewModel : ViewModelBase
    {
        private readonly ActiveDirectoryService _adService;
        private readonly ConfigurationService _configService;
        private readonly CredentialService _credentialService;
        private readonly SystemInfoService _systemInfoService;
        private readonly LogService _logger;

        public Action<string> UpdateStatus { get; set; }

        // --- Properties ---
        public Dictionary<string, ComputerTypeConfig> ComputerTypes => _configService.CurrentSettings?.ActiveDirectory?.ComputerTypes;
        public Dictionary<string, string> SpecialOUs => _configService.CurrentSettings?.ActiveDirectory?.SpecialOUs;

        private string _targetHostnamesText;
        public string TargetHostnamesText { get => _targetHostnamesText; set { _targetHostnamesText = value; OnPropertyChanged(); } }

        private object _currentResults;
        public object CurrentResults { get => _currentResults; set { _currentResults = value; OnPropertyChanged(); } }

        public ObservableCollection<string> Logs { get; } = new ObservableCollection<string>();

        private string _selectedComputerTypeKey;
        public string SelectedComputerTypeKey { get => _selectedComputerTypeKey; set { _selectedComputerTypeKey = value; OnPropertyChanged(); } }

        private string _selectedTargetOuValue;
        public string SelectedTargetOuValue { get => _selectedTargetOuValue; set { _selectedTargetOuValue = value; OnPropertyChanged(); } }

        private string _selectedLoadOuValue;
        public string SelectedLoadOuValue { get => _selectedLoadOuValue; set { _selectedLoadOuValue = value; OnPropertyChanged(); } }

        private bool _isListCreate = true;
        public bool IsListCreate { get => _isListCreate; set { _isListCreate = value; OnPropertyChanged(); } }

        private bool _isPatternCreate;
        public bool IsPatternCreate { get => _isPatternCreate; set { _isPatternCreate = value; OnPropertyChanged(); } }

        private string _startHostname;
        public string StartHostname { get => _startHostname; set { _startHostname = value; OnPropertyChanged(); } }

        private int _createCount = 1;
        public int CreateCount { get => _createCount; set { _createCount = value; OnPropertyChanged(); } }

        private string _adDescription;
        public string AdDescription { get => _adDescription; set { _adDescription = value; OnPropertyChanged(); } }

        private string _commandText = "ipconfig /flushdns";
        public string CommandText { get => _commandText; set { _commandText = value; OnPropertyChanged(); } }

        // --- Commands ---
        public ICommand LoadFromOuCommand { get; }
        public ICommand LoadHostsCommand { get; }
        public ICommand SaveHostsCommand { get; }
        public ICommand GetInfoCommand { get; }
        public ICommand GetAdInfoCommand { get; }
        public ICommand ExportCommand { get; }
        public ICommand CreateCommand { get; }
        public ICommand EnableCommand { get; }
        public ICommand DisableCommand { get; }
        public ICommand MoveCommand { get; }
        public ICommand SetDescriptionCommand { get; }
        public ICommand DeleteCommand { get; }
        public ICommand RunScriptCommand { get; }
        public ICommand RunCommandCommand { get; }

        public ComputerManagementViewModel(ActiveDirectoryService adService, ConfigurationService configService, CredentialService credentialService, SystemInfoService systemInfoService)
        {
            _adService = adService; _configService = configService; _credentialService = credentialService; _systemInfoService = systemInfoService;
            _logger = new LogService();

            LoadFromOuCommand = new RelayCommand(_ => ExecuteLoadFromOu());
            LoadHostsCommand = new RelayCommand(_ => ExecuteLoadHosts());
            SaveHostsCommand = new RelayCommand(_ => ExecuteSaveHosts());
            GetInfoCommand = new RelayCommand(async _ => await ExecuteGetInfo());
            GetAdInfoCommand = new RelayCommand(async _ => await ExecuteGetAdInfo());
            ExportCommand = new RelayCommand(_ => ExecuteExport());
            CreateCommand = new RelayCommand(_ => ExecuteCreate());
            EnableCommand = new RelayCommand(_ => ProcessAdAction("Enable", host => _adService.SetComputerStatus(host, true)));
            DisableCommand = new RelayCommand(_ => ProcessAdAction("Disable", host => _adService.SetComputerStatus(host, false)));
            MoveCommand = new RelayCommand(_ => ProcessAdAction("Move", host => _adService.MoveComputerObject(host, SelectedTargetOuValue, $"Moved to Restricted OU on {DateTime.Now.ToShortDateString()}")));
            SetDescriptionCommand = new RelayCommand(_ => ProcessAdAction("Set Description", host => _adService.SetComputerDescription(host, AdDescription)));
            DeleteCommand = new RelayCommand(_ => ExecuteDelete());
            RunScriptCommand = new RelayCommand(async _ => await ExecuteRunScript());
            RunCommandCommand = new RelayCommand(async _ => await ExecuteRunCommand());
        }

        private void LogMessage(string msg) => Application.Current.Dispatcher.Invoke(() => Logs.Add($"[{DateTime.Now:HH:mm:ss}] {msg}"));
        
        private string[] GetAdHostnames() 
        {
            var (resolved, unresolved) = HostResolverHelper.ResolveTargetHosts(TargetHostnamesText);
            foreach (var unres in unresolved)
            {
                LogMessage($"Warning: Could not resolve hostname '{unres}'. Proceeding anyway...");
            }
            return resolved.Concat(unresolved).ToArray();
        }

        private void ProcessAdAction(string actionName, Func<string, bool> adOperation)
        {
            var hosts = GetAdHostnames();
            if (hosts.Length == 0) { MessageBox.Show("Please enter target hostnames first.", "Missing Input", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            foreach (var host in hosts)
            {
                LogMessage($"Attempting to {actionName} {host}...");
                bool success = adOperation(host);
                LogMessage(success ? $"SUCCESS: {host}" : $"FAILED: Could not {actionName} {host}.");
            }
        }

        private async Task ExecuteGetInfo()
        {
            var hosts = GetAdHostnames();
            if (hosts.Length == 0) return;
            UpdateStatus?.Invoke("Querying computers via WMI... Please wait.");
            var results = new ObservableCollection<ComputerInfoResult>();
            foreach (var pc in hosts) results.Add(await _systemInfoService.GetComputerDataAsync(pc));
            CurrentResults = results;
            UpdateStatus?.Invoke($"WMI Query complete. Processed {hosts.Length} computers.");
        }

        private async Task ExecuteGetAdInfo()
        {
            var hosts = GetAdHostnames();
            if (hosts.Length == 0) return;
            UpdateStatus?.Invoke("Querying Active Directory Computers... Please wait.");
            var results = new ObservableCollection<AdComputerInfoResult>();
            foreach (var pc in hosts) results.Add(await Task.Run(() => _adService.GetAdComputerInfo(pc)));
            CurrentResults = results;
            UpdateStatus?.Invoke($"AD Query complete. Processed {hosts.Length} computers.");
        }

        private void ExecuteCreate()
        {
            if (string.IsNullOrWhiteSpace(SelectedComputerTypeKey)) return;
            string[] hostsToCreate;

            if (IsPatternCreate)
            {
                hostsToCreate = HostnameHelper.GenerateSequential(StartHostname, CreateCount);
                if (hostsToCreate.Length == 0) { MessageBox.Show("Could not find a numeric sequence in the starting hostname.", "Format Error", MessageBoxButton.OK, MessageBoxImage.Error); return; }
                try
                {
                    string fullPath = _logger.SaveBulkLog(LogCategory.ComputerObjectGenerated, hostsToCreate, "ComputerObjectGenerated");
                    MessageBox.Show($"Generated {hostsToCreate.Length} hostnames.\n\nSaved to:\n{fullPath}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                } catch { }
            }
            else
            {
                hostsToCreate = GetAdHostnames();
            }

            foreach (var host in hostsToCreate)
            {
                LogMessage($"Attempting to Create {host}...");
                LogMessage(_adService.CreateComputerObject(host, SelectedComputerTypeKey) ? $"SUCCESS: Created {host}" : $"FAILED: Could not create {host}.");
            }
        }

        private void ExecuteDelete()
        {
            var hosts = GetAdHostnames();
            if (hosts.Length == 0) return;
            
            string expected = hosts.Length <= 20 ? hosts.Length.ToString() : (hosts.Length <= 100 ? $"Delete {hosts.Length}" : $"CONFIRM-{new Random().Next(1000, 9999)}");
            string input = DialogHelper.ShowInputDialog($"You are about to delete {hosts.Length} objects.\n\nType '{expected}' to confirm.", "Confirm Deletion");
            
            if (string.Equals(input, expected, StringComparison.OrdinalIgnoreCase)) ProcessAdAction("Delete", host => _adService.DeleteComputerObject(host));
            else LogMessage("Deletion cancelled by user.");
        }

        private async Task ExecuteRunScript()
        {
            var hosts = GetAdHostnames();
            if (hosts.Length == 0) return;
            if (new OpenFileDialog { Filter = "Scripts (*.bat;*.ps1)|*.bat;*.ps1" }.ShowDialog() == true)
            {
                var svc = new RemoteExecutionService(_logger, _configService.CurrentSettings?.ExternalTools?.PsExecPath ?? "psexec.exe", _credentialService.Username, _credentialService.Password);
                foreach (string pc in hosts)
                {
                    UpdateStatus?.Invoke($"Running script on {pc}...");
                    string res = await svc.RunRemoteScriptWinRMAsync(pc, "", _credentialService.Username, _credentialService.Password);
                    LogMessage($"Script Execution on {pc}: {(res.StartsWith("ERROR") ? "FAILED" : "SUCCESS")}");
                }
                MessageBox.Show("Script execution completed.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private async Task ExecuteRunCommand()
        {
            var hosts = GetAdHostnames();
            if (hosts.Length == 0 || string.IsNullOrWhiteSpace(CommandText)) return;
            var svc = new RemoteExecutionService(_logger, _configService.CurrentSettings?.ExternalTools?.PsExecPath ?? "psexec.exe", _credentialService.Username, _credentialService.Password);
            foreach (string pc in hosts)
            {
                UpdateStatus?.Invoke($"Running command on {pc}...");
                string res = await svc.RunRemoteCommandWinRMAsync(pc, CommandText, _credentialService.Username, _credentialService.Password);
                LogMessage($"Command on {pc}: {(res.StartsWith("ERROR") ? "FAILED" : "SUCCESS")}");
            }
        }

        private void ExecuteExport()
        {
            try
            {
                string path = _logger.GetNewFilePath(LogCategory.ActiveDirectoryObjectQuery, "ComputerQuery", ".csv");
                if (CurrentResults is IEnumerable<ComputerInfoResult> w) CsvExportService.Export(w, path);
                else if (CurrentResults is IEnumerable<AdComputerInfoResult> a) CsvExportService.Export(a, path);
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = path, UseShellExecute = true });
            } catch (Exception ex) { MessageBox.Show($"Export failed: {ex.Message}"); }
        }
        
        private void ExecuteLoadHosts() 
        { 
            var dlg = new OpenFileDialog { Filter = "Text Files|*.txt" };
            if (dlg.ShowDialog() == true) 
            {
                TargetHostnamesText = File.ReadAllText(dlg.FileName); 
            }
        }
        
        private void ExecuteSaveHosts() 
        { 
            try
            {
                _logger.SaveTextLog(LogCategory.ComputerObjectList, TargetHostnamesText, "Hosts");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save hostnames: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void ExecuteLoadFromOu() 
        { 
            if (string.IsNullOrWhiteSpace(SelectedLoadOuValue))
            {
                MessageBox.Show("Please select or enter an OU to load computers from.", "Missing Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            TargetHostnamesText = string.Join("\r\n", _adService.GetComputersFromOu(_configService.CurrentSettings.ActiveDirectory.DomainName, SelectedLoadOuValue, _credentialService.Username, _credentialService.Password)); 
        }
    }
}
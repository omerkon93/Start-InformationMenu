using System;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using AdminInfoTools.Models;
using AdminInfoTools.Services;
using AdminInfoTools.Helpers;

namespace AdminInfoTools.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ConfigurationService _configService;
        private readonly CredentialService _credentialService;

        public Action OnSettingsLoaded { get; set; }
        public Action OnCredentialsUpdated { get; set; }
        public Action<string> UpdateStatus { get; set; }

        // --- Credentials Properties ---
        private string _adUsername;
        public string AdUsername { get => _adUsername; set { _adUsername = value; OnPropertyChanged(); } }

        private string _adPassword;
        public string AdPassword { get => _adPassword; set { _adPassword = value; OnPropertyChanged(); } }

        private bool _saveCredentialsChecked;
        public bool SaveCredentialsChecked { get => _saveCredentialsChecked; set { _saveCredentialsChecked = value; OnPropertyChanged(); } }

        private string _credentialStatusMessage = "Status: Credentials Not Set";
        public string CredentialStatusMessage { get => _credentialStatusMessage; set { _credentialStatusMessage = value; OnPropertyChanged(); } }

        private Brush _credentialStatusColor = Brushes.White;
        public Brush CredentialStatusColor { get => _credentialStatusColor; set { _credentialStatusColor = value; OnPropertyChanged(); } }

        // --- Settings File Properties ---
        private string _settingsPath = GetDefaultSettingsPath();
        public string SettingsPath { get => _settingsPath; set { _settingsPath = value; OnPropertyChanged(); } }

        private static string GetDefaultSettingsPath()
        {
            string basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Settings", "settings.jsonc");
            
            // Fallback for Visual Studio debugging (navigates up from bin\Debug\net8.0-windows\)
            if (!File.Exists(basePath))
            {
                string devPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\Settings\settings.jsonc"));
                if (File.Exists(devPath)) return devPath;
            }

            return basePath;
        }

        // --- Config Edit Properties ---
        private string _editDomainName;
        public string EditDomainName { get => _editDomainName; set { _editDomainName = value; OnPropertyChanged(); } }

        private string _editTargetServer;
        public string EditTargetServer { get => _editTargetServer; set { _editTargetServer = value; OnPropertyChanged(); } }

        private string _editCompPattern;
        public string EditCompPattern { get => _editCompPattern; set { _editCompPattern = value; OnPropertyChanged(); } }

        private string _editLogsDir;
        public string EditLogsDir { get => _editLogsDir; set { _editLogsDir = value; OnPropertyChanged(); } }

        private string _editRemoteTemp;
        public string EditRemoteTemp { get => _editRemoteTemp; set { _editRemoteTemp = value; OnPropertyChanged(); } }

        private string _editDefCompList;
        public string EditDefCompList { get => _editDefCompList; set { _editDefCompList = value; OnPropertyChanged(); } }

        private string _editDefUserList;
        public string EditDefUserList { get => _editDefUserList; set { _editDefUserList = value; OnPropertyChanged(); } }

        private string _editPsExec;
        public string EditPsExec { get => _editPsExec; set { _editPsExec = value; OnPropertyChanged(); } }

        private string _editDameware;
        public string EditDameware { get => _editDameware; set { _editDameware = value; OnPropertyChanged(); } }

        // --- Commands ---
        public ICommand SaveCredentialsCommand { get; }
        public ICommand BrowseSettingsCommand { get; }
        public ICommand LoadSettingsCommand { get; }
        public ICommand SaveConfigCommand { get; }

        public SettingsViewModel(ConfigurationService configService, CredentialService credentialService)
        {
            _configService = configService;
            _credentialService = credentialService;

            SaveCredentialsCommand = new RelayCommand(ExecuteSaveCredentials);
            BrowseSettingsCommand = new RelayCommand(_ => ExecuteBrowseSettings());
            LoadSettingsCommand = new RelayCommand(_ => ExecuteLoadSettings());
            SaveConfigCommand = new RelayCommand(_ => ExecuteSaveConfig());

            LoadSavedCredentials();
        }

        private void LoadSavedCredentials()
        {
            var creds = _credentialService.LoadSavedCredentials();
            if (creds.HasValue)
            {
                AdUsername = creds.Value.Username;
                AdPassword = creds.Value.Password;
                SaveCredentialsChecked = true;

                CredentialStatusMessage = "Status: Credentials loaded from disk.";
                CredentialStatusColor = Brushes.LimeGreen;
            }
        }

        private void ExecuteSaveCredentials(object parameter)
        {
            // Allow passing PasswordBox from view as parameter to avoid strict MVVM PasswordBox binding headaches
            string passwordToSave = AdPassword;
            if (parameter is System.Windows.Controls.PasswordBox pb)
            {
                passwordToSave = pb.Password;
            }

            if (string.IsNullOrWhiteSpace(AdUsername) || string.IsNullOrWhiteSpace(passwordToSave))
            {
                MessageBox.Show("Please enter both a username and a password.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _credentialService.SaveOrClearCredentials(AdUsername, passwordToSave, SaveCredentialsChecked);

                CredentialStatusMessage = SaveCredentialsChecked 
                    ? "Status: Credentials stored in memory and saved to disk." 
                    : "Status: Credentials stored securely in memory for this session.";
                CredentialStatusColor = Brushes.LimeGreen;

                // Sync password property just in case it was passed via UI element
                AdPassword = passwordToSave; 

                OnCredentialsUpdated?.Invoke();
                
                MessageBox.Show("Credentials successfully set.", "Credentials Updated", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (IOException ex)
            {
                MessageBox.Show($"{ex.Message}: {ex.InnerException?.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void ExecuteBrowseSettings()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog { Filter = "JSON Configuration (*.jsonc;*.json)|*.jsonc;*.json" };
            if (openFileDialog.ShowDialog() == true)
            {
                SettingsPath = openFileDialog.FileName;
            }
        }

        private void ExecuteLoadSettings()
        {
            if (File.Exists(SettingsPath))
            {
                if (_configService.LoadConfiguration(SettingsPath))
                {
                    UpdateStatus?.Invoke($"Loaded config: {Path.GetFileName(SettingsPath)}");
                    MessageBox.Show("Configuration loaded successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    PopulateEditUi();
                    OnSettingsLoaded?.Invoke();
                }
                else
                {
                    MessageBox.Show("Failed to parse the configuration file. Please check its format.", "Parse Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show($"Settings file not found at:\n{SettingsPath}\n\nPlease verify the path and try again.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void PopulateEditUi()
        {
            var currentSettings = _configService.CurrentSettings;
            if (currentSettings != null)
            {
                EditDomainName = currentSettings.ActiveDirectory?.DomainName ?? "";
                EditTargetServer = currentSettings.ActiveDirectory?.TargetServer ?? "";
                EditCompPattern = currentSettings.ActiveDirectory?.ComputerNamePattern ?? "";

                EditLogsDir = currentSettings.AppConfig?.LogsDirectory ?? "";
                EditRemoteTemp = currentSettings.AppConfig?.RemoteTempPath ?? "";
                EditDefCompList = currentSettings.AppConfig?.DefaultComputerList ?? "";
                EditDefUserList = currentSettings.AppConfig?.DefaultUserList ?? "";

                EditPsExec = currentSettings.ExternalTools?.PsExecPath ?? "";
                EditDameware = currentSettings.ExternalTools?.DamewarePath ?? "";
            }
        }

        private void ExecuteSaveConfig()
        {
            if (_configService.CurrentSettings == null)
            {
                MessageBox.Show("No configuration loaded to save. Please load a file first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(SettingsPath) || !File.Exists(SettingsPath))
            {
                MessageBox.Show("The specified file path is invalid or does not exist.", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                var settings = _configService.CurrentSettings;
                
                if (settings.ActiveDirectory != null)
                {
                    settings.ActiveDirectory.DomainName = EditDomainName;
                    settings.ActiveDirectory.TargetServer = EditTargetServer;
                    settings.ActiveDirectory.ComputerNamePattern = EditCompPattern;
                }

                if (settings.AppConfig != null)
                {
                    settings.AppConfig.LogsDirectory = EditLogsDir;
                    settings.AppConfig.RemoteTempPath = EditRemoteTemp;
                    settings.AppConfig.DefaultComputerList = EditDefCompList;
                    settings.AppConfig.DefaultUserList = EditDefUserList;
                }

                if (settings.ExternalTools != null)
                {
                    settings.ExternalTools.PsExecPath = EditPsExec;
                    settings.ExternalTools.DamewarePath = EditDameware;
                }

                var options = new JsonSerializerOptions { WriteIndented = true };
                string jsonString = JsonSerializer.Serialize(settings, options);

                File.WriteAllText(SettingsPath, jsonString);

                MessageBox.Show("Configuration successfully saved to disk!", "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save configuration:\n\n{ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
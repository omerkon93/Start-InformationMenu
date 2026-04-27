﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using AdminInfoTools.Models;
using AdminInfoTools.Helpers;
using AdminInfoTools.Services;

namespace AdminInfoTools
{
    public partial class MainWindow : Window
    {
        private readonly ConfigurationService _configService;
        private readonly SystemInfoService _systemInfoService;
        private readonly CredentialService _credentialService;
        private ActiveDirectoryService _adService; 
        
        public ObservableCollection<ComputerInfoResult> ComputerResults { get; set; }
        public ObservableCollection<AdComputerInfoResult> AdComputerResults { get; set; }
        

        public MainWindow()
        {
            InitializeComponent();

            // --- 2. INITIALIZE SERVICES ---
            _configService = new ConfigurationService();
            _systemInfoService = new SystemInfoService();
            _credentialService = new CredentialService();
            ComputerResults = new ObservableCollection<ComputerInfoResult>();
            AdComputerResults = new ObservableCollection<AdComputerInfoResult>();


            // --- 3. BIND EVENTS ---

            BtnComputerMenu.Click += BtnComputerMenu_Click;
            BtnUserMenu.Click += (s, e) => SwitchView(ViewUserManagement);
            
            // Options Menu Events
            BtnOptionsMenu.Click += (s, e) => SwitchView(ViewOptions);
            BtnSaveCredentials.Click += BtnSaveCredentials_Click;
            BtnBrowseSettings.Click += BtnBrowseSettings_Click;
            BtnLoadSettingsOptions.Click += BtnLoadSettingsOptions_Click;
            BtnSaveConfigToDisk.Click += BtnSaveConfigToDisk_Click;

            // Computer Management Action Events
            BtnAdCreate.Click += BtnAdCreate_Click;
            BtnAdDelete.Click += BtnAdDelete_Click;
            BtnAdEnable.Click += (s, e) => ProcessAdAction("Enable", host => _adService.SetComputerStatus(host, true));
            BtnAdDisable.Click += (s, e) => ProcessAdAction("Disable", host => _adService.SetComputerStatus(host, false));
            BtnAdMove.Click += BtnAdMove_Click;
            BtnAdSetDescription.Click += BtnAdSetDescription_Click; 
            
            BtnRunScript.Click += BtnRunScript_Click;
            BtnRunCommand.Click += BtnRunCommand_Click;
            
            // Query Buttons
            BtnGetInfo.Click += BtnGetInfo_Click;
            BtnGetAdInfo.Click += BtnGetAdInfo_Click;
            BtnExport.Click += BtnExport_Click;

            // Hostname Load/Save Events
            BtnAdLoadHosts.Click += BtnAdLoadHosts_Click;
            BtnAdSaveHosts.Click += BtnAdSaveHosts_Click;

            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Auto-load saved credentials if they exist
            LoadSavedCredentials();

            // Automatically simulate the settings load now that the UI is fully ready!
            BtnLoadSettingsOptions_Click(this, new RoutedEventArgs());
        }

        // --- VIEW NAVIGATION ---
        private void SwitchView(UIElement activeView)
        {
            ViewComputerManagement.Visibility = Visibility.Collapsed;
            if (ViewOptions != null) ViewOptions.Visibility = Visibility.Collapsed; 
            if (ViewUserManagement != null) ViewUserManagement.Visibility = Visibility.Collapsed;
            
            activeView.Visibility = Visibility.Visible;
        }

        private void BtnComputerMenu_Click(object sender, RoutedEventArgs e)
        {
            if (_configService.CurrentSettings == null)
            {
                MessageBox.Show("Please load a Settings file first.", "Setup Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_credentialService.AreCredentialsSet)
            {
                MessageBox.Show("Please set your AD Credentials in the Options menu first.", "Credentials Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                SwitchView(ViewOptions); // Automatically route them to the options page
                return;
            }

            if (_adService == null) _adService = new ActiveDirectoryService(_configService); // This should have been initialized on config load
            
            // Pass the session credentials to the service
            _adService.DomainUser = _credentialService.Username;
            _adService.DomainPass = _credentialService.Password;
            
            CmbComputerType.ItemsSource = _configService.CurrentSettings.ActiveDirectory.ComputerTypes;
            if (CmbComputerType.Items.Count > 0) CmbComputerType.SelectedIndex = 0;

            CmbTargetOu.ItemsSource = _configService.CurrentSettings.ActiveDirectory.SpecialOUs;
            CmbTargetOu.DisplayMemberPath = "Key";
            CmbTargetOu.SelectedValuePath = "Value";
            if (CmbTargetOu.Items.Count > 0) CmbTargetOu.SelectedIndex = 0;

            CmbLoadOu.ItemsSource = _configService.CurrentSettings.ActiveDirectory.SpecialOUs;
            if (CmbLoadOu.Items.Count > 0) CmbLoadOu.SelectedIndex = 0;

            SwitchView(ViewComputerManagement);
            StatusText.Text = "Computer Management mode.";
        }

        // --- ACTIVE DIRECTORY ACTIONS ---
        private void LogAdMessage(string message)
        {
            ListAdLogs.Items.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            ListAdLogs.ScrollIntoView(ListAdLogs.Items[ListAdLogs.Items.Count - 1]);
        }

        private void LoadSavedCredentials()
        {
            var creds = _credentialService.LoadSavedCredentials();
            if (creds.HasValue)
            {
                TxtAdUsername.Text = creds.Value.Username;
                TxtAdPassword.Password = creds.Value.Password;
                ChkSaveCredentials.IsChecked = true;

                LblCredStatus.Text = "Status: Credentials loaded from disk.";
                LblCredStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LimeGreen);
            }
        }

        private void BtnSaveCredentials_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtAdUsername.Text) || string.IsNullOrWhiteSpace(TxtAdPassword.Password))
            {
                MessageBox.Show("Please enter both a username and a password.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                _credentialService.SaveOrClearCredentials(TxtAdUsername.Text, TxtAdPassword.Password, ChkSaveCredentials.IsChecked == true);

                // If the AD service is already running, update it live
                if (_adService != null)
                {
                    _adService.DomainUser = _credentialService.Username;
                    _adService.DomainPass = _credentialService.Password;
                }

                LblCredStatus.Text = ChkSaveCredentials.IsChecked == true 
                    ? "Status: Credentials stored in memory and saved to disk." 
                    : "Status: Credentials stored securely in memory for this session.";
                LblCredStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LimeGreen);
                
                MessageBox.Show("Credentials successfully set.", "Credentials Updated", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (IOException ex)
            {
                MessageBox.Show($"{ex.Message}: {ex.InnerException?.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private string[] GetAdHostnames()
        {
            return TxtComputerObjectList.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                                      .Select(h => h.Trim().ToUpper())
                                      .Where(h => !string.IsNullOrEmpty(h))
                                      .ToArray();
        }

        private void ProcessAdAction(string actionName, Func<string, bool> adOperation)
        {
            if (_adService == null || !_credentialService.AreCredentialsSet)
            {
                MessageBox.Show("Active Directory service not initialized or credentials not set. Please load settings and set credentials first.", "Setup Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                SwitchView(ViewOptions);
                return;
            }

            var hosts = GetAdHostnames();
            if (hosts.Length == 0) return;

            foreach (var host in hosts)
            {
                LogAdMessage($"Attempting to {actionName} {host}...");
                bool success = adOperation(host);
                if (success) LogAdMessage($"SUCCESS: {host}");
                else LogAdMessage($"FAILED: Could not {actionName} {host}.");
            }
        }

        private void BtnAdCreate_Click(object sender, RoutedEventArgs e)
        {
            if (CmbComputerType.SelectedValue == null) return;
            string selectedType = CmbComputerType.SelectedValue.ToString();

            string[] hostsToCreate;

            // Determine where we are getting our hostnames from
            if (RdoPatternCreate.IsChecked == true)
            {
                if (!int.TryParse(TxtCreateCount.Text, out int count) || count <= 0)
                {
                    MessageBox.Show("Please enter a valid number of objects to create.", "Invalid Count", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                string startHost = TxtStartHostname.Text.Trim();
                if (string.IsNullOrWhiteSpace(startHost))
                {
                    MessageBox.Show("Please enter a starting hostname.", "Missing Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // Use the new HostnameHelper to generate sequential hostnames
                hostsToCreate = HostnameHelper.GenerateSequential(startHost, count);
                if (hostsToCreate.Length == 0)
                {
                    MessageBox.Show("Could not find a numeric sequence in the starting hostname (e.g., '0001') or an error occurred during generation.", "Format Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
            }
            else
            {
                hostsToCreate = GetAdHostnames();
                if (hostsToCreate.Length == 0)
                {
                    MessageBox.Show("Please enter or load target hostnames first.", "Missing Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            // If we generated names sequentially, save them to a file for the user's reference.
            if (RdoPatternCreate.IsChecked == true && hostsToCreate.Any())
            {
                try
                {
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string logDirectory = @"C:\Projects\Start-InformationMenu\cs\Logs\ComputerObjectGenerated\";
                    Directory.CreateDirectory(logDirectory); // Ensures the directory exists before saving
                    string filePath = Path.Combine(logDirectory, $"ComputerObjectGenerated-{timestamp}.txt");
                    string fullPath = FileHelper.SaveLinesToFile(hostsToCreate, filePath);
                    MessageBox.Show($"Successfully generated {hostsToCreate.Length} hostnames.\n\nSaved to:\n{fullPath}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Hostnames were generated, but could not save to text file.\nError: {ex.Message}", "File Write Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }

            // Loop through the final list and create them
            foreach (var host in hostsToCreate)
            {
                LogAdMessage($"Attempting to Create {host}...");
                bool success = _adService.CreateComputerObject(host, selectedType);
                if (success) LogAdMessage($"SUCCESS: Created {host}");
                else LogAdMessage($"FAILED: Could not create {host}.");
            }
        }

        private void BtnAdDelete_Click(object sender, RoutedEventArgs e)
        {
            var hosts = GetAdHostnames();
            if (hosts.Length == 0)
            {
                MessageBox.Show("Please enter or load target hostnames first.", "Missing Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (ConfirmDeletion(hosts.Length))
            {
                ProcessAdAction("Delete", host => _adService.DeleteComputerObject(host));
            }
            else
            {
                LogAdMessage("Deletion cancelled by user.");
            }
        }

        private bool ConfirmDeletion(int objectCount)
        {
            if (objectCount <= 5)
            {
                var result = MessageBox.Show($"Are you ABSOLUTELY sure you want to delete {objectCount} object(s) from AD?", "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
                return result == MessageBoxResult.Yes;
            }
            else if (objectCount <= 20)
            {
                string input = ShowInputDialog($"You are about to delete {objectCount} objects.\n\nType '{objectCount}' to confirm.", "Confirm Deletion");
                return input == objectCount.ToString();
            }
            else if (objectCount <= 100)
            {
                string expected = $"Delete {objectCount}";
                string input = ShowInputDialog($"WARNING: You are deleting {objectCount} objects.\n\nType '{expected}' to confirm.", "Confirm Deletion");
                return string.Equals(input, expected, StringComparison.OrdinalIgnoreCase);
            }
            else
            {
                string pin = new Random().Next(1000, 9999).ToString();
                string expected = $"CONFIRM-{pin}";
                string input = ShowInputDialog($"CRITICAL: You are attempting to delete {objectCount} objects.\n\nType '{expected}' to execute.", "CRITICAL: Mass Deletion");
                return input == expected;
            }
        }

        private string ShowInputDialog(string promptText, string title)
        {
            Window inputWindow = new Window
            {
                Title = title,
                Width = 400,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var textBlock = new TextBlock { Text = promptText, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(textBlock, 0);
            grid.Children.Add(textBlock);

            var textBox = new TextBox { Margin = new Thickness(0, 0, 0, 10) };
            Grid.SetRow(textBox, 1);
            grid.Children.Add(textBox);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetRow(buttonPanel, 2);

            var okButton = new Button { Content = "OK", Width = 75, Margin = new Thickness(0, 0, 10, 0), IsDefault = true };
            var cancelButton = new Button { Content = "Cancel", Width = 75, IsCancel = true };
            
            string result = null;
            okButton.Click += (s, e) => { result = textBox.Text; inputWindow.DialogResult = true; };
            
            buttonPanel.Children.Add(okButton);
            buttonPanel.Children.Add(cancelButton);
            grid.Children.Add(buttonPanel);
            
            inputWindow.Content = grid;
            
            // Auto-focus the textbox when the window appears
            inputWindow.Loaded += (s, e) => textBox.Focus();

            inputWindow.ShowDialog();
            return result;
        }

        private void BtnAdMove_Click(object sender, RoutedEventArgs e)
        {
            if (CmbTargetOu.SelectedValue == null) return;
            string targetOuPath = CmbTargetOu.SelectedValue.ToString();
            string description = $"Moved to Restricted OU on {DateTime.Now.ToShortDateString()}";
            
            ProcessAdAction("Move", host => _adService.MoveComputerObject(host, targetOuPath, description));
        }

        // --- NEW: SET DESCRIPTION ---
        private void BtnAdSetDescription_Click(object sender, RoutedEventArgs e)
        {
            string newDesc = TxtAdDescription.Text;
            if (string.IsNullOrWhiteSpace(newDesc))
            {
                MessageBox.Show("Please enter a new description.", "Empty Field", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ProcessAdAction("Set Description", host => _adService.SetComputerDescription(host, newDesc));
        }

        // --- REMOTE EXECUTION ---
        private async void BtnRunScript_Click(object sender, RoutedEventArgs e)
        {
            var selectedComputers = GetAdHostnames();
            if (selectedComputers.Length == 0)
            {
                MessageBox.Show("Please enter or load at least one computer name into the 'Target Hostnames' box.", "Empty List", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                InitialDirectory = @"C:\AdminScripts",
                Filter = "Scripts (*.bat;*.ps1)|*.bat;*.ps1|All files (*.*)|*.*",
                Title = "Select the script to invoke remotely"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string selectedFile = openFileDialog.FileName;
                
                BtnRunScript.IsEnabled = false;
                BtnRunCommand.IsEnabled = false;

                string psExecPath = _configService.CurrentSettings?.ExternalTools?.PsExecPath ?? "psexec.exe";
                var logger = new LogService();
                var remoteService = new RemoteExecutionService(logger, psExecPath, _credentialService.Username, _credentialService.Password);

                foreach (string pc in selectedComputers)
                {
                    StatusText.Text = $"Running script on {pc}...";
                    string result = await remoteService.RunRemoteScriptWinRMAsync(pc, selectedFile, _credentialService.Username, _credentialService.Password);
                    LogAdMessage($"Script Execution on {pc}: {(result.StartsWith("ERROR") || result.StartsWith("EXCEPTION") ? "FAILED" : "SUCCESS")}");
                }

                StatusText.Text = "Script execution completed.";
                MessageBox.Show("Script execution completed. Check logs for details.", "Execution Complete", MessageBoxButton.OK, MessageBoxImage.Information);
                
                BtnRunScript.IsEnabled = true;
                BtnRunCommand.IsEnabled = true;
            }
        }

        private async void BtnRunCommand_Click(object sender, RoutedEventArgs e)
        {
            var selectedComputers = GetAdHostnames();
            if (selectedComputers.Length == 0)
            {
                MessageBox.Show("Please enter or load at least one computer name into the 'Target Hostnames' box.", "Empty List", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string command = TxtCommand.Text;
            if (string.IsNullOrWhiteSpace(command))
            {
                MessageBox.Show("Please enter a command to execute.", "Empty Command", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            BtnRunScript.IsEnabled = false;
            BtnRunCommand.IsEnabled = false;

            string psExecPath = _configService.CurrentSettings?.ExternalTools?.PsExecPath ?? "psexec.exe";
            var logger = new LogService();
            var remoteService = new RemoteExecutionService(logger, psExecPath, _credentialService.Username, _credentialService.Password);

            foreach (string pc in selectedComputers)
            {
                StatusText.Text = $"Running command on {pc}...";
                string result = await remoteService.RunRemoteCommandWinRMAsync(pc, command, _credentialService.Username, _credentialService.Password);
                LogAdMessage($"Command Execution on {pc}: {(result.StartsWith("ERROR") || result.StartsWith("EXCEPTION") ? "FAILED" : "SUCCESS")}");
            }

            StatusText.Text = "Command Execution Complete.";
            MessageBox.Show("Command execution completed. Check logs for details.", "Execution Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            
            BtnRunScript.IsEnabled = true;
            BtnRunCommand.IsEnabled = true;
        }

        // --- LOAD & SAVE HOSTNAMES ---
        private void BtnAdLoadHosts_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog 
            { 
                Filter = "Text Files (*.txt)|*.txt|All files (*.*)|*.*",
                Title = "Load Hostnames"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                TxtComputerObjectList.Text = File.ReadAllText(openFileDialog.FileName);
                LogAdMessage($"Loaded hostnames from {Path.GetFileName(openFileDialog.FileName)}");
            }
        }

        private void BtnAdSaveHosts_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtComputerObjectList.Text))
            {
                MessageBox.Show("There are no computer object list to save.", "Empty", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            try
            {
                string saveDirectory = @"C:\Projects\Start-InformationMenu\cs\Logs\ComputerObjectList";
                Directory.CreateDirectory(saveDirectory);
                string filePath = Path.Combine(saveDirectory, $"ComputerObjectList_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

                File.WriteAllText(filePath, TxtComputerObjectList.Text);
                LogAdMessage($"Saved computer object list to {Path.GetFileName(filePath)}");
                MessageBox.Show($"Successfully saved computer object list to:\n{filePath}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save computer object list:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnLoadFromOu_Click(object sender, RoutedEventArgs e)
        {
            if (CmbLoadOu.SelectedValue == null)
            {
                MessageBox.Show("Please select an OU from the dropdown.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string selectedOu = CmbLoadOu.SelectedValue.ToString();
            string domainName = _configService.CurrentSettings?.ActiveDirectory?.DomainName ?? "testlab.local";

            BtnLoadFromOu.IsEnabled = false;
            StatusText.Text = "Querying AD for computers...";

            try
            {
                if (_adService == null)
                {
                    MessageBox.Show("Active Directory service not initialized. Please load settings first.", "Setup Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                List<string> computers = _adService.GetComputersFromOu(domainName, selectedOu, _credentialService.Username, _credentialService.Password);

                if (computers.Count > 0)
                {
                    // Join the list with newlines and populate the TextBox
                    TxtComputerObjectList.Text = string.Join(Environment.NewLine, computers);
                    StatusText.Text = $"Loaded {computers.Count} computers from OU.";
                }
                else
                {
                    MessageBox.Show("No computers found in the selected OU.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    StatusText.Text = "No computers found.";
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error querying OU: {ex.Message}", "AD Error", MessageBoxButton.OK, MessageBoxImage.Error);
                StatusText.Text = "Error querying AD.";
            }
            finally
            {
                BtnLoadFromOu.IsEnabled = true;
            }
        }

        // --- SYSTEM INFO CODE ---
        // --- SETTINGS & OPTIONS CODE ---
        private void BtnBrowseSettings_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog { Filter = "JSON Configuration (*.jsonc;*.json)|*.jsonc;*.json" };
            if (openFileDialog.ShowDialog() == true)
            {
                TxtSettingsPath.Text = openFileDialog.FileName;
            }
        }

        private void BtnLoadSettingsOptions_Click(object sender, RoutedEventArgs e)
        {
            string path = TxtSettingsPath.Text;

            if (File.Exists(path))
            {
                if (_configService.LoadConfiguration(path))
                {
                    StatusText.Text = $"Loaded config: {Path.GetFileName(path)}";
                    MessageBox.Show("Configuration loaded successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                    
                    // Initialize _adService here after config is loaded.
                    // This ensures _adService is available for all AD operations
                    // as soon as the configuration is ready.
                    if (_adService == null)
                    {
                        try
                        {
                            _adService = new ActiveDirectoryService(_configService);
                            // If credentials were auto-loaded, apply them now
                            if (_credentialService.AreCredentialsSet)
                            {
                                _adService.DomainUser = _credentialService.Username;
                                _adService.DomainPass = _credentialService.Password;
                            }

                            this.DataContext = new ViewModels.UserManagementViewModel(_adService);
                        }
                        catch (InvalidOperationException ex)
                        {
                            MessageBox.Show($"Failed to initialize Active Directory Service: {ex.Message}. Please check your configuration.", "AD Service Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            _adService = null; // Ensure it's null if initialization failed
                        }
                    }

                    // Populate Computer Management Dropdowns
                    if (_configService.CurrentSettings.ActiveDirectory != null)
                    {
                        CmbComputerType.ItemsSource = _configService.CurrentSettings.ActiveDirectory.ComputerTypes;
                        if (CmbComputerType.Items.Count > 0) CmbComputerType.SelectedIndex = 0;

                        CmbTargetOu.ItemsSource = _configService.CurrentSettings.ActiveDirectory.SpecialOUs;
                        CmbTargetOu.DisplayMemberPath = "Key";
                        CmbTargetOu.SelectedValuePath = "Value";
                        if (CmbTargetOu.Items.Count > 0) CmbTargetOu.SelectedIndex = 0;

                        CmbLoadOu.ItemsSource = _configService.CurrentSettings.ActiveDirectory.SpecialOUs;
                        if (CmbLoadOu.Items.Count > 0) CmbLoadOu.SelectedIndex = 0;
                    }

                    // --- NEW: Populate the Edit UI ---
                    var currentSettings = _configService.CurrentSettings;
                    if (currentSettings != null)
                    {
                        // AD Settings
                        TxtEditDomainName.Text = currentSettings.ActiveDirectory?.DomainName ?? "";
                        TxtEditTargetServer.Text = currentSettings.ActiveDirectory?.TargetServer ?? "";
                        TxtEditCompPattern.Text = currentSettings.ActiveDirectory?.ComputerNamePattern ?? "";

                        // App Settings
                        TxtEditLogsDir.Text = currentSettings.AppConfig?.LogsDirectory ?? "";
                        TxtEditRemoteTemp.Text = currentSettings.AppConfig?.RemoteTempPath ?? "";
                        TxtEditDefCompList.Text = currentSettings.AppConfig?.DefaultComputerList ?? "";
                        TxtEditDefUserList.Text = currentSettings.AppConfig?.DefaultUserList ?? "";

                        // External Tools
                        TxtEditPsExec.Text = currentSettings.ExternalTools?.PsExecPath ?? "";
                        TxtEditDameware.Text = currentSettings.ExternalTools?.DamewarePath ?? "";
                    }
                }
                else
                {
                    MessageBox.Show("Failed to parse the configuration file. Please check its format.", "Parse Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show($"Settings file not found at:\n{path}\n\nPlease verify the path and try again.", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnSaveConfigToDisk_Click(object sender, RoutedEventArgs e)
        {
            if (_configService.CurrentSettings == null)
            {
                MessageBox.Show("No configuration loaded to save. Please load a file first.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string path = TxtSettingsPath.Text;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show("The specified file path is invalid or does not exist.", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                // 1. Update the in-memory settings object with the UI text
                var settings = _configService.CurrentSettings;
                
                // AD Settings
                if (settings.ActiveDirectory != null)
                {
                    settings.ActiveDirectory.DomainName = TxtEditDomainName.Text;
                    settings.ActiveDirectory.TargetServer = TxtEditTargetServer.Text;
                    settings.ActiveDirectory.ComputerNamePattern = TxtEditCompPattern.Text;
                }

                // App Settings
                if (settings.AppConfig != null)
                {
                    settings.AppConfig.LogsDirectory = TxtEditLogsDir.Text;
                    settings.AppConfig.RemoteTempPath = TxtEditRemoteTemp.Text;
                    settings.AppConfig.DefaultComputerList = TxtEditDefCompList.Text;
                    settings.AppConfig.DefaultUserList = TxtEditDefUserList.Text;
                }

                // External Tools
                if (settings.ExternalTools != null)
                {
                    settings.ExternalTools.PsExecPath = TxtEditPsExec.Text;
                    settings.ExternalTools.DamewarePath = TxtEditDameware.Text;
                }

                // 2. Configure the JSON Serializer to make it readable (Pretty Print)
                var options = new System.Text.Json.JsonSerializerOptions 
                { 
                    WriteIndented = true 
                };

                // 3. Serialize the object back into a JSON string
                string jsonString = System.Text.Json.JsonSerializer.Serialize(settings, options);

                // 4. Save the string back to the file
                File.WriteAllText(path, jsonString);

                MessageBox.Show("Configuration successfully saved to disk!", "Save Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save configuration:\n\n{ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async void BtnGetInfo_Click(object sender, RoutedEventArgs e)
        {
            var computersToQuery = GetAdHostnames();
            if (computersToQuery.Length == 0)
            {
                MessageBox.Show("Please enter or load at least one computer name into the 'Target Hostnames' box.", "Empty List", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            BtnGetInfo.IsEnabled = false; 
            StatusText.Text = "Querying computers via WMI... Please wait.";

            // 1. FORCE CLEAR: Wipe the grid's memory of any previous AD columns
            MainDataGrid.ItemsSource = null;
            MainDataGrid.Columns.Clear(); 
            ComputerResults.Clear();

            // 2. Fetch the data
            foreach (var pc in computersToQuery)
            {
                var result = await _systemInfoService.GetComputerDataAsync(pc);
                ComputerResults.Add(result);
            }

            // 3. REBIND: Attach the newly filled list to the grid
            MainDataGrid.ItemsSource = ComputerResults;
            
            StatusText.Text = $"WMI Query complete. Processed {computersToQuery.Length} computers.";
            BtnGetInfo.IsEnabled = true;
        }

        private async void BtnGetAdInfo_Click(object sender, RoutedEventArgs e)
        {
            var computersToQuery = GetAdHostnames();
            if (computersToQuery.Length == 0)
            {
                MessageBox.Show("Please enter or load at least one computer name into the 'Target Hostnames' box.", "Empty List", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            
            if (_adService == null || !_credentialService.AreCredentialsSet)
            {
                MessageBox.Show("Active Directory service not initialized or credentials not set. Please load settings and set credentials first.", "Setup Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                SwitchView(ViewOptions);
                return;
            }
            // Credentials should already be set on _adService if it's not null, from BtnLoadSettingsOptions_Click or BtnComputerMenu_Click

            BtnGetAdInfo.IsEnabled = false; 
            StatusText.Text = "Querying Active Directory Computers... Please wait.";

            // 1. FORCE CLEAR: Wipe the grid's memory of any previous WMI columns
            MainDataGrid.ItemsSource = null;
            MainDataGrid.Columns.Clear();
            AdComputerResults.Clear();

            // 2. Fetch the data
            foreach (var pc in computersToQuery)
            {
                var result = await System.Threading.Tasks.Task.Run(() => _adService.GetAdComputerInfo(pc));
                AdComputerResults.Add(result);
            }

            // 3. REBIND: Attach the newly filled list to the grid
            MainDataGrid.ItemsSource = AdComputerResults;

            StatusText.Text = $"AD Query complete. Processed {computersToQuery.Length} computers.";
            BtnGetAdInfo.IsEnabled = true;
        }

        // --- EXPORT TO CSV ---
        private void BtnExport_Click(object sender, RoutedEventArgs e)
        {
            if (MainDataGrid.Items.Count == 0 || MainDataGrid.ItemsSource == null)
            {
                MessageBox.Show("There is no data to export. Please run a query first.", "Empty", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string exportDirectory = @"C:\Projects\Start-InformationMenu\cs\Logs\ComputerObjectQuery\";
            
                try
                {
                Directory.CreateDirectory(exportDirectory);
                string filePath = Path.Combine(exportDirectory, $"ComputerObjectQuery_{DateTime.Now:yyyyMMdd_HHmmss}.csv");

                    if (MainDataGrid.ItemsSource is IEnumerable<ComputerInfoResult> wmiResults && wmiResults.Any())
                    {
                    CsvExportService.Export(wmiResults, filePath);
                    }
                    else if (MainDataGrid.ItemsSource is IEnumerable<AdComputerInfoResult> adResults && adResults.Any())
                    {
                    CsvExportService.Export(adResults, filePath);
                    }
                    else
                    {
                        MessageBox.Show("The data in the grid is not in a recognized format for export.", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                    FileName = filePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
        }

        // --- ACCORDION LOGIC ---
        private void Accordion_Expanded(object sender, RoutedEventArgs e)
        {
            // Ensure we are interacting with an Expander
            if (sender is System.Windows.Controls.Expander expandedExpander)
            {
                var allExpanders = new[] { ExpanderQuery, ExpanderCreate, ExpanderStatus, ExpanderManage, ExpanderDelete, ExpanderRemote };
                foreach (var expander in allExpanders)
                {
                    if (expander != null && expander != expandedExpander) expander.IsExpanded = false;
                }
            }
        }

        private void UserAccordion_Expanded(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Expander expandedExpander)
            {
                var allExpanders = new[] { ExpUserQuery, ExpUserStatus, ExpUserOrg, ExpUserSecurity };
                foreach (var expander in allExpanders)
                {
                    if (expander != null && expander != expandedExpander) expander.IsExpanded = false;
                }
            }
        }

        // --- CREATION MODE TOGGLE ---
        private void RdoCreateMode_Changed(object sender, RoutedEventArgs e)
        {
            if (PanelSequentialInput == null) return; // Prevent crashes during window load
            PanelSequentialInput.Visibility = RdoPatternCreate.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
    }
}
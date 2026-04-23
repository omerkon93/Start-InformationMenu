﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
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
        private ActiveDirectoryService _adService; 
        
        public ObservableCollection<ComputerInfoResult> ComputerResults { get; set; }
        public ObservableCollection<AdComputerInfoResult> AdComputerResults { get; set; }
        public ObservableCollection<AdUserInfoResult> UserResults { get; set; }
        private List<string> _loadedUserNames;
        private List<string> _loadedComputerNames;
        private string _sessionUsername;
        private string _sessionPassword;
        private readonly string _credFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "credentials.dat");
        

        public MainWindow()
        {
            InitializeComponent();

            // --- 2. INITIALIZE SERVICES ---
            _configService = new ConfigurationService();
            _systemInfoService = new SystemInfoService();
            ComputerResults = new ObservableCollection<ComputerInfoResult>();
            AdComputerResults = new ObservableCollection<AdComputerInfoResult>();
            _loadedComputerNames = new List<string>();
            UserResults = new ObservableCollection<AdUserInfoResult>();
            _loadedUserNames = new List<string>();

            MainDataGrid.ItemsSource = ComputerResults;
            UserDataGrid.ItemsSource = UserResults;

            // --- 3. BIND EVENTS ---
            // Sidebar Menu Events
            BtnComputerMenu.Click += BtnComputerMenu_Click;
            BtnUserMenu.Click += (s, e) => SwitchView(ViewUserManagement);
            
            // Options Menu Events
            BtnOptionsMenu.Click += (s, e) => SwitchView(ViewOptions);
            BtnSaveCredentials.Click += BtnSaveCredentials_Click;
            BtnBrowseSettings.Click += BtnBrowseSettings_Click;
            BtnLoadSettingsOptions.Click += BtnLoadSettingsOptions_Click;
            BtnSaveConfigToDisk.Click += BtnSaveConfigToDisk_Click;

            // User Info Events
            BtnUserLoadHosts.Click += BtnUserLoadHosts_Click;
            BtnUserSaveHosts.Click += BtnUserSaveHosts_Click;
            BtnGetUserAdInfo.Click += BtnGetUserAdInfo_Click;
            BtnExportUsers.Click += BtnExportUsers_Click;
            BtnUserUnlock.Click += (s, e) => ProcessUserAction("Unlock", u => _adService.UnlockUserAccount(u));
            BtnUserEnable.Click += (s, e) => ProcessUserAction("Enable", u => _adService.SetUserStatus(u, true));
            BtnUserDisable.Click += (s, e) => ProcessUserAction("Disable", u => _adService.SetUserStatus(u, false));
            BtnUserForcePassReset.Click += (s, e) => ProcessUserAction("Pass Reset", u => _adService.ForcePasswordReset(u));
            BtnUserSetOrg.Click += BtnUserSetOrg_Click;

            // Computer Management Action Events
            BtnAdCreate.Click += BtnAdCreate_Click;
            BtnAdDelete.Click += BtnAdDelete_Click;
            BtnAdEnable.Click += (s, e) => ProcessAdAction("Enable", host => _adService.SetComputerStatus(host, true));
            BtnAdDisable.Click += (s, e) => ProcessAdAction("Disable", host => _adService.SetComputerStatus(host, false));
            BtnAdMove.Click += BtnAdMove_Click;
            BtnAdSetDescription.Click += BtnAdSetDescription_Click; 
            
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

            if (string.IsNullOrWhiteSpace(_sessionUsername) || string.IsNullOrWhiteSpace(_sessionPassword))
            {
                MessageBox.Show("Please set your AD Credentials in the Options menu first.", "Credentials Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                SwitchView(ViewOptions); // Automatically route them to the options page
                return;
            }

            if (_adService == null) _adService = new ActiveDirectoryService(_configService);
            
            // Pass the session credentials to the service
            _adService.DomainUser = _sessionUsername;
            _adService.DomainPass = _sessionPassword;
            
            CmbComputerType.ItemsSource = _configService.CurrentSettings.ActiveDirectory.ComputerTypes;
            if (CmbComputerType.Items.Count > 0) CmbComputerType.SelectedIndex = 0;

            CmbTargetOu.ItemsSource = _configService.CurrentSettings.ActiveDirectory.SpecialOUs;
            CmbTargetOu.DisplayMemberPath = "Key";
            CmbTargetOu.SelectedValuePath = "Value";
            if (CmbTargetOu.Items.Count > 0) CmbTargetOu.SelectedIndex = 0;

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
            try
            {
                if (File.Exists(_credFilePath))
                {
                    var parts = File.ReadAllText(_credFilePath).Split('|');
                    if (parts.Length == 2)
                    {
                        string user = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[0]));
                        string pass = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(parts[1]));
                        
                        TxtAdUsername.Text = user;
                        TxtAdPassword.Password = pass;
                        ChkSaveCredentials.IsChecked = true;

                        // Auto-set session credentials
                        _sessionUsername = user;
                        _sessionPassword = pass;
                        LblCredStatus.Text = "Status: Credentials loaded from disk.";
                        LblCredStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LimeGreen);
                    }
                }
            }
            catch { /* Ignore errors if file is tampered with or corrupted */ }
        }

        private void BtnSaveCredentials_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtAdUsername.Text) || string.IsNullOrWhiteSpace(TxtAdPassword.Password))
            {
                MessageBox.Show("Please enter both a username and a password.", "Invalid Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Store in memory
            _sessionUsername = TxtAdUsername.Text;
            _sessionPassword = TxtAdPassword.Password;

            // Save or remove credentials file based on checkbox
            try
            {
                if (ChkSaveCredentials.IsChecked == true)
                {
                    string encUser = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(_sessionUsername));
                    string encPass = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(_sessionPassword));
                    File.WriteAllText(_credFilePath, $"{encUser}|{encPass}");
                }
                else
                {
                    if (File.Exists(_credFilePath)) File.Delete(_credFilePath);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to save credentials to disk: {ex.Message}", "Save Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }

            // If the service is already running, update it live
            if (_adService != null)
            {
                _adService.DomainUser = _sessionUsername;
                _adService.DomainPass = _sessionPassword;
            }

            LblCredStatus.Text = ChkSaveCredentials.IsChecked == true 
                ? "Status: Credentials stored in memory and saved to disk." 
                : "Status: Credentials stored securely in memory for this session.";
            LblCredStatus.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.LimeGreen);
            
            MessageBox.Show("Credentials successfully set.", "Credentials Updated", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private string[] GetAdHostnames()
        {
            return TxtAdHostnames.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                                      .Select(h => h.Trim().ToUpper())
                                      .Where(h => !string.IsNullOrEmpty(h))
                                      .ToArray();
        }

        private void ProcessAdAction(string actionName, Func<string, bool> adOperation)
        {
            if (_adService == null || string.IsNullOrWhiteSpace(_adService.DomainUser) || string.IsNullOrWhiteSpace(_adService.DomainPass))
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
            if (RdoCreateSequential.IsChecked == true)
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

            // --- NEW: WRITE TO TEXT FILE (Moved from GenerateSequentialHostnames) ---
            try
            {
                // Choose your file name and path. 
                // Using just the file name saves it directly to the folder where your .exe is running.
                string fileName = "GeneratedHostnames.txt";
                
                // WriteAllLines automatically puts each item in the list on a new line
                System.IO.File.WriteAllLines(fileName, hostsToCreate);

                // Get the full path so the user knows exactly where to look for it
                string fullPath = System.IO.Path.GetFullPath(fileName);
                MessageBox.Show($"Successfully generated {hostsToCreate.Length} hostnames.\n\nSaved to:\n{fullPath}", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (System.Exception ex)
            {
                MessageBox.Show($"Hostnames were generated, but could not save to text file.\nError: {ex.Message}", "File Write Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            var result = MessageBox.Show("Are you ABSOLUTELY sure you want to delete these objects from AD?", "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                ProcessAdAction("Delete", host => _adService.DeleteComputerObject(host));
            }
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
                TxtAdHostnames.Text = File.ReadAllText(openFileDialog.FileName);
                LogAdMessage($"Loaded hostnames from {Path.GetFileName(openFileDialog.FileName)}");
            }
        }

        private void BtnAdSaveHosts_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtAdHostnames.Text))
            {
                MessageBox.Show("There are no hostnames to save.", "Empty", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog 
            { 
                Filter = "Text Files (*.txt)|*.txt",
                FileName = "TargetHostnames.txt",
                Title = "Save Hostnames"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                File.WriteAllText(saveFileDialog.FileName, TxtAdHostnames.Text);
                LogAdMessage($"Saved hostnames to {Path.GetFileName(saveFileDialog.FileName)}");
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
                            if (!string.IsNullOrWhiteSpace(_sessionUsername) && !string.IsNullOrWhiteSpace(_sessionPassword))
                            {
                                _adService.DomainUser = _sessionUsername;
                                _adService.DomainPass = _sessionPassword;
                            }
                        }
                        catch (InvalidOperationException ex)
                        {
                            MessageBox.Show($"Failed to initialize Active Directory Service: {ex.Message}. Please check your configuration.", "AD Service Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            _adService = null; // Ensure it's null if initialization failed
                        }
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

        private void BtnLoadList_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog { Filter = "Text Files (*.txt)|*.txt" };
            if (openFileDialog.ShowDialog() == true)
            {
                _loadedComputerNames.Clear();
                _loadedComputerNames.AddRange(File.ReadAllLines(openFileDialog.FileName).Where(l => !string.IsNullOrWhiteSpace(l)).Select(l => l.Trim().ToUpper()));
                StatusText.Text = $"Loaded {_loadedComputerNames.Count} computers.";
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
            
            if (_adService == null || string.IsNullOrWhiteSpace(_adService.DomainUser) || string.IsNullOrWhiteSpace(_adService.DomainPass))
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

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv",
                FileName = $"AdminInfoExport_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                Title = "Export Results to CSV"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                try
                {
                    ExportToCsv(saveFileDialog.FileName);
                    
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = saveFileDialog.FileName,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Export failed: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void ExportToCsv(string filePath)
        {
            var currentData = MainDataGrid.ItemsSource.Cast<object>().ToList();
            if (currentData.Count == 0) return;

            var properties = currentData.GetType().GetProperties();

            using (StreamWriter sw = new StreamWriter(filePath, false, System.Text.Encoding.UTF8))
            {
                sw.WriteLine(string.Join(",", properties.Select(p => p.Name)));

                foreach (var item in currentData)
                {
                    var values = properties.Select(p => 
                    {
                        var val = p.GetValue(item, null)?.ToString() ?? "";
                        return $"\"{val.Replace("\"", "\"\"")}\"";
                    });
                    
                    sw.WriteLine(string.Join(",", values));
                }
            }
        }

        // --- ACCORDION LOGIC ---
        private void Accordion_Expanded(object sender, RoutedEventArgs e)
        {
            // Ensure we are interacting with an Expander
            if (sender is System.Windows.Controls.Expander expandedExpander)
            {
                var allExpanders = new[] { ExpanderQuery, ExpanderCreate, ExpanderStatus, ExpanderManage, ExpanderDelete };
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
            PanelSequentialInput.Visibility = RdoCreateSequential.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }

        // --- USER MANAGEMENT CODE ---
        private async void BtnGetUserAdInfo_Click(object sender, RoutedEventArgs e)
        {
            var usersToQuery = GetTargetUsers(); // Use the new textbox reader!
            if (usersToQuery.Length == 0) return;
            
            if (string.IsNullOrWhiteSpace(_sessionUsername) || string.IsNullOrWhiteSpace(_sessionPassword))
            {
                MessageBox.Show("Please set your AD Credentials in the Options menu first.", "Credentials Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                SwitchView(ViewOptions);
                return;
            }

            if (_adService == null || string.IsNullOrWhiteSpace(_adService.DomainUser) || string.IsNullOrWhiteSpace(_adService.DomainPass))
            {
                MessageBox.Show("Active Directory service not initialized or credentials not set. Please load settings and set credentials first.", "Setup Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                SwitchView(ViewOptions);
                return;
            }
            // Credentials should already be set on _adService if it's not null, from BtnLoadSettingsOptions_Click or BtnUserMenu_Click

            BtnGetUserAdInfo.IsEnabled = false; 
            
            UserDataGrid.ItemsSource = UserResults;
            UserResults.Clear();
            
            StatusText.Text = "Querying Active Directory Users... Please wait.";

            foreach (var user in usersToQuery) // Loop through the new array
            {
                // Run in background so UI doesn't freeze
                var result = await System.Threading.Tasks.Task.Run(() => _adService.GetAdUserInfo(user));
                UserResults.Add(result);
            }

            StatusText.Text = $"User Query complete. Processed {usersToQuery.Length} users.";
            BtnGetUserAdInfo.IsEnabled = true;
        }

        private void BtnExportUsers_Click(object sender, RoutedEventArgs e)
        {
            if (UserDataGrid.Items.Count == 0 || UserDataGrid.ItemsSource == null)
            {
                MessageBox.Show("There is no data to export.", "Empty", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            SaveFileDialog saveFileDialog = new SaveFileDialog
            {
                Filter = "CSV File (*.csv)|*.csv",
                FileName = $"UserExport_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (saveFileDialog.ShowDialog() == true)
            {
                // We can reuse the exact same CSV exporter from System Info!
                // Temporarily swap the datagrid source to run the export
                var originalSource = MainDataGrid.ItemsSource;
                MainDataGrid.ItemsSource = UserDataGrid.ItemsSource;
                ExportToCsv(saveFileDialog.FileName);
                MainDataGrid.ItemsSource = originalSource;
                
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = saveFileDialog.FileName, UseShellExecute = true });
            }
        }

        // --- NEW USER MANAGEMENT LOGIC ---
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

        private string[] GetTargetUsers()
        {
            return TxtUsernames.Text.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                                    .Select(h => h.Trim()).Where(h => !string.IsNullOrEmpty(h)).ToArray();
        }

        private void LogUserMessage(string message)
        {
            ListUserLogs.Items.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            ListUserLogs.ScrollIntoView(ListUserLogs.Items[ListUserLogs.Items.Count - 1]);
        }

        private void ProcessUserAction(string actionName, Func<string, bool> adOperation)
        {
            if (_adService == null || string.IsNullOrWhiteSpace(_adService.DomainUser) || string.IsNullOrWhiteSpace(_adService.DomainPass))
            {
                MessageBox.Show("Active Directory service not initialized or credentials not set. Please load settings and set credentials first.", "Setup Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                SwitchView(ViewOptions);
                return;
            }

            var users = GetTargetUsers();
            if (users.Length == 0) return;

            foreach (var user in users)
            {
                LogUserMessage($"ACTION: {actionName,-12} | TARGET: {user,-15} | STATUS: Attempting...");
                bool success = adOperation(user);
                LogUserMessage($"ACTION: {actionName,-12} | TARGET: {user,-15} | STATUS: {(success ? "SUCCESS" : "FAILED")}");
            }
        }

        private void BtnUserSetOrg_Click(object sender, RoutedEventArgs e)
        {
            string dept = TxtUserDept.Text;
            string title = TxtUserTitle.Text;
            if (string.IsNullOrWhiteSpace(dept) && string.IsNullOrWhiteSpace(title))
            {
                MessageBox.Show("Please enter a Department or Job Title.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            ProcessUserAction("Set Org Info", u => _adService.SetUserOrganization(u, dept, title));
        }

        // --- LOAD / SAVE USER LISTS ---
        private void BtnUserLoadHosts_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog { Filter = "Text Files (*.txt)|*.txt" };
            if (openFileDialog.ShowDialog() == true) TxtUsernames.Text = File.ReadAllText(openFileDialog.FileName);
        }

        private void BtnUserSaveHosts_Click(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog { Filter = "Text Files (*.txt)|*.txt", FileName = "TargetUsers.txt" };
            if (saveFileDialog.ShowDialog() == true) File.WriteAllText(saveFileDialog.FileName, TxtUsernames.Text);
        }
    }
}
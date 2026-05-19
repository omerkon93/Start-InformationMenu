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
using System.Security.AccessControl;
using System.Windows.Media;
using AdminInfoTools.ViewModels;

namespace AdminInfoTools.Views
{
    public partial class MainWindow : Window
    {
        private readonly ConfigurationService _configService;
        private readonly SystemInfoService _systemInfoService;
        private readonly CredentialService _credentialService;
        private ActiveDirectoryService _adService; 
        private NtfsManagementService _ntfsService;
        private ComputerManagementViewModel _computerViewModel;
        private OuManagementViewModel _ouViewModel;
        private NtfsManagementViewModel _ntfsViewModel;
        private ComputerActionsViewModel _computerActionsViewModel;
        private SettingsViewModel _settingsViewModel;
        

        public MainWindow()
        {
            InitializeComponent();

            // --- 2. INITIALIZE SERVICES ---
            _configService = new ConfigurationService();
            _systemInfoService = new SystemInfoService();
            _credentialService = new CredentialService();
            _ntfsService = new NtfsManagementService(new LogService(), null);
            
            _ntfsViewModel = new NtfsManagementViewModel(_ntfsService, null)
            {
                UpdateStatus = (msg) => StatusText.Text = msg
            };
            ViewNtfsManagement.DataContext = _ntfsViewModel;

            _settingsViewModel = new SettingsViewModel(_configService, _credentialService)
            {
                UpdateStatus = (msg) => StatusText.Text = msg,
                OnSettingsLoaded = InitializeViewModelsAfterSettingsLoaded,
                OnCredentialsUpdated = () =>
                {
                    if (_adService != null)
                    {
                        _adService.DomainUser = _settingsViewModel.UseNativeCredentials ? null : _credentialService.Username;
                        _adService.DomainPass = _settingsViewModel.UseNativeCredentials ? null : _credentialService.Password;
                    }
                }
            };
            ViewOptions.DataContext = _settingsViewModel;

            // --- 3. BIND EVENTS ---

            BtnComputerMenu.Click += BtnComputerMenu_Click;
            BtnUserMenu.Click += (s, e) => SwitchView(ViewUserManagement);
            BtnOuMenu.Click += BtnOuMenu_Click;
            BtnNtfsMenu.Click += BtnNtfsMenu_Click;
            BtnComputerActions.Click += (s, e) => 
            { 
                SwitchView(ViewComputerActions); 
                StatusText.Text = "Computer Actions mode."; 
            };
            
            // Options Menu Events
            BtnOptionsMenu.Click += (s, e) => SwitchView(ViewOptions);

            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Automatically simulate the settings load on startup
            if (_settingsViewModel.LoadSettingsCommand.CanExecute(null))
            {
                _settingsViewModel.LoadSettingsCommand.Execute(null);
            }
        }

        // --- VIEW NAVIGATION ---
        private void SwitchView(UIElement activeView)
        {
            ViewComputerManagement.Visibility = Visibility.Collapsed;
            if (ViewOptions != null) ViewOptions.Visibility = Visibility.Collapsed; 
            if (ViewUserManagement != null) ViewUserManagement.Visibility = Visibility.Collapsed;
            if (ViewOuManagement != null) ViewOuManagement.Visibility = Visibility.Collapsed;
            if (ViewNtfsManagement != null) ViewNtfsManagement.Visibility = Visibility.Collapsed;
            if (ViewComputerActions != null) ViewComputerActions.Visibility = Visibility.Collapsed;
            
            activeView.Visibility = Visibility.Visible;
        }

        private void BtnComputerMenu_Click(object sender, RoutedEventArgs e)
        {
            if (_configService.CurrentSettings == null)
            {
                MessageBox.Show("Please load a Settings file first.", "Setup Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_settingsViewModel.UseNativeCredentials && !_credentialService.AreCredentialsSet)
            {
                MessageBox.Show("Please set your AD Credentials in the Options menu first.", "Credentials Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                SwitchView(ViewOptions); // Automatically route them to the options page
                return;
            }

            if (_adService == null) _adService = new ActiveDirectoryService(_configService); // This should have been initialized on config load
            
            // Pass the session credentials to the service
            _adService.DomainUser = _settingsViewModel.UseNativeCredentials ? null : _credentialService.Username;
            _adService.DomainPass = _settingsViewModel.UseNativeCredentials ? null : _credentialService.Password;
            
            SwitchView(ViewComputerManagement);
            StatusText.Text = "Computer Management mode.";
        }

        private void BtnOuMenu_Click(object sender, RoutedEventArgs e)
        {
            if (_configService.CurrentSettings == null)
            {
                MessageBox.Show("Please load a Settings file first.", "Setup Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (!_settingsViewModel.UseNativeCredentials && !_credentialService.AreCredentialsSet)
            {
                MessageBox.Show("Please set your AD Credentials in the Options menu first.", "Credentials Required", MessageBoxButton.OK, MessageBoxImage.Warning);
                SwitchView(ViewOptions);
                return;
            }

            if (_adService == null) _adService = new ActiveDirectoryService(_configService);
            
            _adService.DomainUser = _settingsViewModel.UseNativeCredentials ? null : _credentialService.Username;
            _adService.DomainPass = _settingsViewModel.UseNativeCredentials ? null : _credentialService.Password;

            SwitchView(ViewOuManagement);
            StatusText.Text = "OU Management mode.";


            if (_ouViewModel != null && (_ouViewModel.OuHierarchy == null || _ouViewModel.OuHierarchy.Count == 0))
            {
                _ouViewModel.RefreshCommand.Execute(null);
            }
        }

        private void BtnNtfsMenu_Click(object sender, RoutedEventArgs e)
        {
            SwitchView(ViewNtfsManagement);
            StatusText.Text = "NTFS Management mode.";
        }

        private void OuTreeViewItem_PreviewMouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // This ensures that the item being right-clicked is the one that gets selected.
            if (sender is TreeViewItem item)
            {
                item.IsSelected = true;
            }
        }

        // --- ACTIVE DIRECTORY ACTIONS ---

        // --- REMOTE EXECUTION ---
        // --- SYSTEM INFO CODE ---
        // --- SETTINGS & OPTIONS CODE ---

        private void InitializeViewModelsAfterSettingsLoaded()
        {
            if (_adService == null)
            {
                try
                {
                    _adService = new ActiveDirectoryService(_configService);
                    _adService.DomainUser = _settingsViewModel.UseNativeCredentials ? null : _credentialService.Username;
                    _adService.DomainPass = _settingsViewModel.UseNativeCredentials ? null : _credentialService.Password;

                    this.DataContext = new UserManagementViewModel(_adService);
                    
                    _computerViewModel = new ComputerManagementViewModel(_adService, _configService, _credentialService, _systemInfoService)
                    {
                        UpdateStatus = (msg) => StatusText.Text = msg
                    };
                    ViewComputerManagement.DataContext = _computerViewModel;

                    _ouViewModel = new OuManagementViewModel(_adService)
                    {
                        UpdateStatus = (msg) => StatusText.Text = msg
                    };
                    ViewOuManagement.DataContext = _ouViewModel;
                    
                    _ntfsService = new NtfsManagementService(new LogService(), _adService);
                    _ntfsViewModel = new NtfsManagementViewModel(_ntfsService, _adService)
                    {
                        UpdateStatus = (msg) => StatusText.Text = msg
                    };
                    ViewNtfsManagement.DataContext = _ntfsViewModel;
                    
                    _computerActionsViewModel = new ComputerActionsViewModel(_adService, _configService, _credentialService)
                    {
                        UpdateStatus = (msg) => StatusText.Text = msg
                    };
                    ViewComputerActions.DataContext = _computerActionsViewModel;
                }
                catch (InvalidOperationException ex)
                {
                    MessageBox.Show($"Failed to initialize Active Directory Service: {ex.Message}. Please check your configuration.", "AD Service Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    _adService = null;
                }
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
    }
}
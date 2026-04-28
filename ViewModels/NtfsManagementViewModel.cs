using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using AdminInfoTools.Models;
using AdminInfoTools.Services;
using AdminInfoTools.Helpers;

namespace AdminInfoTools.ViewModels
{
    public class NtfsManagementViewModel : ViewModelBase
    {
        private readonly NtfsManagementService _ntfsService;
        private readonly ActiveDirectoryService _adService;

        public Action<string> UpdateStatus { get; set; }

        // --- Properties ---

        private string _targetPath;
        public string TargetPath
        {
            get => _targetPath;
            set { _targetPath = value; OnPropertyChanged(); }
        }

        private string _targetIdentities;
        public string TargetIdentities
        {
            get => _targetIdentities;
            set 
            { 
                _targetIdentities = value; 
                IdentityForeground = Brushes.Black; // Reset color on typing
                OnPropertyChanged(); 
            }
        }

        private Brush _identityForeground = Brushes.Black;
        public Brush IdentityForeground
        {
            get => _identityForeground;
            set { _identityForeground = value; OnPropertyChanged(); }
        }

        public ObservableCollection<FilePermissionRule> Permissions { get; } = new ObservableCollection<FilePermissionRule>();

        private FilePermissionRule _selectedPermission;
        public FilePermissionRule SelectedPermission
        {
            get => _selectedPermission;
            set
            {
                _selectedPermission = value;
                OnPropertyChanged();
                if (_selectedPermission != null)
                {
                    TargetIdentities = _selectedPermission.Identity;
                    
                    if (Enum.TryParse<FileSystemRights>(_selectedPermission.Rights, out var right))
                        SelectedRight = right;
                        
                    if (Enum.TryParse<AccessControlType>(_selectedPermission.AccessType, out var accessType))
                        SelectedAccessType = accessType;
                }
            }
        }

        public IEnumerable<FileSystemRights> AvailableRights { get; } = new[] 
        { 
            FileSystemRights.FullControl, 
            FileSystemRights.Modify, 
            FileSystemRights.ReadAndExecute, 
            FileSystemRights.Read, 
            FileSystemRights.Write 
        };

        public IEnumerable<AccessControlType> AvailableAccessTypes { get; } = Enum.GetValues(typeof(AccessControlType)).Cast<AccessControlType>();

        private FileSystemRights _selectedRight = FileSystemRights.ReadAndExecute;
        public FileSystemRights SelectedRight
        {
            get => _selectedRight;
            set { _selectedRight = value; OnPropertyChanged(); }
        }

        private AccessControlType _selectedAccessType = AccessControlType.Allow;
        public AccessControlType SelectedAccessType
        {
            get => _selectedAccessType;
            set { _selectedAccessType = value; OnPropertyChanged(); }
        }

        // --- Commands ---

        public ICommand BrowseCommand { get; }
        public ICommand LoadPermissionsCommand { get; }
        public ICommand LoadIdentitiesFileCommand { get; }
        public ICommand CheckNameCommand { get; }
        public ICommand AddPermissionCommand { get; }
        public ICommand RemovePermissionCommand { get; }

        public NtfsManagementViewModel(NtfsManagementService ntfsService, ActiveDirectoryService adService)
        {
            _ntfsService = ntfsService;
            _adService = adService;

            BrowseCommand = new RelayCommand(_ => ExecuteBrowse());
            LoadPermissionsCommand = new RelayCommand(_ => ExecuteLoadPermissions());
            LoadIdentitiesFileCommand = new RelayCommand(_ => ExecuteLoadIdentitiesFile());
            CheckNameCommand = new RelayCommand(_ => ExecuteCheckName());
            AddPermissionCommand = new RelayCommand(_ => ModifyNtfsPermission(true));
            RemovePermissionCommand = new RelayCommand(_ => ModifyNtfsPermission(false));
        }

        // --- Command Logic ---

        private void ExecuteBrowse()
        {
            OpenFileDialog openFileDialog = new OpenFileDialog
            {
                Title = "Select a File or Folder",
                CheckFileExists = false,
                CheckPathExists = true,
                FileName = "Select Folder",
                Filter = "All Files (*.*)|*.*"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string path = openFileDialog.FileName;
                if (path.EndsWith("Select Folder", StringComparison.OrdinalIgnoreCase))
                    path = Path.GetDirectoryName(path);
                
                if (!string.IsNullOrWhiteSpace(path)) TargetPath = path;
            }
        }

        private void ExecuteLoadPermissions()
        {
            if (string.IsNullOrWhiteSpace(TargetPath) || (!Directory.Exists(TargetPath) && !File.Exists(TargetPath)))
            {
                MessageBox.Show("Please enter a valid file, directory, or UNC share path.", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            UpdateStatus?.Invoke("Loading NTFS Permissions...");
            try
            {
                var perms = _ntfsService.GetPermissions(TargetPath);
                Permissions.Clear();
                foreach(var p in perms) Permissions.Add(p);
                UpdateStatus?.Invoke($"Loaded {perms.Count} permission rules.");
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("Access Denied. You do not have permission to read the security descriptor for this path.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus?.Invoke("Access Denied reading NTFS permissions.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load permissions:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus?.Invoke("Error reading NTFS permissions.");
            }
        }

        private void ExecuteLoadIdentitiesFile()
        {
            var openFileDialog = new OpenFileDialog { Filter = "Text Files (*.txt)|*.txt|CSV Files (*.csv)|*.csv|All Files (*.*)|*.*", Title = "Select Identity List" };
            if (openFileDialog.ShowDialog() == true)
            {
                try { TargetIdentities = File.ReadAllText(openFileDialog.FileName); }
                catch (Exception ex) { MessageBox.Show($"Error reading file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error); }
            }
        }

        private void ExecuteCheckName()
        {
            var identities = GetParsedIdentities();
            if (identities.Count == 0)
            {
                MessageBox.Show("Please enter an identity to check.", "Empty Field", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (_adService == null)
            {
                MessageBox.Show("Active Directory service is not available. Please load a configuration file in the Options menu to check accounts.", "AD Service Not Ready", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var invalidIdentities = identities.Where(identity => !_adService.IsValidAdIdentity(identity)).ToList();

            if (invalidIdentities.Count == 0)
            {
                IdentityForeground = Brushes.LightGreen;
            }
            else
            {
                IdentityForeground = Brushes.Red;
                MessageBox.Show($"The following identities were not found in Active Directory or as local built-in accounts:\n\n{string.Join("\n", invalidIdentities)}", "Check Name Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ModifyNtfsPermission(bool isAdd)
        {
            var identities = GetParsedIdentities();

            if (isAdd && _adService != null)
            {
                var invalidIdentities = identities.Where(id => !_adService.IsValidAdIdentity(id)).ToList();
                if (invalidIdentities.Count > 0)
                {
                    MessageBox.Show($"The following identities are not valid:\n\n{string.Join("\n", invalidIdentities)}\n\nPlease use the 'Check Name' button to verify before adding permissions.", "Invalid Identities", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else if (isAdd && _adService == null)
            {
                MessageBox.Show("Active Directory service is not available. Cannot validate and add permissions. Please load a configuration file first.", "AD Service Not Ready", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrWhiteSpace(TargetPath) || (!Directory.Exists(TargetPath) && !File.Exists(TargetPath)))
            {
                MessageBox.Show("Please enter a valid file or directory target path.", "Invalid Path", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (identities.Count == 0)
            {
                MessageBox.Show("Please enter at least one valid User or Group identity (e.g., DOMAIN\\User).", "Missing Identity", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                if (isAdd) _ntfsService.AddPermissionsBatch(TargetPath, identities, SelectedRight, SelectedAccessType);
                else _ntfsService.RemovePermissionsBatch(TargetPath, identities, SelectedRight, SelectedAccessType);

                MessageBox.Show($"Permission successfully {(isAdd ? "added" : "removed")}.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                ExecuteLoadPermissions(); // Refresh grid
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to modify permissions:\n{ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateStatus?.Invoke($"Error {(isAdd ? "adding" : "removing")} permission.");
            }
        }

        private List<string> GetParsedIdentities()
        {
            if (string.IsNullOrWhiteSpace(TargetIdentities)) return new List<string>();
            return TargetIdentities.Split(new[] { ',', ';', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                                   .Select(i => i.Trim()).Where(i => !string.IsNullOrWhiteSpace(i)).ToList();
        }
    }
}
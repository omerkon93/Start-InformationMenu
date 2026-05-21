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

        public ObservableCollection<IdentitySuggestion> IdentitySuggestions { get; } = new ObservableCollection<IdentitySuggestion>();

        public bool HasSuggestions => IdentitySuggestions.Count > 0;

        private IdentitySuggestion _selectedSuggestion;
        public IdentitySuggestion SelectedSuggestion
        {
            get => _selectedSuggestion;
            set
            {
                _selectedSuggestion = value;
                OnPropertyChanged();
                if (value != null)
                {
                    ApplySuggestion(value);
                    _selectedSuggestion = null;
                    OnPropertyChanged(nameof(SelectedSuggestion));
                }
            }
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

        private bool _isTemporaryPermission;
        public bool IsTemporaryPermission
        {
            get => _isTemporaryPermission;
            set { _isTemporaryPermission = value; OnPropertyChanged(); }
        }

        private int _temporaryDurationHours = 2;
        public int TemporaryDurationHours
        {
            get => _temporaryDurationHours;
            set { _temporaryDurationHours = value; OnPropertyChanged(); }
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

            var invalidIdentities = new List<string>();
            var suggestionsText = new List<string>();
            IdentitySuggestions.Clear();

            foreach (var identity in identities)
            {
                if (!_adService.IsValidAdIdentity(identity))
                {
                    invalidIdentities.Add(identity);
                    var suggestions = _adService.FindSimilarIdentities(identity);
                    if (suggestions != null && suggestions.Count > 0)
                    {
                        suggestionsText.Add($"'{identity}' not found. Did you mean:\n  - {string.Join("\n  - ", suggestions)}");
                        foreach (var sug in suggestions)
                        {
                            string actual = sug.Contains(" (") ? sug.Substring(0, sug.IndexOf(" (")) : sug;
                            IdentitySuggestions.Add(new IdentitySuggestion 
                            { 
                                OriginalQuery = identity, 
                                SuggestionText = $"For '{identity}': {sug}", 
                                ActualIdentity = actual 
                            });
                        }
                    }
                    else
                    {
                        suggestionsText.Add($"'{identity}' not found.");
                    }
                }
            }

            OnPropertyChanged(nameof(HasSuggestions));

            if (invalidIdentities.Count == 0)
            {
                IdentityForeground = Brushes.LightGreen;
                MessageBox.Show("All identities are valid.", "Check Name Success", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                IdentityForeground = Brushes.Red;
                MessageBox.Show($"The following identities were not found:\n\n{string.Join("\n\n", suggestionsText)}\n\nYou can click on a suggestion below the text box to auto-fill it.", "Check Name Failed", MessageBoxButton.OK, MessageBoxImage.Error);
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
                    var suggestionsText = new List<string>();
                    IdentitySuggestions.Clear();
                    foreach (var id in invalidIdentities)
                    {
                        var suggestions = _adService.FindSimilarIdentities(id);
                        if (suggestions != null && suggestions.Count > 0)
                        {
                            suggestionsText.Add($"'{id}' not found. Did you mean:\n  - {string.Join("\n  - ", suggestions)}");
                            foreach (var sug in suggestions)
                            {
                                string actual = sug.Contains(" (") ? sug.Substring(0, sug.IndexOf(" (")) : sug;
                                IdentitySuggestions.Add(new IdentitySuggestion 
                                { 
                                    OriginalQuery = id, 
                                    SuggestionText = $"For '{id}': {sug}", 
                                    ActualIdentity = actual 
                                });
                            }
                        }
                        else
                        {
                            suggestionsText.Add($"'{id}' not found.");
                        }
                    }
                    OnPropertyChanged(nameof(HasSuggestions));
                    MessageBox.Show($"The following identities are not valid:\n\n{string.Join("\n\n", suggestionsText)}\n\nPlease click on a suggestion below the text box to auto-fill it, or use the 'Check Name' button.", "Invalid Identities", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                if (isAdd)
                {
                    if (IsTemporaryPermission && TemporaryDurationHours > 0)
                    {
                        foreach (var identity in identities)
                        {
                            _ntfsService.AddTemporaryPermission(TargetPath, identity, SelectedRight, SelectedAccessType, TimeSpan.FromHours(TemporaryDurationHours));
                        }
                    }
                    else
                    {
                        _ntfsService.AddPermissionsBatch(TargetPath, identities, SelectedRight, SelectedAccessType);
                    }
                }
                else 
                {
                    _ntfsService.RemovePermissionsBatch(TargetPath, identities, SelectedRight, SelectedAccessType);
                }

                MessageBox.Show($"Permission successfully {(isAdd ? "added" : "removed")}.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                ExecuteLoadPermissions(); // Refresh grid
            }
            catch (IdentityAmbiguousException ex)
            {
                MessageBox.Show(ex.Message, "Ambiguous Identity", MessageBoxButton.OK, MessageBoxImage.Warning);
                UpdateStatus?.Invoke("Error: Ambiguous Identity.");
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

        private void ApplySuggestion(IdentitySuggestion suggestion)
        {
            if (string.IsNullOrWhiteSpace(TargetIdentities)) return;
            
            var separators = new[] { ',', ';', '\r', '\n' };
            var parts = TargetIdentities.Split(separators, StringSplitOptions.None).ToList();
            
            bool replaced = false;
            for (int i = 0; i < parts.Count; i++)
            {
                if (parts[i].Trim().Equals(suggestion.OriginalQuery, StringComparison.OrdinalIgnoreCase))
                {
                    parts[i] = parts[i].Replace(parts[i].Trim(), suggestion.ActualIdentity);
                    replaced = true;
                }
            }
            
            if (replaced)
            {
                TargetIdentities = string.Join("\r\n", parts.Where(p => !string.IsNullOrWhiteSpace(p.Trim())));
            }
            
            var toRemove = IdentitySuggestions.Where(s => s.OriginalQuery == suggestion.OriginalQuery).ToList();
            foreach (var item in toRemove)
            {
                IdentitySuggestions.Remove(item);
            }
            OnPropertyChanged(nameof(HasSuggestions));
            
            if (!HasSuggestions) IdentityForeground = Brushes.Black;
        }
    }

    public class IdentitySuggestion
    {
        public string OriginalQuery { get; set; }
        public string SuggestionText { get; set; }
        public string ActualIdentity { get; set; }
    }
}
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;
using AdminInfoTools.Models;
using AdminInfoTools.Services;
using System.Linq;
using System;
using System.Diagnostics;
using System.Windows;
using AdminInfoTools.Helpers;

namespace AdminInfoTools.ViewModels
{
    public enum UserUpdateableFields
    {
        Department,
        JobTitle
    }

    public class UserManagementViewModel : ViewModelBase
    {
        private readonly ActiveDirectoryService _adService;
        private readonly LogService _logger;

        // 1. Properties (Data bound to the UI)
        public ObservableCollection<AdUserInfoResult> UserResults { get; set; } = new ObservableCollection<AdUserInfoResult>();
        public ObservableCollection<string> UserLogs { get; set; } = new ObservableCollection<string>();

        private string _targetUsersText;
        public string TargetUsersText
        {
            get => _targetUsersText;
            set
            {
                _targetUsersText = value;
                OnPropertyChanged(); // Tells the UI the text changed
            }
        }

        private string _statusText = "Ready.";
        public string StatusText
        {
            get => _statusText;
            set
            {
                _statusText = value;
                OnPropertyChanged();
            }
        }

        // 2. Commands (Bound to Buttons)
        public ICommand GetUserInfoCommand { get; }
        public ICommand ExportUsersCommand { get; }
        public ICommand UnlockUserCommand { get; }
        public ICommand EnableUserCommand { get; }
        public ICommand DisableUserCommand { get; }
        public ICommand SetOrganizationCommand { get; }

        // 3. Constructor
        public UserManagementViewModel(ActiveDirectoryService adService)
        {
            _adService = adService;
            _logger = new LogService();
            GetUserInfoCommand = new RelayCommand(async (param) => await ExecuteGetUserInfoAsync());
            
            ExportUsersCommand = new RelayCommand(ExecuteExportUsers);
            UnlockUserCommand = new RelayCommand((param) => ProcessUserAction("Unlock", u => _adService.UnlockUserAccount(u)));
            EnableUserCommand = new RelayCommand((param) => ProcessUserAction("Enable", u => _adService.SetUserStatus(u, true)));
            DisableUserCommand = new RelayCommand((param) => ProcessUserAction("Disable", u => _adService.SetUserStatus(u, false)));
            SetOrganizationCommand = new RelayCommand((param) => ExecuteSetOrganization());
        }

        // 4. The Logic (Moved from BtnGetUserAdInfo_Click)
        private async Task ExecuteGetUserInfoAsync()
        {
            if (string.IsNullOrWhiteSpace(TargetUsersText)) return;

            var usersToQuery = TargetUsersText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                                              .Select(h => h.Trim())
                                              .Where(h => !string.IsNullOrEmpty(h))
                                              .ToArray();

            StatusText = "Querying Active Directory Users... Please wait.";
            UserResults.Clear();

            foreach (var user in usersToQuery)
            {
                var result = await Task.Run(() => _adService.GetAdUserInfo(user));
                UserResults.Add(result);
            }

            StatusText = $"User Query complete. Processed {usersToQuery.Length} users.";
        }

        private void ProcessUserAction(string actionName, Func<string, bool> adOperation)
        {
            var users = TargetUsersText?.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(h => h.Trim()).Where(h => !string.IsNullOrEmpty(h)).ToArray();
                                        
            if (users == null || users.Length == 0) return;

            foreach (var user in users)
            {
                LogMessage($"ACTION: {actionName,-12} | TARGET: {user,-15} | STATUS: Attempting...");
                bool success = adOperation(user);
                LogMessage($"ACTION: {actionName,-12} | TARGET: {user,-15} | STATUS: {(success ? "SUCCESS" : "FAILED")}");
            }
        }

        private void ExecuteSetOrganization()
        {
            var users = TargetUsersText?.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(h => h.Trim()).Where(h => !string.IsNullOrEmpty(h)).ToArray();
                                        
            if (users == null || users.Length == 0)
            {
                MessageBox.Show("Please enter target users first.", "Missing Input", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var enumValues = Enum.GetValues(typeof(UserUpdateableFields)).Cast<UserUpdateableFields>().ToArray();
            string promptMessage = "Select field to update by entering its number:\n\n" + 
                                   string.Join("\n", enumValues.Select((e, i) => $"{i + 1}. {e}"));

            string choiceStr = DialogHelper.ShowInputDialog(promptMessage, "Select Field");
            
            if (int.TryParse(choiceStr, out int choice) && choice >= 1 && choice <= enumValues.Length)
            {
                var selectedField = enumValues[choice - 1];
                string newValue = DialogHelper.ShowInputDialog($"Enter new value for {selectedField}:", "Enter Value");

                if (newValue != null)
                {
                    // Reuse ProcessUserAction for consistency and automatic logging
                    ProcessUserAction($"SetOrg ({selectedField})", user =>
                    {
                        if (selectedField == UserUpdateableFields.Department)
                            return _adService.SetUserOrganization(user, newValue, null);
                        else
                            return _adService.SetUserOrganization(user, null, newValue);
                    });
                }
            }
            else if (!string.IsNullOrWhiteSpace(choiceStr))
            {
                 MessageBox.Show("Invalid selection.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void LogMessage(string message)
        {
            // Using Dispatcher to ensure UI thread updates smoothly if called asynchronously
            System.Windows.Application.Current.Dispatcher.Invoke(() =>
            {
                UserLogs.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
            });
        }

        private void ExecuteExportUsers(object obj)
        {
            try
            {
                if (UserResults == null || UserResults.Count == 0) return;
                
                // Reusing ActiveDirectoryObjectQuery category, but prefixing with "UserQuery"
                string path = _logger.GetNewFilePath(LogCategory.ActiveDirectoryObjectQuery, "UserQuery", ".csv");
                CsvExportService.Export(UserResults, path);
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
            }
            catch (Exception ex) { MessageBox.Show($"Export failed: {ex.Message}", "Export Error", MessageBoxButton.OK, MessageBoxImage.Error); }
        }
    }
}
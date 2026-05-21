using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Input;
using AdminInfoTools.Models;
using AdminInfoTools.Services;
using AdminInfoTools.Helpers;

namespace AdminInfoTools.ViewModels
{
    public class OuManagementViewModel : ViewModelBase
    {
        private readonly IADOuService _ouService;
        public Action<string> UpdateStatus { get; set; }

        private ObservableCollection<OuNode> _ouHierarchy;
        public ObservableCollection<OuNode> OuHierarchy
        {
            get => _ouHierarchy;
            set { _ouHierarchy = value; OnPropertyChanged(); }
        }

        public ICommand RefreshCommand { get; }
        public ICommand CreateOuCommand { get; }
        public ICommand RenameOuCommand { get; }
        public ICommand DeleteOuCommand { get; }
        public ICommand MoveOuCommand { get; }

        public OuManagementViewModel(IADOuService ouService)
        {
            _ouService = ouService;
            RefreshCommand = new RelayCommand(_ => ExecuteRefresh());
            CreateOuCommand = new RelayCommand(param => ExecuteCreate(param as OuNode));
            RenameOuCommand = new RelayCommand(param => ExecuteRename(param as OuNode));
            DeleteOuCommand = new RelayCommand(param => ExecuteDelete(param as OuNode));
            MoveOuCommand = new RelayCommand(param => ExecuteMove(param as OuNode));
        }

        private void ExecuteRefresh()
        {
            if (_ouService == null) return;
            UpdateStatus?.Invoke("Refreshing OU Hierarchy...");
            OuHierarchy = _ouService.GetOuHierarchy();
            UpdateStatus?.Invoke("OU Hierarchy refreshed.");
        }

        private void ExecuteCreate(OuNode parentNode)
        {
            if (parentNode == null) return;
            string newOuName = DialogHelper.ShowInputDialog($"Enter the name for the new Sub-OU under '{parentNode.Name}':", "Create Sub-OU");
            if (!string.IsNullOrWhiteSpace(newOuName))
            {
                if (_ouService.CreateOu(parentNode.DistinguishedName, newOuName)) ExecuteRefresh();
                else MessageBox.Show("Failed to create OU. Please check logs for details.", "Create Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExecuteRename(OuNode targetNode)
        {
            if (targetNode == null) return;
            string newOuName = DialogHelper.ShowInputDialog($"Enter the new name for '{targetNode.Name}':", "Rename OU");
            if (!string.IsNullOrWhiteSpace(newOuName))
            {
                if (_ouService.RenameOu(targetNode.DistinguishedName, newOuName)) ExecuteRefresh();
                else MessageBox.Show("Failed to rename OU. Please check logs for details.", "Rename Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExecuteDelete(OuNode targetNode)
        {
            if (targetNode == null) return;
            var confirm = MessageBox.Show($"Are you sure you want to delete the OU '{targetNode.Name}' and ALL of its contents?\n\nThis action cannot be undone!", "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                if (_ouService.DeleteOu(targetNode.DistinguishedName)) ExecuteRefresh();
                else MessageBox.Show("Failed to delete OU. Please check logs for details.", "Delete Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExecuteMove(OuNode sourceNode)
        {
            if (sourceNode == null) return;
            string destinationDn = DialogHelper.ShowOuSelectionDialog($"Move '{sourceNode.Name}'", OuHierarchy);

            if (!string.IsNullOrWhiteSpace(destinationDn))
            {
                if (destinationDn.Equals(sourceNode.DistinguishedName, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("The destination OU cannot be the same as the source OU.", "Move Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                if (destinationDn.EndsWith("," + sourceNode.DistinguishedName, StringComparison.OrdinalIgnoreCase))
                {
                    MessageBox.Show("Cannot move an OU into one of its own sub-OUs.", "Invalid Destination", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (_ouService.MoveOu(sourceNode.DistinguishedName, destinationDn)) ExecuteRefresh();
                else MessageBox.Show("Failed to move OU. Check logs for details.", "Move Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
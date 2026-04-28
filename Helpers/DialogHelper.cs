using System;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using AdminInfoTools.Models;

namespace AdminInfoTools.Helpers
{
    public static class DialogHelper
    {
        public static string ShowInputDialog(string promptText, string title)
        {
            Window inputWindow = new Window
            {
                Title = title,
                Width = 400,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current.MainWindow,
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
            inputWindow.Loaded += (s, e) => textBox.Focus();

            inputWindow.ShowDialog();
            return result;
        }

        public static string ShowOuSelectionDialog(string title, ObservableCollection<OuNode> hierarchy)
        {
            Window inputWindow = new Window
            {
                Title = title, Width = 450, Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = Application.Current.MainWindow,
                ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.ToolWindow
            };

            var bc = new System.Windows.Media.BrushConverter();
            inputWindow.Background = (System.Windows.Media.Brush)bc.ConvertFrom("#2D2D30");

            var grid = new Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var instructions = new TextBlock { Text = "Expand the hierarchy below and select the destination Organizational Unit (OU):", Foreground = System.Windows.Media.Brushes.White, Margin = new Thickness(0, 0, 0, 10), TextWrapping = TextWrapping.Wrap };
            Grid.SetRow(instructions, 0); grid.Children.Add(instructions);

            var treeView = new TreeView { Margin = new Thickness(0, 0, 0, 15), Background = (System.Windows.Media.Brush)bc.ConvertFrom("#1E1E1E"), Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0) };
            var style = new Style(typeof(TreeViewItem)); style.Setters.Add(new Setter(TreeViewItem.ForegroundProperty, System.Windows.Media.Brushes.White)); treeView.ItemContainerStyle = style;
            var template = new HierarchicalDataTemplate(typeof(OuNode)) { ItemsSource = new System.Windows.Data.Binding("Children") };
            var textFactory = new FrameworkElementFactory(typeof(TextBlock)); textFactory.SetBinding(TextBlock.TextProperty, new System.Windows.Data.Binding("Name")); textFactory.SetValue(TextBlock.PaddingProperty, new Thickness(4)); template.VisualTree = textFactory;
            treeView.ItemTemplate = template; treeView.ItemsSource = hierarchy;
            Grid.SetRow(treeView, 1); grid.Children.Add(treeView);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            Grid.SetRow(buttonPanel, 2);
            var okButton = new Button { Content = "Select", Width = 80, Height = 30, Margin = new Thickness(0, 0, 10, 0), IsDefault = true, Background = (System.Windows.Media.Brush)bc.ConvertFrom("#007ACC"), Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand };
            var cancelButton = new Button { Content = "Cancel", Width = 80, Height = 30, IsCancel = true, Background = (System.Windows.Media.Brush)bc.ConvertFrom("#3E3E42"), Foreground = System.Windows.Media.Brushes.White, BorderThickness = new Thickness(0), Cursor = System.Windows.Input.Cursors.Hand };
            string selectedDn = null;
            okButton.Click += (s, e) => { if (treeView.SelectedItem is OuNode selectedNode) { selectedDn = selectedNode.DistinguishedName; inputWindow.DialogResult = true; } else { MessageBox.Show("Please select a destination OU.", "Selection Required", MessageBoxButton.OK, MessageBoxImage.Warning); } };
            buttonPanel.Children.Add(okButton); buttonPanel.Children.Add(cancelButton); grid.Children.Add(buttonPanel);
            inputWindow.Content = grid; inputWindow.ShowDialog();
            return selectedDn;
        }
    }
}
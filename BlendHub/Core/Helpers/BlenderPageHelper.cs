using BlendHub.Models;
using BlendHub.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace BlendHub.Pages
{
    public static class BlenderPageHelper
    {
        public static int GetCategoryOrder(string category)
        {
            return category switch
            {
                "Extensions & Tools" => 1,
                "Preferences & Configuration" => 2,
                "History & Recent Data" => 3,
                _ => 99
            };
        }

        public static bool IsVersionNewer(string version1, string version2)
        {
            // Simple comparison for Blender versions (e.g. 4.1 vs 3.6)
            if (double.TryParse(version1, NumberStyles.Any, CultureInfo.InvariantCulture, out var v1) &&
                double.TryParse(version2, NumberStyles.Any, CultureInfo.InvariantCulture, out var v2))
            {
                return v1 > v2;
            }
            return string.Compare(version1, version2, StringComparison.OrdinalIgnoreCase) > 0;
        }

        public static ContentDialog CreateStyledDialog(FrameworkElement owner, string title, string content, string primaryButtonText, string secondaryButtonText = "", string closeButtonText = "")
        {
            var dialog = new ContentDialog
            {
                Title = title,
                Content = content,
                PrimaryButtonText = primaryButtonText,
                SecondaryButtonText = secondaryButtonText,
                CloseButtonText = closeButtonText,
                DefaultButton = ContentDialogButton.Secondary,
                XamlRoot = owner.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = (App.MainWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
                CloseButtonStyle = Application.Current.Resources["DefaultButtonStyle"] as Style
            };
            return dialog;
        }

        public static async Task<ContentDialogResult> ShowVersionMismatchDialog(FrameworkElement owner, string sourceVersion, string targetVersions, string actionName)
        {
            var dialog = CreateStyledDialog(
                owner,
                "Version Mismatch Warning",
                $"You are {actionName} settings from a newer version ({sourceVersion}) to older version(s) ({targetVersions}).\n\n{actionName} Preferences or Startup Files from a newer version can cause UI glitches or crashes in older Blender versions.\n\nDo you want to continue?",
                $"{actionName} Anyway",
                "Cancel"
            );

            return await dialog.ShowAsync();
        }

        public static async Task RefreshConfigItemsAsync(ObservableCollection<ConfigItemViewModel> itemsCollection, string versionPath, 
            BlenderSettingsService blenderService, Action<ConfigItemViewModel> onPropertyChanged, 
            CollectionViewSource groupedSource)
        {
            var items = await Task.Run(() => blenderService.GetDefaultBackupItems(versionPath));
            
            var existingSelection = itemsCollection.ToDictionary(i => i.Name, i => i.IsEnabled);

            itemsCollection.Clear();
            foreach (var item in items)
            {
                bool isEnabled = item.IsEnabled;
                if (existingSelection.TryGetValue(item.Name, out var wasEnabled))
                {
                    isEnabled = wasEnabled;
                }

                var vm = new ConfigItemViewModel
                {
                    Name = item.Name,
                    IsEnabled = isEnabled,
                    IsExists = item.Exists,
                    TooltipText = item.Category,
                    Category = item.Category,
                    RelativePath = item.RelativePath,
                    IsFolder = item.IsFolder
                };
                vm.PropertyChanged += (s, e) => onPropertyChanged?.Invoke(vm);
                itemsCollection.Add(vm);
            }

            // Group by category and update view
            var groups = itemsCollection
                .GroupBy(i => i.Category)
                .OrderBy(g => GetCategoryOrder(g.Key))
                .Select(g => new CategoryGroup
                {
                    Key = g.Key,
                    Items = g.ToList()
                })
                .ToList();

            groupedSource.Source = groups;
        }

        /// <summary>
        /// Common method to launch Blender from version info
        /// </summary>
        public static void LaunchBlender(BlenderVersionInfo versionInfo, BlenderSettingsService blenderService)
        {
            if (versionInfo != null && !string.IsNullOrEmpty(versionInfo.ExecutablePath) && 
                System.IO.File.Exists(versionInfo.ExecutablePath))
            {
                try
                {
                    blenderService.LaunchBlender(versionInfo.ExecutablePath);
                }
                catch { }
            }
        }
    }
}

using BlendHub.Models;
using BlendHub.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using System;
using BlendHub.Controls;
using BlendHub.Dialogs;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Windows.Storage.Pickers;
using Microsoft.Windows.ApplicationModel.Resources;

namespace BlendHub.Pages
{
    public sealed partial class ProjectPage : Page
    {
        public ViewModels.ProjectViewModel ViewModel { get; } = new();
        private readonly ResourceLoader _resourceLoader = new();

        public ProjectPage()
        {
            this.InitializeComponent();
            GroupedProjects.Source = ViewModel.GroupedProjectsCollection;
        }

        protected override void OnNavigatedTo(NavigationEventArgs e)
        {
            base.OnNavigatedTo(e);
            ViewModel.LoadProjects();
            if (e.Parameter is string parameter && !string.IsNullOrEmpty(parameter))
            {
                if (parameter == "create")
                {
                    ShowDialog_Click(this, new RoutedEventArgs());
                }
                else
                {
                    ViewModel.SearchText = parameter;
                }
            }
        }

        private void ProjectSuccessInfoBar_Closed(InfoBar sender, InfoBarClosedEventArgs args)
        {
            ViewModel.IsSuccessInfoBarOpen = false;
        }

        private void SortFieldItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioMenuFlyoutItem item && item.IsChecked)
            {
                string tag = item.Tag?.ToString() ?? "Date";
                ViewModel.SetSortField(tag);
            }
        }

        private void SortOrderItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioMenuFlyoutItem item && item.IsChecked)
            {
                string tag = item.Tag?.ToString() ?? "Desc";
                ViewModel.SetSortOrder(tag);
            }
        }

        private async void ShowDialog_Click(object sender, RoutedEventArgs e)
        {
            var content = new CreateProjectDialogContent();
            var dialog = new ContentDialog
            {
                Title = "Create New Project",
                Content = content,
                PrimaryButtonText = "Create",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = (App.MainWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
                CloseButtonStyle = Application.Current.Resources["DefaultButtonStyle"] as Style
            };

            dialog.IsPrimaryButtonEnabled = content.IsValid;
            content.ValidationChanged += (s, args) => { dialog.IsPrimaryButtonEnabled = content.IsValid; };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary)
            {
                string projectName = content.ProjectName;
                string projectLocation = content.ProjectLocation;
                string fileName = content.FileName;
                var selectedVersion = content.SelectedVersion;
                var folders = content.Folders.Where(f => f.IsSelected).Select(f => f.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList();
                var launchers = content.FileLaunchers
                    .Where(l => !string.IsNullOrWhiteSpace(l.Extension) && !string.IsNullOrWhiteSpace(l.ProgramPath))
                    .GroupBy(l => l.Extension.ToLowerInvariant())
                    .ToDictionary(g => g.Key, g => g.First().ProgramPath);

                try
                {
                    await ViewModel.CreateNewProjectAsync(projectName, projectLocation, fileName, selectedVersion, folders, launchers, content.AutoUpdatePrimaryBlend);
                }
                catch (Exception ex)
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = "Creation Failed",
                        Content = $"Could not create project folders: {ex.Message}",
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot,
                        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                        RequestedTheme = (App.MainWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default
                    };
                    await errorDialog.ShowAsync();
                }
            }
        }

        private void OpenProject_Click(object sender, RoutedEventArgs e)
        {
            if (ViewModel.LastCreatedProject != null && File.Exists(ViewModel.LastCreatedProject.FullBlendPath))
            {
                Process.Start(new ProcessStartInfo(ViewModel.LastCreatedProject.FullBlendPath) { UseShellExecute = true });
            }
        }

        private async void BrowseProjectBtn_Click(object sender, RoutedEventArgs e)
        {
            var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
            picker.FileTypeFilter.Add(".blend");
            BlendHub.Helpers.WindowHelper.InitializeWithWindow(picker);
            var file = await picker.PickSingleFileAsync();
            if (file != null)
            {
                await ProcessDroppedFolderAsync(await file.GetParentAsync());
            }
        }

        private void RootGrid_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
                if (DragOverlay != null)
                {
                    DragOverlay.Visibility = Visibility.Visible;
                }
            }
        }

        private void DragOverlay_DragOver(object sender, DragEventArgs e)
        {
            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
                e.DragUIOverride.Caption = "Add to BlendHub";
            }
        }

        private void DragOverlay_DragLeave(object sender, DragEventArgs e)
        {
            if (DragOverlay != null)
            {
                DragOverlay.Visibility = Visibility.Collapsed;
            }
        }

        private async void DragOverlay_Drop(object sender, DragEventArgs e)
        {
            if (DragOverlay != null)
            {
                DragOverlay.Visibility = Visibility.Collapsed;
            }

            if (e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
            {
                var items = await e.DataView.GetStorageItemsAsync();
                foreach (var item in items)
                {
                    if (item is Windows.Storage.StorageFolder folder)
                    {
                        await ProcessDroppedFolderAsync(folder);
                    }
                }
            }
        }

        private void DropZone_DragOver(object sender, DragEventArgs e)
        {
            RootGrid_DragOver(sender, e);
        }

        private void DropZone_Drop(object sender, DragEventArgs e)
        {
            DragOverlay_Drop(sender, e);
        }

        public class SelectedBlendResult
        {
            public string BlendFile { get; set; } = string.Empty;
            public bool AutoUpdate { get; set; }
        }

        private async Task<SelectedBlendResult?> PromptSelectBlendFileAsync(string title, string message, List<string> filePaths, string rootPath)
        {
            var autoUpdateToggle = new ToggleSwitch
            {
                Header = "Auto Update Primary .blend File",
                IsOn = false,
                Margin = new Thickness(0, 4, 0, 8)
            };

            var listbox = new ListView
            {
                SelectionMode = ListViewSelectionMode.Single,
                MaxHeight = 220,
                Margin = new Thickness(0, 8, 0, 0)
            };

            var lastModifiedFile = filePaths
                .Select(fp => new FileInfo(fp))
                .OrderByDescending(f => f.LastWriteTime)
                .FirstOrDefault();

            string primaryRelativePath = lastModifiedFile != null ? Path.GetRelativePath(rootPath, lastModifiedFile.FullName) : "";

            void PopulateListBox()
            {
                listbox.Items.Clear();
                if (autoUpdateToggle.IsOn)
                {
                    if (!string.IsNullOrEmpty(primaryRelativePath))
                    {
                        listbox.Items.Add(primaryRelativePath);
                    }
                }
                else
                {
                    foreach (var fp in filePaths)
                    {
                        listbox.Items.Add(Path.GetRelativePath(rootPath, fp));
                    }
                }

                if (listbox.Items.Count > 0)
                {
                    listbox.SelectedIndex = 0;
                }
            }

            autoUpdateToggle.Toggled += (s, args) => PopulateListBox();
            PopulateListBox();

            var stack = new StackPanel { Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
            stack.Children.Add(new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, FontSize = 14 });
            stack.Children.Add(autoUpdateToggle);
            stack.Children.Add(listbox);

            var dialog = new ContentDialog
            {
                Title = title,
                Content = stack,
                PrimaryButtonText = "Select File",
                CloseButtonText = "Cancel",
                DefaultButton = ContentDialogButton.Primary,
                XamlRoot = this.XamlRoot,
                Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                RequestedTheme = (App.MainWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default,
                CloseButtonStyle = Application.Current.Resources["DefaultButtonStyle"] as Style
            };

            dialog.IsPrimaryButtonEnabled = listbox.SelectedItem != null;
            listbox.SelectionChanged += (s, e) =>
            {
                dialog.IsPrimaryButtonEnabled = listbox.SelectedItem != null;
            };

            var result = await dialog.ShowAsync();
            if (result == ContentDialogResult.Primary && listbox.SelectedItem != null)
            {
                return new SelectedBlendResult
                {
                    BlendFile = listbox.SelectedItem.ToString() ?? string.Empty,
                    AutoUpdate = autoUpdateToggle.IsOn
                };
            }
            return null;
        }

        private async Task ProcessDroppedFolderAsync(Windows.Storage.StorageFolder folder)
        {
            if (folder == null) return;
            string path = folder.Path;

            var (rootBlendFiles, allBlendFiles, subfolders) = await Task.Run(() =>
            {
                var rbf = Directory.GetFiles(path, "*.blend");
                var abf = rbf.Length == 0 ? Directory.GetFiles(path, "*.blend", SearchOption.AllDirectories) : Array.Empty<string>();
                var subs = Directory.GetDirectories(path)
                    .Select(d => System.IO.Path.GetFileName(d))
                    .ToList();
                return (rbf, abf, subs);
            });

            string? chosenBlendFile = null;
            bool autoUpdate = false;

            if (rootBlendFiles.Length == 1)
            {
                chosenBlendFile = Path.GetFileName(rootBlendFiles[0]);
            }
            else if (rootBlendFiles.Length > 1)
            {
                var selectionResult = await PromptSelectBlendFileAsync(
                    _resourceLoader.GetString("ProjectPage_Dialog_SelectPrimaryTitle"),
                    _resourceLoader.GetString("ProjectPage_Dialog_SelectPrimaryDesc"),
                    rootBlendFiles.ToList(),
                    path);
                
                if (selectionResult == null) return;
                chosenBlendFile = selectionResult.BlendFile;
                autoUpdate = selectionResult.AutoUpdate;
            }
            else
            {
                if (allBlendFiles.Length == 0)
                {
                    var errorDialog = new ContentDialog
                    {
                        Title = _resourceLoader.GetString("ProjectPage_Dialog_NoFilesTitle"),
                        Content = string.Format(_resourceLoader.GetString("ProjectPage_Dialog_NoFilesDesc"), folder.Name),
                        CloseButtonText = "OK",
                        XamlRoot = this.XamlRoot,
                        Style = Application.Current.Resources["DefaultContentDialogStyle"] as Style,
                        RequestedTheme = (App.MainWindow.Content as FrameworkElement)?.RequestedTheme ?? ElementTheme.Default
                    };
                    await errorDialog.ShowAsync();
                    return;
                }

                var selectionResult = await PromptSelectBlendFileAsync(
                    _resourceLoader.GetString("ProjectPage_Dialog_ScanNestedTitle"),
                    _resourceLoader.GetString("ProjectPage_Dialog_ScanNestedDesc"),
                    allBlendFiles.ToList(),
                    path);

                if (selectionResult == null) return;
                chosenBlendFile = selectionResult.BlendFile;
                autoUpdate = selectionResult.AutoUpdate;
            }

            var newProject = new Project
            {
                Name = folder.Name,
                Path = path,
                BlendFileName = chosenBlendFile,
                AutoUpdatePrimaryBlend = autoUpdate,
                CreatedAt = DateTime.Now,
                BlenderVersion = "Unknown",
                Subfolders = subfolders,
                CompletionProgress = 0
            };

            if (AppSettingsService.Instance.Settings.AutoDetectBlenderVersion)
            {
                await ProjectService.DetectProjectVersionsAsync(new List<Project> { newProject });
            }

            await ViewModel.AddProjectAsync(newProject);
        }
    }
}

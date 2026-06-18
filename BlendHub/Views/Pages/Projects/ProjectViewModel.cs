using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BlendHub.Models;
using BlendHub.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;

namespace BlendHub.ViewModels
{
#pragma warning disable MVVMTK0045
    public partial class ProjectViewModel : ObservableObject
    {
        private readonly BlenderSettingsService _blenderService = new();
        private readonly ResourceLoader _resourceLoader = new();
        private List<Project> _allProjects = new();

        public ObservableCollection<Project> Projects { get; } = new();
        public ObservableCollection<ProjectGroup> GroupedProjectsCollection { get; } = new();

        [ObservableProperty]
        private string _searchText = string.Empty;

        [ObservableProperty]
        private string _currentSortField = "Date";

        [ObservableProperty]
        private string _currentSortOrder = "Desc";

        [ObservableProperty]
        private string _sortButtonText = string.Empty;

        [ObservableProperty]
        private bool _isCreating;

        [ObservableProperty]
        private string _creatingProgressText = string.Empty;

        [ObservableProperty]
        private bool _isSuccessInfoBarOpen;

        [ObservableProperty]
        private string _successInfoBarTitle = string.Empty;

        [ObservableProperty]
        private string _successInfoBarMessage = string.Empty;

        [ObservableProperty]
        private Project? _lastCreatedProject;

        [ObservableProperty]
        private bool _isProjectsListVisible;

        [ObservableProperty]
        private bool _isCategorizedProjectsListVisible;

        [ObservableProperty]
        private bool _isNoSearchResultsVisible;

        [ObservableProperty]
        private bool _isDropZoneVisible;

        [ObservableProperty]
        private bool _isProjectsListContainerVisible;

        public ProjectViewModel()
        {
        }

        partial void OnSearchTextChanged(string value)
        {
            ApplyFilterAndSort();
        }

        public void LoadProjects()
        {
            try
            {
                var loadedProjects = ProjectService.LoadProjects();
                Debug.WriteLine($"[ProjectViewModel] Loaded {loadedProjects.Count} projects from JSON");
                _allProjects = loadedProjects;
                ApplyFilterAndSort();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProjectViewModel] Error loading projects: {ex.Message}");
            }
        }

        public void ApplyFilterAndSort()
        {
            string filter = SearchText.Trim().ToLowerInvariant();
            var filtered = string.IsNullOrEmpty(filter)
                ? _allProjects
                : _allProjects.Where(p => p.Name.ToLowerInvariant().Contains(filter) || p.Path.ToLowerInvariant().Contains(filter));

            IEnumerable<Project> sorted;
            if (CurrentSortField == "Date")
            {
                sorted = CurrentSortOrder == "Asc"
                    ? filtered.OrderByDescending(x => x.IsPinned).ThenBy(x => x.CreatedAt)
                    : filtered.OrderByDescending(x => x.IsPinned).ThenByDescending(x => x.CreatedAt);
            }
            else if (CurrentSortField == "Modified")
            {
                Func<Project, DateTime> getModifiedTime = x =>
                {
                    try
                    {
                        if (File.Exists(x.FullBlendPath)) return File.GetLastWriteTime(x.FullBlendPath);
                        if (Directory.Exists(x.Path)) return Directory.GetLastWriteTime(x.Path);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[ProjectViewModel] Error getting modified time: {ex.Message}");
                    }
                    return x.CreatedAt;
                };
                sorted = CurrentSortOrder == "Asc"
                    ? filtered.OrderByDescending(x => x.IsPinned).ThenBy(getModifiedTime)
                    : filtered.OrderByDescending(x => x.IsPinned).ThenByDescending(getModifiedTime);
            }
            else // Name
            {
                Func<Project, string> getName = x => string.IsNullOrEmpty(x.Name) ? "" : x.Name.ToLower();
                sorted = CurrentSortOrder == "Asc"
                    ? filtered.OrderByDescending(x => x.IsPinned).ThenBy(getName)
                    : filtered.OrderByDescending(x => x.IsPinned).ThenByDescending(getName);
            }

            // Update Sort Button Text
            string fieldText = CurrentSortField switch
            {
                "Modified" => _resourceLoader.GetString("ProjectPage_Sort_DateModifiedLabel"),
                "Name" => _resourceLoader.GetString("ProjectPage_Sort_NameLabel"),
                "Date" => _resourceLoader.GetString("ProjectPage_Sort_DateCreatedLabel"),
                _ => _resourceLoader.GetString("ProjectPage_Sort_DateCreatedLabel")
            };

            string orderText = CurrentSortField switch
            {
                "Name" => CurrentSortOrder == "Asc" 
                    ? _resourceLoader.GetString("ProjectPage_Sort_OrderAZ") 
                    : _resourceLoader.GetString("ProjectPage_Sort_OrderZA"),
                _ => CurrentSortOrder == "Asc" 
                    ? _resourceLoader.GetString("ProjectPage_Sort_OrderOldest") 
                    : _resourceLoader.GetString("ProjectPage_Sort_OrderNewest")
            };

            SortButtonText = $"{fieldText} {orderText}";

            bool categorize = AppSettingsService.Instance.Settings.CategorizeProjectsByProgress;

            if (categorize)
            {
                var inProgressList = sorted.Where(p => p.CompletionProgress < 100).ToList();
                var completedList = sorted.Where(p => p.CompletionProgress == 100).ToList();

                var groupedList = new List<ProjectGroup>();
                if (inProgressList.Count > 0)
                {
                    var groupTitle = string.Format(_resourceLoader.GetString("ProjectPage_Group_InProgress"), inProgressList.Count);
                    var inProgressGroup = new ProjectGroup { Key = groupTitle };
                    foreach (var p in inProgressList) inProgressGroup.Add(p);
                    groupedList.Add(inProgressGroup);
                }
                if (completedList.Count > 0)
                {
                    var groupTitle = string.Format(_resourceLoader.GetString("ProjectPage_Group_Completed"), completedList.Count);
                    var completedGroup = new ProjectGroup { Key = groupTitle };
                    foreach (var p in completedList) completedGroup.Add(p);
                    groupedList.Add(completedGroup);
                }

                UpdateGroupedProjects(groupedList);
            }
            else
            {
                UpdateFilteredProjects(sorted.ToList());
            }

            bool hasAnyProjects = _allProjects.Count > 0;
            bool hasFilteredProjects = categorize 
                ? GroupedProjectsCollection.Count > 0 
                : Projects.Count > 0;
            bool isSearching = !string.IsNullOrEmpty(SearchText.Trim());

            IsProjectsListContainerVisible = hasAnyProjects;
            IsProjectsListVisible = !categorize && Projects.Count > 0;
            IsCategorizedProjectsListVisible = categorize && GroupedProjectsCollection.Count > 0;
            IsDropZoneVisible = !hasAnyProjects && !isSearching;
            IsNoSearchResultsVisible = hasAnyProjects && !hasFilteredProjects;
        }

        private void UpdateFilteredProjects(List<Project> sorted)
        {
            if (Projects == null) return;

            // Remove items no longer in sorted list
            for (int i = Projects.Count - 1; i >= 0; i--)
            {
                var item = Projects[i];
                if (!sorted.Any(x => x.Path == item.Path))
                {
                    Projects.RemoveAt(i);
                }
            }

            // Add or move items to match sorted order exactly
            for (int i = 0; i < sorted.Count; i++)
            {
                var targetItem = sorted[i];
                int existingIdx = -1;

                for (int j = i; j < Projects.Count; j++)
                {
                    if (Projects[j].Path == targetItem.Path)
                    {
                        existingIdx = j;
                        break;
                    }
                }

                if (existingIdx == -1)
                {
                    Projects.Insert(i, targetItem);
                }
                else if (existingIdx != i)
                {
                    var itemToMove = Projects[existingIdx];
                    Projects.RemoveAt(existingIdx);
                    Projects.Insert(i, itemToMove);
                }
            }
        }

        private void UpdateGroupedProjects(List<ProjectGroup> targetGroups)
        {
            if (GroupedProjectsCollection == null) return;

            for (int i = GroupedProjectsCollection.Count - 1; i >= 0; i--)
            {
                var existingGroup = GroupedProjectsCollection[i];
                if (!targetGroups.Any(g => g.Key == existingGroup.Key))
                {
                    GroupedProjectsCollection.RemoveAt(i);
                }
            }

            for (int i = 0; i < targetGroups.Count; i++)
            {
                var targetGroup = targetGroups[i];
                var existingGroup = GroupedProjectsCollection.FirstOrDefault(g => g.Key == targetGroup.Key);

                if (existingGroup == null)
                {
                    var newGroup = new ProjectGroup { Key = targetGroup.Key };
                    foreach (var item in targetGroup)
                    {
                        newGroup.Add(item);
                    }
                    GroupedProjectsCollection.Insert(i, newGroup);
                }
                else
                {
                    SyncGroupProjectItems(existingGroup, targetGroup.ToList());

                    int existingIdx = GroupedProjectsCollection.IndexOf(existingGroup);
                    if (existingIdx != i)
                    {
                        GroupedProjectsCollection.RemoveAt(existingIdx);
                        GroupedProjectsCollection.Insert(i, existingGroup);
                    }
                }
            }
        }

        private void SyncGroupProjectItems(ProjectGroup existingGroup, List<Project> targetItems)
        {
            for (int i = existingGroup.Count - 1; i >= 0; i--)
            {
                var item = existingGroup[i];
                if (!targetItems.Any(x => x.Path == item.Path))
                {
                    existingGroup.RemoveAt(i);
                }
            }

            for (int i = 0; i < targetItems.Count; i++)
            {
                var targetItem = targetItems[i];
                int existingIdx = -1;

                for (int j = i; j < existingGroup.Count; j++)
                {
                    if (existingGroup[j].Path == targetItem.Path)
                    {
                        existingIdx = j;
                        break;
                    }
                }

                if (existingIdx == -1)
                {
                    existingGroup.Insert(i, targetItem);
                }
                else if (existingIdx != i)
                {
                    var itemToMove = existingGroup[existingIdx];
                    existingGroup.RemoveAt(existingIdx);
                    existingGroup.Insert(i, itemToMove);
                }
            }
        }

        [RelayCommand]
        public void ClearSearch()
        {
            SearchText = string.Empty;
        }

        [RelayCommand]
        public async Task RefreshProjectsAsync()
        {
            Controls.ProjectCardView.ClearThumbnailCache();
            Projects.Clear();
            if (AppSettingsService.Instance.Settings.AutoDetectBlenderVersion)
            {
                await ProjectService.DetectProjectVersionsAsync(_allProjects);
            }
            LoadProjects();
        }

        [RelayCommand]
        public void SetSortField(string field)
        {
            CurrentSortField = field;
            ApplyFilterAndSort();
        }

        [RelayCommand]
        public void SetSortOrder(string order)
        {
            CurrentSortOrder = order;
            ApplyFilterAndSort();
        }

        public async Task AddProjectAsync(Project project)
        {
            if (project == null || string.IsNullOrEmpty(project.Path)) return;
            if (_allProjects.Any(p => p.Path == project.Path)) return;

            _allProjects.Add(project);
            ProjectService.SaveProjects(_allProjects);
            ApplyFilterAndSort();
        }

        public async Task CreateNewProjectAsync(string projectName, string projectLocation, string fileName, 
            BlenderVersionInfo? selectedVersion, List<string> folders, Dictionary<string, string> launchers, bool autoUpdatePrimaryBlend)
        {
            IsCreating = true;
            CreatingProgressText = string.Format(_resourceLoader.GetString("ProjectPage_CreatingProgress"), projectName);

            try
            {
                string blenderExePath = selectedVersion?.ExecutablePath ?? string.Empty;
                string blenderVersionStr = selectedVersion?.Version ?? "Unknown";

                Project newProject = new Project
                {
                    Name = projectName,
                    Path = Path.Combine(projectLocation, projectName),
                    BlendFileName = fileName,
                    AutoUpdatePrimaryBlend = autoUpdatePrimaryBlend,
                    BlenderVersion = blenderVersionStr,
                    CreatedAt = DateTime.Now,
                    Subfolders = folders,
                    FileLaunchers = launchers
                };

                await Task.Run(() =>
                {
                    if (!Directory.Exists(newProject.Path)) Directory.CreateDirectory(newProject.Path);
                    foreach (var sub in newProject.Subfolders)
                    {
                        string subPath = Path.Combine(newProject.Path, sub);
                        if (!Directory.Exists(subPath)) Directory.CreateDirectory(subPath);
                    }

                    if (!File.Exists(newProject.FullBlendPath))
                    {
                        bool createdProperly = false;
                        if (!string.IsNullOrEmpty(blenderExePath) && File.Exists(blenderExePath))
                        {
                            try
                            {
                                var info = new ProcessStartInfo
                                {
                                    FileName = blenderExePath,
                                    Arguments = $"--background --python-expr \"import bpy; bpy.ops.wm.save_as_mainfile(filepath=r'{newProject.FullBlendPath}')\"",
                                    CreateNoWindow = true,
                                    UseShellExecute = false
                                };
                                using var p = Process.Start(info);
                                if (p != null) createdProperly = p.WaitForExit(10000) && File.Exists(newProject.FullBlendPath);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[ProjectViewModel] Error starting Blender process: {ex.Message}");
                                throw new Exception($"Failed to create the .blend file using the Blender executable. {ex.Message}");
                            }
                        }
                        if (!createdProperly)
                        {
                            throw new Exception("Failed to create the .blend file. The Blender process timed out or the file was not created.");
                        }
                    }
                });

                _allProjects.Add(newProject);
                LastCreatedProject = newProject;
                ProjectService.SaveProjects(_allProjects);
                ApplyFilterAndSort();

                SuccessInfoBarTitle = _resourceLoader.GetString("ProjectPage_Success_CreatedTitle");
                SuccessInfoBarMessage = string.Format(_resourceLoader.GetString("ProjectPage_Success_CreatedMessage"), newProject.Name, newProject.Path);
                IsSuccessInfoBarOpen = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProjectViewModel] Error creating project: {ex}");
                throw;
            }
            finally
            {
                IsCreating = false;
            }
        }
    }

    public class ProjectGroup : ObservableCollection<Project>
    {
        public string Key { get; set; } = string.Empty;
    }
#pragma warning restore MVVMTK0045
}

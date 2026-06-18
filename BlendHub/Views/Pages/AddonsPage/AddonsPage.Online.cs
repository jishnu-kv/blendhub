using BlendHub.Models;
using BlendHub.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace BlendHub.Pages
{
    public sealed partial class AddonsPage : Page
    {
        // --- Online Extensions Sync & Helper Logic ---

        private async Task LoadOnlineExtensionsAsync()
        {
            if (BackgroundUpdatePanel == null) return;

            // Step 1: Load local cache immediately for instantaneous rendering
            string localJson = string.Empty;
            string localPath = string.Empty;
            try
            {
                localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "extensions.json");
                if (!File.Exists(localPath))
                {
                    localPath = Path.Combine(Directory.GetCurrentDirectory(), "src", "extensions.json");
                }

                if (File.Exists(localPath))
                {
                    localJson = await File.ReadAllTextAsync(localPath);
                    _lastSyncedTime = File.GetLastWriteTime(localPath);
                    UpdateRefreshButtonToolTip();
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[AddonsPage] Failed to load local extensions cache: {ex.Message}");
            }

            if (!string.IsNullOrEmpty(localJson))
            {
                try
                {
                    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var response = System.Text.Json.JsonSerializer.Deserialize<OnlineApiResponse>(localJson, options);
                    
                    if (response?.Data != null)
                    {
                        var mapped = response.Data.Select(MapToAddonItem).ToList();
                        _onlineAddons.Clear();
                        _onlineAddons.AddRange(mapped
                            .GroupBy(a => new { a.Name, a.Version })
                            .Select(g => g.First())
                        );
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[AddonsPage] Failed to parse local extensions cache: {ex.Message}");
                }
            }

            // Immediately apply filters so the page renders instantly!
            ApplyFilters();

            // Step 2: Asynchronously sync with the live API in the background
            if (_onlineAddons.Count == 0)
            {
                BackgroundUpdatePanel.Visibility = Visibility.Visible;
            }

            try
            {
                _httpClient.Timeout = TimeSpan.FromSeconds(12);
                string liveJson = await _httpClient.GetStringAsync("https://extensions.blender.org/api/v1/extensions/?format=json");

                if (!string.IsNullOrEmpty(liveJson) && liveJson != localJson)
                {
                    var options = new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var response = System.Text.Json.JsonSerializer.Deserialize<OnlineApiResponse>(liveJson, options);

                    if (response?.Data != null)
                    {
                        var mapped = response.Data.Select(MapToAddonItem).ToList();
                        _onlineAddons.Clear();
                        _onlineAddons.AddRange(mapped
                            .GroupBy(a => new { a.Name, a.Version })
                            .Select(g => g.First())
                        );

                        // Re-apply filters to incrementally merge live updates!
                        ApplyFilters();
                    }

                    // Save the new online extensions to local cache file
                    try
                    {
                        if (!string.IsNullOrEmpty(localPath))
                        {
                            var parentDir = Path.GetDirectoryName(localPath);
                            if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
                            {
                                Directory.CreateDirectory(parentDir);
                            }
                            await File.WriteAllTextAsync(localPath, liveJson);
                        }
                    }
                    catch (Exception writeEx)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AddonsPage] Failed to write live extensions to cache: {writeEx.Message}");
                    }
                }
                _lastSyncedTime = DateTime.Now;
                UpdateRefreshButtonToolTip();
            }
            catch (Exception downloadEx)
            {
                System.Diagnostics.Debug.WriteLine($"[AddonsPage] Live background extensions sync failed: {downloadEx.Message}");
            }
            finally
            {
                BackgroundUpdatePanel.Visibility = Visibility.Collapsed;
            }
        }



        private AddonItem MapToAddonItem(OnlineExtData ext)
        {
            var item = new AddonItem
            {
                Name = ext.Name,
                FolderName = ext.Id,
                Version = ext.Version,
                Author = string.IsNullOrEmpty(ext.Maintainer)
                    ? "Unknown"
                    : (ext.Maintainer.IndexOf('<') >= 0
                        ? ext.Maintainer.Substring(0, ext.Maintainer.IndexOf('<')).Trim()
                        : ext.Maintainer),
                Description = ext.Tagline,
                Category = ext.Tags != null && ext.Tags.Count > 0 ? string.Join(", ", ext.Tags) : "General",
                Type = "Extension",
                ExtensionType = !string.IsNullOrEmpty(ext.Type) ? ext.Type.ToLowerInvariant() : "add-on",
                Repository = "extensions.blender.org",
                BlenderVersion = ext.BlenderVersionMin,
                Path = ext.ArchiveUrl, // Use URL as Path to flag as online
                WebsiteUrl = ext.Website,
                BlenderVersionMin = ext.BlenderVersionMin,
                License = ext.License != null ? string.Join(", ", ext.License) : string.Empty,
                ArchiveSize = ext.ArchiveSize
            };

            if (ext.Permissions != null && ext.Permissions.Count > 0)
            {
                var perms = ext.Permissions.Select(kv => $"{char.ToUpper(kv.Key[0]) + kv.Key.Substring(1)}: {kv.Value}");
                item.Permissions = string.Join(", ", perms);
            }

            return item;
        }

        private static string FormatBytes(long bytes)
        {
            string[] suffix = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int order = 0;
            while (size >= 1024 && order < suffix.Length - 1)
            {
                order++;
                size = size / 1024;
            }
            return $"{size:0.#} {suffix[order]}";
        }
    }

    public class OnlineApiResponse
    {
        [JsonPropertyName("data")]
        public List<OnlineExtData> Data { get; set; } = new();
    }

    public class OnlineExtData
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("version")]
        public string Version { get; set; } = string.Empty;

        [JsonPropertyName("tagline")]
        public string Tagline { get; set; } = string.Empty;

        [JsonPropertyName("archive_url")]
        public string ArchiveUrl { get; set; } = string.Empty;

        [JsonPropertyName("type")]
        public string Type { get; set; } = string.Empty;

        [JsonPropertyName("blender_version_min")]
        public string BlenderVersionMin { get; set; } = string.Empty;

        [JsonPropertyName("website")]
        public string Website { get; set; } = string.Empty;

        [JsonPropertyName("maintainer")]
        public string Maintainer { get; set; } = string.Empty;

        [JsonPropertyName("license")]
        public List<string> License { get; set; } = new();

        [JsonPropertyName("tags")]
        public List<string> Tags { get; set; } = new();

        [JsonPropertyName("permissions")]
        public Dictionary<string, string>? Permissions { get; set; }

        [JsonPropertyName("archive_size")]
        public long ArchiveSize { get; set; }
    }
}

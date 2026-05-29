using BlendHub.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace BlendHub.Helpers
{
    public static class WebVersionDatabaseHelper
    {
        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All })
        {
            Timeout = TimeSpan.FromSeconds(15)
        };
        private const string BaseUrl = "https://download.blender.org/release/";
        private const string JsonFileName = "blender_versions.json";

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        private static string GetAppDirectory()
        {
            try
            {
                if (Package.Current != null)
                    return Package.Current.InstalledLocation.Path;
            }
            catch
            {
                // Package.Current throws in unpackaged mode
            }
            return AppContext.BaseDirectory;
        }

        private static string GetRoamingJsonPath()
        {
            var roamingPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var blendHubPath = Path.Combine(roamingPath, "BlendHub");

            if (!Directory.Exists(blendHubPath))
                Directory.CreateDirectory(blendHubPath);

            return Path.Combine(blendHubPath, JsonFileName);
        }

        /// <summary>
        /// Initialize by copying bundled JSON from app package to roaming folder on first run.
        /// </summary>
        public static async Task InitializeDatabaseAsync()
        {
            try
            {
                var roamingJsonPath = GetRoamingJsonPath();

                Debug.WriteLine($"[WebDB] InitializeDatabaseAsync called");
                Debug.WriteLine($"[WebDB] Roaming path: {roamingJsonPath}");
                Debug.WriteLine($"[WebDB] Roaming file exists: {File.Exists(roamingJsonPath)}");

                if (!File.Exists(roamingJsonPath))
                {
                    Debug.WriteLine("[WebDB] JSON not found in roaming folder, copying from app package...");

                    var appJsonPath = Path.Combine(GetAppDirectory(), JsonFileName);

                    Debug.WriteLine($"[WebDB] Looking for source at: {appJsonPath}");
                    Debug.WriteLine($"[WebDB] Source file exists: {File.Exists(appJsonPath)}");

                    if (File.Exists(appJsonPath))
                    {
                        File.Copy(appJsonPath, roamingJsonPath, overwrite: true);
                        Debug.WriteLine($"[WebDB] Copied JSON to: {roamingJsonPath}");
                    }
                    else
                    {
                        Debug.WriteLine("[WebDB] Source JSON not found — creating empty file.");
                        await File.WriteAllTextAsync(roamingJsonPath, "[]");
                    }
                }
                else
                {
                    Debug.WriteLine($"[WebDB] JSON already exists at: {roamingJsonPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebDB] Error initializing: {ex.Message}");
                Debug.WriteLine($"[WebDB] Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        /// <summary>
        /// Load all versions from the roaming JSON file.
        /// Returns a dictionary keyed by short version (e.g. "4.2").
        /// </summary>
        public static async Task<Dictionary<string, BlenderVersionJsonInfo>> GetAllVersionsAsync()
        {
            var result = new Dictionary<string, BlenderVersionJsonInfo>();
            var jsonPath = GetRoamingJsonPath();

            if (!File.Exists(jsonPath))
            {
                Debug.WriteLine("[WebDB] JSON file not found, returning empty result.");
                return result;
            }

            try
            {
                var json = await File.ReadAllTextAsync(jsonPath);
                var list = JsonSerializer.Deserialize<List<BlenderVersionJsonInfo>>(json, _jsonOptions);

                if (list != null)
                {
                    foreach (var item in list)
                    {
                        var key = VersionHelper.GetShortVersion(item.Version);
                        if (!string.IsNullOrEmpty(key) && !result.ContainsKey(key))
                            result[key] = item;
                    }
                }

                Debug.WriteLine($"[WebDB] Loaded {result.Count} versions from JSON.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebDB] Error reading JSON: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Refresh by scraping the Blender release website and saving results to JSON.
        /// </summary>
        public static async Task RefreshDatabaseAsync()
        {
            try
            {
                Debug.WriteLine($"[WebDB] Starting web scrape from {BaseUrl}");

                var html = await _httpClient.GetStringAsync(BaseUrl);
                var versionDirs = ParseDirectoryListing(html, BaseUrl)
                    .Where(e => e.Name.StartsWith("Blender", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => VersionHelper.ParseVersion(
                        e.Name.StartsWith("Blender", StringComparison.OrdinalIgnoreCase) ? e.Name.Substring(7) : e.Name))
                    .ToList();

                Debug.WriteLine($"[WebDB] Found {versionDirs.Count} version directories. Scraping most recent...");

                // Load existing data to merge with
                var existing = await GetAllVersionsAsync();

                // Scan top 15 newest versions
                var versionsToScan = versionDirs.Take(15).ToList();

                foreach (var dir in versionsToScan)
                {
                    var dirname = dir.Name;
                    var versionStr = dirname.StartsWith("Blender", StringComparison.OrdinalIgnoreCase)
                        ? dirname.Substring(7)
                        : dirname;
                    var shortVersion = VersionHelper.GetShortVersion(versionStr);

                    Debug.WriteLine($"[WebDB] Scraping: {dirname}...");

                    try
                    {
                        var subHtml = await _httpClient.GetStringAsync(dir.Url);
                        var files = ParseDirectoryListing(subHtml, dir.Url);
                        var windowsFiles = files.Where(f => IsWindowsInstaller(f.Name)).ToList();

                        if (windowsFiles.Any())
                        {
                            var installers = windowsFiles.Select(f => new WindowsInstaller
                            {
                                Filename = f.Name,
                                Url = f.Url,
                                ReleaseDate = f.Date,
                                SizeBytes = f.SizeBytes
                            }).ToList();

                            existing[shortVersion] = new BlenderVersionJsonInfo
                            {
                                Version = versionStr,
                                WindowsInstallers = installers
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[WebDB] Failed to scrape {dirname}: {ex.Message}");
                    }
                }

                // Save back to roaming JSON
                await SaveJsonAsync(existing.Values.OrderByDescending(v => VersionHelper.ParseVersion(v.Version)).ToList());

                Debug.WriteLine("[WebDB] Web refresh complete.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebDB] Error refreshing from web: {ex.Message}");
                throw;
            }
        }

        private static async Task SaveJsonAsync(IEnumerable<BlenderVersionJsonInfo> versions)
        {
            var jsonPath = GetRoamingJsonPath();
            var json = JsonSerializer.Serialize(versions.ToList(), _jsonOptions);
            await File.WriteAllTextAsync(jsonPath, json);
            Debug.WriteLine($"[WebDB] Saved {versions.Count()} versions to {jsonPath}");
        }

        private static List<(string Name, string Url, string Date, long SizeBytes)> ParseDirectoryListing(string html, string baseUrl)
        {
            var results = new List<(string Name, string Url, string Date, long SizeBytes)>();

            var matches = Regex.Matches(html, @"<a href=""([^""]+)"">[^<]+</a>\s+(\d{2}-\w{3}-\d{4}\s+\d{2}:\d{2})\s+([\d-]+|-)");

            foreach (Match m in matches)
            {
                var href = m.Groups[1].Value;
                if (href == "../" || href == "/") continue;

                var name = href.TrimEnd('/');
                var date = m.Groups[2].Value;
                var sizeStr = m.Groups[3].Value;
                long size = 0;
                if (long.TryParse(sizeStr, out var s)) size = s;

                results.Add((name, baseUrl + href, date, size));
            }

            return results;
        }

        private static bool IsWindowsInstaller(string filename)
        {
            var lower = filename.ToLower();
            bool hasKeyword = Regex.IsMatch(lower, @"windows|_win");
            string ext = Path.GetExtension(lower);
            var windowsExts = new[] { ".exe", ".msi", ".msix", ".zip" };
            return hasKeyword && windowsExts.Contains(ext);
        }

        /// <summary>
        /// Get JSON path for diagnostics.
        /// </summary>
        public static string GetDatabasePath() => GetRoamingJsonPath();
    }
}

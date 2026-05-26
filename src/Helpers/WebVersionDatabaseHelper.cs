using BlendHub.Models;
using Microsoft.Data.Sqlite;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace BlendHub.Helpers
{
    public static class WebVersionDatabaseHelper
    {
        private static readonly HttpClient _httpClient = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
        private const string BaseUrl = "https://download.blender.org/release/";

        private static string GetAppDirectory()
        {
            try
            {
                // For packaged apps (MSIX), use InstalledLocation
                if (Package.Current != null)
                {
                    return Package.Current.InstalledLocation.Path;
                }
            }
            catch
            {
                // Package.Current throws in unpackaged mode
            }
            // Fallback to base directory for unpackaged apps
            return AppContext.BaseDirectory;
        }

        private static string GetRoamingDatabasePath()
        {
            var roamingPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var blendHubPath = Path.Combine(roamingPath, "BlendHub");

            if (!Directory.Exists(blendHubPath))
            {
                Directory.CreateDirectory(blendHubPath);
            }

            return Path.Combine(blendHubPath, "blender_versions_web.db");
        }

        private static string GetConnectionString()
        {
            var path = GetRoamingDatabasePath();
            return $"Data Source={path};Cache=Shared;Pooling=false;";
        }

        /// <summary>
        /// Initialize database by copying from app package to roaming folder on first run
        /// </summary>
        public static async Task InitializeDatabaseAsync()
        {
            try
            {
                var roamingDbPath = GetRoamingDatabasePath();

                Debug.WriteLine($"[WebDB] InitializeDatabaseAsync called");
                Debug.WriteLine($"[WebDB] Roaming path: {roamingDbPath}");
                Debug.WriteLine($"[WebDB] Roaming file exists: {File.Exists(roamingDbPath)}");

                // Check if database already exists in roaming folder
                if (!File.Exists(roamingDbPath))
                {
                    Debug.WriteLine("[WebDB] Database not found in roaming folder, copying from app package...");

                    // Copy from app installation directory
                    var appDbPath = Path.Combine(GetAppDirectory(), "blender_versions_web.db");

                    Debug.WriteLine($"[WebDB] Looking for source at: {appDbPath}");
                    Debug.WriteLine($"[WebDB] Source file exists: {File.Exists(appDbPath)}");

                    if (File.Exists(appDbPath))
                    {
                        File.Copy(appDbPath, roamingDbPath, true);
                        Debug.WriteLine($"[WebDB] Copied database to: {roamingDbPath}");
                    }
                    else
                    {
                        Debug.WriteLine($"[WebDB] Source database not found at: {appDbPath}");
                        // Create empty database with schema
                        await CreateEmptyDatabaseAsync();
                    }
                }
                else
                {
                    Debug.WriteLine($"[WebDB] Database already exists at: {roamingDbPath}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebDB] Error initializing database: {ex.Message}");
                Debug.WriteLine($"[WebDB] Stack trace: {ex.StackTrace}");
                throw;
            }
        }

        private static async Task CreateEmptyDatabaseAsync()
        {
            using (var connection = new SqliteConnection(GetConnectionString()))
            {
                await connection.OpenAsync();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        CREATE TABLE IF NOT EXISTS Versions (
                            Id TEXT PRIMARY KEY,
                            Version TEXT NOT NULL,
                            Directory TEXT NOT NULL,
                            LastUpdated TEXT NOT NULL
                        );

                        CREATE TABLE IF NOT EXISTS Installers (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            VersionId TEXT NOT NULL,
                            Filename TEXT NOT NULL,
                            Url TEXT NOT NULL,
                            ReleaseDate TEXT NOT NULL,
                            SizeBytes INTEGER NOT NULL,
                            FOREIGN KEY(VersionId) REFERENCES Versions(Id)
                        );

                        CREATE INDEX IF NOT EXISTS idx_version_id ON Installers(VersionId);
                    ";
                    await command.ExecuteNonQueryAsync();
                }
            }

            Debug.WriteLine("[WebDB] Created empty database with schema");
        }

        /// <summary>
        /// Get all versions from database
        /// </summary>
        public static async Task<Dictionary<string, BlenderVersionJsonInfo>> GetAllVersionsAsync()
        {
            var result = new Dictionary<string, BlenderVersionJsonInfo>();
            var roamingDbPath = GetRoamingDatabasePath();

            // Don't try to open if file doesn't exist - SQLite would create empty file
            if (!File.Exists(roamingDbPath))
            {
                Debug.WriteLine("[WebDB] Database file doesn't exist, returning empty result");
                return result;
            }

            try
            {
                using (var connection = new SqliteConnection(GetConnectionString()))
                {
                    await connection.OpenAsync();

                    // Step 1: Load all versions
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT Id, Version FROM Versions ORDER BY Id DESC";
                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var versionId = reader.GetString(0);
                                var version = reader.GetString(1);

                                result[versionId] = new BlenderVersionJsonInfo
                                {
                                    Version = version,
                                    WindowsInstallers = new List<WindowsInstaller>()
                                };
                            }
                        }
                    }

                    // Step 2: Load all installers in one query and group them in memory
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = @"
                            SELECT VersionId, Filename, Url, ReleaseDate, SizeBytes 
                            FROM Installers";

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                var versionId = reader.GetString(0);
                                if (result.TryGetValue(versionId, out var versionInfo))
                                {
                                    versionInfo.WindowsInstallers.Add(new WindowsInstaller
                                    {
                                        Filename = reader.GetString(1),
                                        Url = reader.GetString(2),
                                        ReleaseDate = reader.IsDBNull(3) ? "Unknown" : reader.GetString(3),
                                        SizeBytes = reader.GetInt64(4)
                                    });
                                }
                            }
                        }
                    }
                }

                Debug.WriteLine($"[WebDB] Retrieved {result.Count} versions from database");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebDB] Error retrieving versions: {ex.Message}");
            }

            return result;
        }

        /// <summary>
        /// Refresh database by scraping the Blender release website
        /// </summary>
        public static async Task RefreshDatabaseAsync()
        {
            try
            {
                Debug.WriteLine($"[WebDB] Starting web scrape from {BaseUrl}");

                var html = await _httpClient.GetStringAsync(BaseUrl);
                var versionDirs = ParseDirectoryListing(html, BaseUrl)
                    .Where(e => e.Name.StartsWith("Blender", StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(e => VersionHelper.ParseVersion(e.Name.StartsWith("Blender", StringComparison.OrdinalIgnoreCase) ? e.Name.Substring(7) : e.Name))
                    .ToList();

                Debug.WriteLine($"[WebDB] Found {versionDirs.Count} version directories. Scraping most recent versions...");

                using (var connection = new SqliteConnection(GetConnectionString()))
                {
                    await connection.OpenAsync();

                    // Limit to top 20 newest versions for speed, or scan all if needed.
                    // Let's do top 15 for a good balance of speed and coverage.
                    var versionsToScan = versionDirs.Take(15).ToList();

                    foreach (var dir in versionsToScan)
                    {
                        var dirname = dir.Name;
                        var versionStr = dirname.StartsWith("Blender", StringComparison.OrdinalIgnoreCase) ? dirname.Substring(7) : dirname;
                        var versionId = VersionHelper.GetShortVersion(versionStr);

                        Debug.WriteLine($"[WebDB] Scraping version directory: {dirname}...");

                        try
                        {
                            var subHtml = await _httpClient.GetStringAsync(dir.Url);
                            var files = ParseDirectoryListing(subHtml, dir.Url);
                            var windowsFiles = files.Where(f => IsWindowsInstaller(f.Name)).ToList();

                            if (windowsFiles.Any())
                            {
                                await UpsertVersionAsync(connection, versionId, versionStr, dirname);
                                foreach (var f in windowsFiles)
                                {
                                    await InsertInstallerAsync(connection, versionId, f.Name, f.Url, f.Date, f.SizeBytes);
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"[WebDB] Failed to scrape {dirname}: {ex.Message}");
                        }
                    }
                }

                Debug.WriteLine("[WebDB] Web refresh complete");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[WebDB] Error refreshing from web: {ex.Message}");
                throw;
            }
        }

        private static List<(string Name, string Url, string Date, long SizeBytes)> ParseDirectoryListing(string html, string baseUrl)
        {
            var results = new List<(string Name, string Url, string Date, long SizeBytes)>();

            // Regex to match Apache directory listing entries
            // Pattern: <a href="filename">filename</a>   DD-Mon-YYYY HH:MM   SIZE
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

        private static async Task UpsertVersionAsync(SqliteConnection conn, string id, string version, string directory)
        {
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = @"
                    INSERT OR REPLACE INTO Versions (Id, Version, Directory, LastUpdated)
                    VALUES (@id, @version, @dir, @now)";
                cmd.Parameters.AddWithValue("@id", id);
                cmd.Parameters.AddWithValue("@version", version);
                cmd.Parameters.AddWithValue("@dir", directory);
                cmd.Parameters.AddWithValue("@now", DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss"));
                await cmd.ExecuteNonQueryAsync();
            }
        }

        private static async Task InsertInstallerAsync(SqliteConnection conn, string versionId, string filename, string url, string date, long size)
        {
            using (var cmd = conn.CreateCommand())
            {
                // Check if already exists to avoid duplicates
                cmd.CommandText = "SELECT COUNT(*) FROM Installers WHERE VersionId = @vId AND Filename = @file";
                cmd.Parameters.AddWithValue("@vId", versionId);
                cmd.Parameters.AddWithValue("@file", filename);
                var countObj = await cmd.ExecuteScalarAsync();
                long count = countObj != null ? (long)countObj : 0;

                if (count == 0)
                {
                    cmd.CommandText = @"
                        INSERT INTO Installers (VersionId, Filename, Url, ReleaseDate, SizeBytes)
                        VALUES (@vId, @file, @url, @date, @size)";
                    cmd.Parameters.AddWithValue("@url", url);
                    cmd.Parameters.AddWithValue("@date", date);
                    cmd.Parameters.AddWithValue("@size", size);
                    await cmd.ExecuteNonQueryAsync();
                }
            }
        }

        /// <summary>
        /// Get database path for diagnostics
        /// </summary>
        public static string GetDatabasePath() => GetRoamingDatabasePath();
    }
}

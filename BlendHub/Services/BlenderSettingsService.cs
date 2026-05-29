using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace BlendHub.Services
{
    public class BlenderVersionInfo
    {
        public string Version { get; set; } = string.Empty;
        public string ConfigPath { get; set; } = string.Empty;
        public string ExecutablePath { get; set; } = string.Empty;
        public string DisplayName => $"Blender {Version}";
        public bool IsUpdateAvailable { get; set; }
        public bool ShowDivider { get; set; } = true;
    }

    public class BackupItem
    {
        public string Name { get; set; } = string.Empty;
        public bool IsEnabled { get; set; } = true;
        public string RelativePath { get; set; } = string.Empty;
        public bool IsFolder { get; set; }
        public bool Exists { get; set; } = true;
        public string Category { get; set; } = string.Empty;
        public string Tooltip { get; set; } = string.Empty;
    }

    public class BlenderSettingsService
    {
        private static readonly string[] IgnoredFolders = { "__pycache__", ".cache", ".local", ".git", ".svn", ".idea", ".vscode", "sync_blender" };
        private static readonly string[] IgnoredExtensions = { ".pyc", ".pyo", ".log", ".tmp" };

        public string GetBlenderRootPath()
        {
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Blender Foundation", "Blender");
        }

        public List<BlenderVersionInfo> GetInstalledVersions()
        {
            var versions = new List<BlenderVersionInfo>();
            var configRoot = GetBlenderRootPath();
            var discoveredVersions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // version -> executablePath

            var searchPaths = new[]
            {
                @"C:\Program Files\Blender Foundation",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Steam\steamapps\common\Blender"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\WindowsApps"),
                @"C:\Program Files\WindowsApps"
            };

            // Phase 1: Discover versions from installation directories
            foreach (var root in searchPaths)
            {
                if (!Directory.Exists(root)) continue;

                try
                {
                    if (root.EndsWith("Blender Foundation", StringComparison.OrdinalIgnoreCase))
                    {
                        // Standard installation: "C:\Program Files\Blender Foundation\Blender 4.2\blender.exe"
                        foreach (var dir in Directory.GetDirectories(root))
                        {
                            try
                            {
                                var dirName = Path.GetFileName(dir);
                                var versionStr = ExtractVersionFromName(dirName);
                                if (string.IsNullOrEmpty(versionStr)) continue;

                                var exe = Path.Combine(dir, "blender.exe");
                                if (!File.Exists(exe)) exe = Path.Combine(dir, "blender-launcher.exe");
                                if (File.Exists(exe) && !discoveredVersions.ContainsKey(versionStr))
                                {
                                    discoveredVersions[versionStr] = exe;
                                }
                            }
                            catch { }
                        }
                    }
                    else if (root.Contains("Steam", StringComparison.OrdinalIgnoreCase))
                    {
                        // Steam installation
                        var exe = Path.Combine(root, "blender.exe");
                        if (!File.Exists(exe)) exe = Path.Combine(root, "blender-launcher.exe");
                        if (File.Exists(exe))
                        {
                            try
                            {
                                var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(exe);
                                var ver = vi.ProductVersion ?? vi.FileVersion;
                                var versionStr = ExtractMajorMinor(ver);
                                if (!string.IsNullOrEmpty(versionStr) && !discoveredVersions.ContainsKey(versionStr))
                                {
                                    discoveredVersions[versionStr] = exe;
                                }
                            }
                            catch { }
                        }
                    }
                    else if (root.Contains("WindowsApps"))
                    {
                        // Microsoft Store: use PackageManager API to discover installed Blender packages
                        // This bypasses the ACL restriction on C:\Program Files\WindowsApps
                        try
                        {
                            var packageManager = new Windows.Management.Deployment.PackageManager();
                            var packages = packageManager.FindPackagesForUser("");
                            foreach (var pkg in packages)
                            {
                                try
                                {
                                    if (!pkg.Id.Name.Contains("Blender", StringComparison.OrdinalIgnoreCase)) continue;

                                    var installPath = pkg.InstalledPath;
                                    if (string.IsNullOrEmpty(installPath)) continue;

                                    // Extract version from package version (e.g. 5.1.2.0 -> "5.1")
                                    var pkgVersion = pkg.Id.Version;
                                    var versionStr = $"{pkgVersion.Major}.{pkgVersion.Minor}";

                                    // Blender exe is inside a "Blender" subdirectory within the package
                                    var exe = Path.Combine(installPath, "Blender", "blender.exe");
                                    if (!File.Exists(exe)) exe = Path.Combine(installPath, "Blender", "blender-launcher.exe");
                                    // Also check root of package dir
                                    if (!File.Exists(exe)) exe = Path.Combine(installPath, "blender.exe");
                                    if (!File.Exists(exe)) exe = Path.Combine(installPath, "blender-launcher.exe");

                                    if (File.Exists(exe) && !discoveredVersions.ContainsKey(versionStr))
                                    {
                                        discoveredVersions[versionStr] = exe;
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { /* PackageManager API not available or failed */ }
                    }
                }
                catch { /* Ignore scan errors */ }
            }

            // Phase 2: Ensure configRoot exists and pre-create config folders for discovered versions
            if (!Directory.Exists(configRoot))
            {
                try { Directory.CreateDirectory(configRoot); } catch { }
            }

            foreach (var kvp in discoveredVersions)
            {
                var configPath = Path.Combine(configRoot, kvp.Key);
                if (!Directory.Exists(configPath))
                {
                    try
                    {
                        Directory.CreateDirectory(configPath);
                        Directory.CreateDirectory(Path.Combine(configPath, "config"));
                    }
                    catch { }
                }
            }

            // Phase 3: Build versions list from AppData config folders (now enriched by Phase 2)
            if (Directory.Exists(configRoot))
            {
                foreach (var dir in Directory.GetDirectories(configRoot))
                {
                    var version = Path.GetFileName(dir);
                    if (version.Length == 0 || !char.IsDigit(version[0])) continue;

                    var info = new BlenderVersionInfo
                    {
                        Version = version,
                        ConfigPath = dir
                    };

                    // Use discovered executable if available
                    if (discoveredVersions.TryGetValue(version, out var discoveredExe))
                    {
                        info.ExecutablePath = discoveredExe;
                    }
                    else
                    {
                        // Fallback: search for executable across common paths
                        foreach (var root in searchPaths)
                        {
                            if (!Directory.Exists(root)) continue;

                            try
                            {
                                if (root.Contains("WindowsApps"))
                                {
                                    // Use PackageManager API to find Store-installed Blender matching this version
                                    try
                                    {
                                        var pm = new Windows.Management.Deployment.PackageManager();
                                        foreach (var pkg in pm.FindPackagesForUser(""))
                                        {
                                            try
                                            {
                                                if (!pkg.Id.Name.Contains("Blender", StringComparison.OrdinalIgnoreCase)) continue;
                                                var pkgVer = $"{pkg.Id.Version.Major}.{pkg.Id.Version.Minor}";
                                                if (!pkgVer.Equals(version, StringComparison.OrdinalIgnoreCase)) continue;

                                                var installPath = pkg.InstalledPath;
                                                if (string.IsNullOrEmpty(installPath)) continue;

                                                var exe = Path.Combine(installPath, "Blender", "blender.exe");
                                                if (!File.Exists(exe)) exe = Path.Combine(installPath, "Blender", "blender-launcher.exe");
                                                if (!File.Exists(exe)) exe = Path.Combine(installPath, "blender.exe");
                                                if (!File.Exists(exe)) exe = Path.Combine(installPath, "blender-launcher.exe");
                                                if (File.Exists(exe)) { info.ExecutablePath = exe; break; }
                                            }
                                            catch { }
                                        }
                                    }
                                    catch { }
                                    if (!string.IsNullOrEmpty(info.ExecutablePath)) break;
                                }
                                else
                                {
                                    var installDir = Directory.GetDirectories(root)
                                        .FirstOrDefault(d => d.Contains(version) || d.EndsWith("Blender"));

                                    if (installDir != null)
                                    {
                                        var exe = Path.Combine(installDir, "blender.exe");
                                        if (!File.Exists(exe)) exe = Path.Combine(installDir, "blender-launcher.exe");
                                        if (File.Exists(exe)) { info.ExecutablePath = exe; break; }
                                    }
                                }
                            }
                            catch { }
                        }
                    }

                    versions.Add(info);
                }
            }




            // Add custom blender paths from settings if they exist and are not already in the list
            var customPaths = AppSettingsService.Instance.Settings.CustomBlenderPaths;
            if (customPaths != null)
            {
                foreach (var customPath in customPaths)
                {
                    if (!string.IsNullOrEmpty(customPath) && File.Exists(customPath))
                    {
                        if (!versions.Any(v => v.ExecutablePath.Equals(customPath, StringComparison.OrdinalIgnoreCase)))
                        {
                            versions.Add(new BlenderVersionInfo
                            {
                                Version = "Custom",
                                ExecutablePath = customPath,
                                ConfigPath = string.Empty // Custom paths might not have a config folder we know about
                            });
                        }
                    }
                }
            }

            var sortedVersions = versions.OrderByDescending(v => v.Version).ToList();
            for (int i = 0; i < sortedVersions.Count; i++)
            {
                sortedVersions[i].ShowDivider = (i < sortedVersions.Count - 1);
            }
            return sortedVersions;
        }

        public void LaunchBlender(string exePath)
        {
            if (File.Exists(exePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exePath,
                    UseShellExecute = true
                });
            }
        }

        public void OpenConfigFolder(string configPath)
        {
            if (Directory.Exists(configPath))
            {
                System.Diagnostics.Process.Start("explorer.exe", configPath);
            }
        }

        public List<BackupItem> GetDefaultBackupItems(string versionPath)
        {
            var items = new List<BackupItem>
            {
                new BackupItem { Name = "Addons", RelativePath = "scripts/addons", IsFolder = true, Category = "Extensions & Tools" },
                new BackupItem { Name = "Extensions", RelativePath = "scripts/extensions", IsFolder = true, Category = "Extensions & Tools" },
                new BackupItem { Name = "Presets", RelativePath = "scripts/presets", IsFolder = true, Category = "Extensions & Tools" },

                new BackupItem { Name = "Preferences", RelativePath = "config/userpref.blend", IsFolder = false, Category = "Preferences & Configuration" },
                new BackupItem { Name = "Startup File", RelativePath = "config/startup.blend", IsFolder = false, Category = "Preferences & Configuration" },
                new BackupItem { Name = "Platform Support", RelativePath = "config/platform_support.txt", IsFolder = false, Category = "Preferences & Configuration" },

                new BackupItem { Name = "Recent Files", RelativePath = "config/recent-files.txt", IsFolder = false, Category = "History & Recent Data" },
                new BackupItem { Name = "Recent Searches", RelativePath = "config/recent-searches.txt", IsFolder = false, Category = "History & Recent Data" },
                new BackupItem { Name = "Bookmarks", RelativePath = "config/bookmarks.txt", IsFolder = false, Category = "History & Recent Data" }
            };

            // Set existence state and filter extensions path if it doesn't exist in scripts
            foreach (var item in items)
            {
                // Special check for extensions: might be in root or in scripts/extensions
                if (item.Name == "Extensions")
                {
                    var scriptsExtensions = Path.Combine(versionPath, "scripts/extensions");
                    var rootExtensions = Path.Combine(versionPath, "extensions");

                    if (Directory.Exists(scriptsExtensions))
                    {
                        item.RelativePath = "scripts/extensions";
                        item.Exists = true;
                    }
                    else if (Directory.Exists(rootExtensions))
                    {
                        item.RelativePath = "extensions";
                        item.Exists = true;
                    }
                    else
                    {
                        item.Exists = false;
                    }
                }
                else
                {
                    var fullPath = Path.Combine(versionPath, item.RelativePath);
                    item.Exists = item.IsFolder ? Directory.Exists(fullPath) : File.Exists(fullPath);
                }

                // Default IsEnabled to Exists
                item.IsEnabled = item.Exists;

                // Set Tooltip
                if (item.Exists)
                {
                    item.Tooltip = $"Include {item.Name} ({(item.IsFolder ? "folder" : "file")})";
                }
                else
                {
                    string type = item.IsFolder ? "Folder" : "File";
                    item.Tooltip = $"{type} not found at {item.RelativePath}";
                }
            }

            return items;
        }

        public async Task BackupAsync(string versionPath, string destinationPath, string backupName, List<BackupItem> items, Action<string, double>? onProgress = null)
        {
            var versionName = Path.GetFileName(versionPath);
            var fullBackupName = $"{backupName}_{versionName}";
            var backupRoot = Path.Combine(destinationPath, fullBackupName);

            if (!Directory.Exists(backupRoot))
                Directory.CreateDirectory(backupRoot);

            int totalItems = items.Count;
            int currentItem = 0;

            foreach (var item in items.Where(i => i.IsEnabled))
            {
                var src = Path.Combine(versionPath, item.RelativePath);
                var dst = Path.Combine(backupRoot, item.RelativePath);

                onProgress?.Invoke($"Backing up {item.Name}...", (double)currentItem / totalItems);

                if (item.IsFolder)
                {
                    await CopyDirectoryAsync(src, dst);
                }
                else
                {
                    var dstDir = Path.GetDirectoryName(dst);
                    if (dstDir != null && !Directory.Exists(dstDir))
                        Directory.CreateDirectory(dstDir);

                    if (File.Exists(src))
                        File.Copy(src, dst, true);
                }

                currentItem++;
            }

            onProgress?.Invoke("Backup complete", 1.0);
        }

        public async Task RestoreAsync(string backupPath, string targetVersionPath, List<BackupItem> items, Action<string, double>? onProgress = null)
        {
            int totalItems = items.Count;
            int currentItem = 0;

            foreach (var item in items.Where(i => i.IsEnabled))
            {
                var src = Path.Combine(backupPath, item.RelativePath);
                var dst = Path.Combine(targetVersionPath, item.RelativePath);

                onProgress?.Invoke($"Restoring {item.Name}...", (double)currentItem / totalItems);

                if (item.IsFolder)
                {
                    if (Directory.Exists(src))
                        await CopyDirectoryAsync(src, dst);
                }
                else
                {
                    var dstDir = Path.GetDirectoryName(dst);
                    if (dstDir != null && !Directory.Exists(dstDir))
                        Directory.CreateDirectory(dstDir);

                    if (File.Exists(src))
                        File.Copy(src, dst, true);
                }

                currentItem++;
            }

            onProgress?.Invoke("Restore complete", 1.0);
        }

        public async Task SyncAsync(string sourceVersionPath, string targetVersionPath, List<BackupItem> items, Action<string, double>? onProgress = null)
        {
            await RestoreAsync(sourceVersionPath, targetVersionPath, items, onProgress);
        }

        private async Task CopyDirectoryAsync(string sourceDir, string destinationDir)
        {
            if (!Directory.Exists(sourceDir)) return;

            Directory.CreateDirectory(destinationDir);

            foreach (var file in Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories))
            {
                // Filter out ignored folders and extensions
                var relativePath = Path.GetRelativePath(sourceDir, file);
                var pathParts = relativePath.Split(Path.DirectorySeparatorChar);

                if (pathParts.Any(p => IgnoredFolders.Contains(p))) continue;
                if (IgnoredExtensions.Contains(Path.GetExtension(file))) continue;

                var destFile = Path.Combine(destinationDir, relativePath);
                var destFileDir = Path.GetDirectoryName(destFile);
                if (destFileDir != null) Directory.CreateDirectory(destFileDir);

                await Task.Run(() => File.Copy(file, destFile, true));
            }
        }

        public List<string> GetBackups(string backupRoot)
        {
            if (!Directory.Exists(backupRoot)) return new List<string>();

            return Directory.GetDirectories(backupRoot)
                            .Select(Path.GetFileName)
                            .Where(n => !string.IsNullOrEmpty(n))
                            .Cast<string>()
                            .OrderByDescending(n => n)
                            .ToList();
        }

        public void DeleteBackup(string backupRoot, string backupName)
        {
            var backupPath = Path.Combine(backupRoot, backupName);
            if (Directory.Exists(backupPath))
            {
                Directory.Delete(backupPath, true);
            }
        }

        public void OpenBackupFolder(string backupRoot)
        {
            if (Directory.Exists(backupRoot))
            {
                System.Diagnostics.Process.Start("explorer.exe", backupRoot);
            }
        }
        /// <summary>
        /// Extracts major.minor version from a folder name like "Blender 4.2" or "Blender 3.6".
        /// </summary>
        private static string? ExtractVersionFromName(string dirName)
        {
            // Match patterns like "Blender 4.2", "Blender4.2", "blender-4.2"
            var match = System.Text.RegularExpressions.Regex.Match(dirName, @"(\d+\.\d+)");
            return match.Success ? match.Groups[1].Value : null;
        }

        /// <summary>
        /// Extracts major.minor from a full version string like "4.2.0" or "4.2.1.0".
        /// </summary>
        private static string? ExtractMajorMinor(string? fullVersion)
        {
            if (string.IsNullOrEmpty(fullVersion)) return null;

            var parts = fullVersion.Split('.');
            if (parts.Length >= 2 && int.TryParse(parts[0], out _) && int.TryParse(parts[1], out _))
            {
                return $"{parts[0]}.{parts[1]}";
            }
            return null;
        }
    }
}

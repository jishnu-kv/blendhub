using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace BlendHub.Services
{
    public class AddonItem : System.ComponentModel.INotifyPropertyChanged
    {
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        private double _cardWidth = 250.0;
        public double CardWidth
        {
            get => _cardWidth;
            set
            {
                if (_cardWidth != value)
                {
                    _cardWidth = value;
                    PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(nameof(CardWidth)));
                }
            }
        }

        public string Name { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Author { get; set; } = "Unknown";
        public string Description { get; set; } = string.Empty;
        public string Category { get; set; } = "General";
        public string Type { get; set; } = "Legacy Addon"; // "Legacy Addon" or "Extension"
        public string Repository { get; set; } = string.Empty; // e.g. "blender_org", "user_default" (empty for legacy)
        public string BlenderVersion { get; set; } = string.Empty; // e.g. "5.1"
        public string Path { get; set; } = string.Empty; // Absolute path to directory or .py file
        public string WebsiteUrl { get; set; } = string.Empty;
        public string Location { get; set; } = string.Empty;
        public string Support { get; set; } = string.Empty;
        public string BlenderVersionMin { get; set; } = string.Empty;
        public string BlenderVersionMax { get; set; } = string.Empty;
        public string License { get; set; } = string.Empty;
        public string Copyright { get; set; } = string.Empty;
        public string Permissions { get; set; } = string.Empty;
        public long ArchiveSize { get; set; }

        public List<string> BlenderVersions { get; set; } = new();
        public List<string> InstallationPaths { get; set; } = new();
    }

    public class AddonService
    {
        private static readonly string[] IgnoredFolders = { "__pycache__", ".cache", ".local", ".git", ".svn", ".idea", ".vscode" };

        /// <summary>
        /// Scans for both legacy addons and modern extensions for a given Blender configuration path.
        /// </summary>
        public async Task<List<AddonItem>> ScanAddonsAsync(string configPath, string blenderVersion)
        {
            var addons = new List<AddonItem>();
            if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
                return addons;

            // 1. Scan Legacy Addons: configPath/scripts/addons/
            var legacyPath = Path.Combine(configPath, "scripts", "addons");
            if (Directory.Exists(legacyPath))
            {
                await Task.Run(() =>
                {
                    try
                    {
                        // Scan subdirectories
                        foreach (var dir in Directory.GetDirectories(legacyPath))
                        {
                            try
                            {
                                var folderName = Path.GetFileName(dir);
                                if (folderName.StartsWith(".") || folderName.StartsWith("_") || IgnoredFolders.Contains(folderName))
                                    continue;

                                addons.Add(ParseLegacyAddon(dir, blenderVersion));
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[AddonService] Failed to scan legacy addon dir '{dir}': {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AddonService] Failed to read legacy directories from '{legacyPath}': {ex.Message}");
                    }

                    try
                    {
                        // Scan single-file .py addons
                        foreach (var file in Directory.GetFiles(legacyPath, "*.py"))
                        {
                            try
                            {
                                var fileName = Path.GetFileName(file);
                                if (fileName.StartsWith(".") || fileName.StartsWith("_") || fileName.Equals("__init__.py"))
                                    continue;

                                addons.Add(ParseLegacyAddon(file, blenderVersion));
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[AddonService] Failed to scan legacy addon file '{file}': {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AddonService] Failed to read legacy files from '{legacyPath}': {ex.Message}");
                    }
                });
            }

            // 2. Scan Modern Extensions: configPath/extensions/
            var extensionsPath = Path.Combine(configPath, "extensions");
            if (Directory.Exists(extensionsPath))
            {
                await Task.Run(() =>
                {
                    try
                    {
                        foreach (var repoDir in Directory.GetDirectories(extensionsPath))
                        {
                            try
                            {
                                var repoName = Path.GetFileName(repoDir);
                                if (repoName.StartsWith(".") || repoName.StartsWith("_") || IgnoredFolders.Contains(repoName))
                                    continue;

                                // Scan extensions inside repository
                                foreach (var extDir in Directory.GetDirectories(repoDir))
                                {
                                    try
                                    {
                                        var extFolderName = Path.GetFileName(extDir);
                                        if (extFolderName.StartsWith(".") || extFolderName.StartsWith("_") || IgnoredFolders.Contains(extFolderName))
                                            continue;

                                        addons.Add(ParseExtension(extDir, repoName, blenderVersion));
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[AddonService] Failed to scan extension '{extDir}': {ex.Message}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine($"[AddonService] Failed to scan repository directory '{repoDir}': {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[AddonService] Failed to read repositories from '{extensionsPath}': {ex.Message}");
                    }
                });
            }

            return addons.OrderBy(a => a.Name).ToList();
        }

        /// <summary>
        /// Parses metadata from a legacy Blender addon (either a folder with __init__.py or a single .py file).
        /// </summary>
        private static AddonItem ParseLegacyAddon(string path, string blenderVersion)
        {
            var item = new AddonItem
            {
                FolderName = Path.GetFileName(path),
                Path = path,
                Type = "Legacy Addon",
                BlenderVersion = blenderVersion,
                Name = Path.GetFileNameWithoutExtension(path) // Fallback name
            };

            item.BlenderVersions.Add(blenderVersion);
            item.InstallationPaths.Add(path);

            string pyContent = string.Empty;
            try
            {
                if (Directory.Exists(path))
                {
                    var initFile = Path.Combine(path, "__init__.py");
                    if (File.Exists(initFile))
                    {
                        pyContent = File.ReadAllText(initFile);
                    }
                }
                else if (File.Exists(path))
                {
                    pyContent = File.ReadAllText(path);
                }
            }
            catch { /* Ignore IO errors */ }

            if (!string.IsNullOrEmpty(pyContent))
            {
                int startIdx = pyContent.IndexOf("bl_info");
                if (startIdx != -1)
                {
                    int braceStart = pyContent.IndexOf('{', startIdx);
                    if (braceStart != -1)
                    {
                        // Extract a large enough chunk to capture bl_info keys
                        var searchArea = pyContent.Substring(braceStart);
                        
                        item.Name = ExtractPyField(searchArea, "name") ?? item.Name;
                        item.Author = ExtractPyField(searchArea, "author") ?? "Unknown";
                        item.Description = ExtractPyField(searchArea, "description") ?? "";
                        item.Category = ExtractPyField(searchArea, "category") ?? "General";
                        
                        var website = ExtractPyField(searchArea, "doc_url") ?? ExtractPyField(searchArea, "tracker_url") ?? string.Empty;
                        item.WebsiteUrl = website;
                        item.Location = ExtractPyField(searchArea, "location") ?? string.Empty;
                        item.Support = ExtractPyField(searchArea, "support") ?? string.Empty;

                        // Parse version tuple e.g. "version": (1, 0, 2)
                        var versionMatch = Regex.Match(searchArea, @"[""']version[""']\s*:\s*(?:\(([^)]+)\)|\[([^\]]+)\]|[""']([^""']+)[""'])");
                        if (versionMatch.Success)
                        {
                            var verStr = versionMatch.Groups[1].Value;
                            if (string.IsNullOrEmpty(verStr)) verStr = versionMatch.Groups[2].Value;
                            if (string.IsNullOrEmpty(verStr)) verStr = versionMatch.Groups[3].Value;

                            if (!string.IsNullOrEmpty(verStr))
                            {
                                item.Version = string.Join(".", verStr.Split(',')
                                                     .Select(s => s.Trim().Trim('"', '\''))
                                                     .Where(s => !string.IsNullOrEmpty(s)));
                            }
                        }
                    }
                }
            }

            if (string.IsNullOrEmpty(item.Version)) item.Version = "1.0.0";
            return item;
        }

        /// <summary>
        /// Extract standard python field values (handles single quotes, double quotes, and triple quotes).
        /// </summary>
        private static string? ExtractPyField(string area, string fieldName)
        {
            var patterns = new[]
            {
                $@"[""']{fieldName}[""']\s*:\s*""""""(.*?)""""""",
                $@"[""']{fieldName}[""']\s*:\s*''''''(.*?)''''''",
                $@"[""']{fieldName}[""']\s*:\s*""([^""\\]*(?:\\.[^""\\]*)*)""",
                $@"[""']{fieldName}[""']\s*:\s*'([^'\\]*(?:\\.[^'\\]*)*)'"
            };

            foreach (var pattern in patterns)
            {
                var match = Regex.Match(area, pattern, RegexOptions.Singleline);
                if (match.Success)
                {
                    var val = match.Groups[1].Value;
                    return val.Replace("\\\"", "\"").Replace("\\'", "'").Trim();
                }
            }

            return null;
        }

        /// <summary>
        /// Parses metadata from a modern Blender extension's blender_manifest.toml.
        /// </summary>
        private static AddonItem ParseExtension(string path, string repository, string blenderVersion)
        {
            var item = new AddonItem
            {
                FolderName = Path.GetFileName(path),
                Path = path,
                Type = "Extension",
                Repository = repository,
                BlenderVersion = blenderVersion,
                Name = Path.GetFileName(path) // Fallback name
            };

            item.BlenderVersions.Add(blenderVersion);
            item.InstallationPaths.Add(path);

            var manifestFile = Path.Combine(path, "blender_manifest.toml");
            if (File.Exists(manifestFile))
            {
                try
                {
                    var lines = File.ReadAllLines(manifestFile);
                    bool inPermissions = false;
                    bool inLicenseArray = false;
                    bool inCopyrightArray = false;
                    var permissionsList = new List<string>();
                    var licenseList = new List<string>();
                    var copyrightList = new List<string>();

                    foreach (var line in lines)
                    {
                        var trimmed = line.Trim();
                        if (string.IsNullOrEmpty(trimmed)) continue;

                        // Check section headers
                        if (trimmed.StartsWith("[") && trimmed.EndsWith("]"))
                        {
                            inPermissions = trimmed.Equals("[permissions]", StringComparison.OrdinalIgnoreCase);
                            inLicenseArray = false;
                            inCopyrightArray = false;
                            continue;
                        }

                        // Check comments
                        if (trimmed.StartsWith("#")) continue;

                        // If inside permissions block
                        if (inPermissions)
                        {
                            int eqIdx = trimmed.IndexOf('=');
                            if (eqIdx != -1)
                            {
                                var permKey = trimmed.Substring(0, eqIdx).Trim();
                                var permVal = trimmed.Substring(eqIdx + 1).Trim();
                                permVal = CleanQuotes(permVal);
                                if (!string.IsNullOrEmpty(permKey) && !string.IsNullOrEmpty(permVal))
                                {
                                    var permName = char.ToUpper(permKey[0]) + permKey.Substring(1);
                                    permissionsList.Add($"{permName}: {permVal}");
                                }
                            }
                            continue;
                        }

                        // Check array continuation
                        if (inLicenseArray)
                        {
                            if (trimmed.Contains("]"))
                            {
                                inLicenseArray = false;
                            }
                            var vals = ExtractQuotedStrings(trimmed);
                            licenseList.AddRange(vals);
                            continue;
                        }
                        if (inCopyrightArray)
                        {
                            if (trimmed.Contains("]"))
                            {
                                inCopyrightArray = false;
                            }
                            var vals = ExtractQuotedStrings(trimmed);
                            copyrightList.AddRange(vals);
                            continue;
                        }

                        // Standard key-value parsing
                        int eq = trimmed.IndexOf('=');
                        if (eq == -1) continue;

                        var key = trimmed.Substring(0, eq).Trim().ToLowerInvariant();
                        var val = trimmed.Substring(eq + 1).Trim();

                        // Detect arrays
                        if (val.StartsWith("["))
                        {
                            if (key == "license")
                            {
                                if (val.Contains("]"))
                                {
                                    licenseList.AddRange(ExtractQuotedStrings(val));
                                }
                                else
                                {
                                    inLicenseArray = true;
                                }
                                continue;
                            }
                            if (key == "copyright")
                            {
                                if (val.Contains("]"))
                                {
                                    copyrightList.AddRange(ExtractQuotedStrings(val));
                                }
                                else
                                {
                                    inCopyrightArray = true;
                                }
                                continue;
                            }
                        }

                        val = CleanQuotes(val);

                        switch (key)
                        {
                            case "name":
                                item.Name = val;
                                break;
                            case "version":
                                item.Version = val;
                                break;
                             case "author":
                             case "maintainer":
                                 {
                                     string cleanVal = val;
                                     int ltIdx = cleanVal.IndexOf('<');
                                     if (ltIdx >= 0)
                                     {
                                         cleanVal = cleanVal.Substring(0, ltIdx).Trim();
                                     }
                                     item.Author = cleanVal;
                                 }
                                 break;
                            case "tagline":
                            case "description":
                                item.Description = val;
                                break;
                            case "website":
                            case "url":
                                item.WebsiteUrl = val;
                                break;
                            case "category":
                                item.Category = val;
                                break;
                            case "blender_version_min":
                                item.BlenderVersionMin = val;
                                break;
                            case "blender_version_max":
                                item.BlenderVersionMax = val;
                                break;
                            case "license":
                                licenseList.Add(val);
                                break;
                            case "copyright":
                                copyrightList.Add(val);
                                break;
                        }
                    }

                    if (permissionsList.Count > 0)
                        item.Permissions = string.Join(", ", permissionsList);

                    if (licenseList.Count > 0)
                        item.License = string.Join(", ", licenseList.Where(s => !string.IsNullOrEmpty(s)));

                    if (copyrightList.Count > 0)
                        item.Copyright = string.Join(", ", copyrightList.Where(s => !string.IsNullOrEmpty(s)));
                }
                catch { /* Ignore IO errors */ }
            }

            if (string.IsNullOrEmpty(item.Version)) item.Version = "1.0.0";
            if (string.IsNullOrEmpty(item.Author)) item.Author = "Unknown";
            if (string.IsNullOrEmpty(item.Category)) item.Category = "General";

            return item;
        }

        private static string CleanQuotes(string val)
        {
            if ((val.StartsWith("\"") && val.EndsWith("\"")) || (val.StartsWith("'") && val.EndsWith("'")))
            {
                if (val.Length >= 2)
                    val = val.Substring(1, val.Length - 2);
            }
            return val.Trim();
        }

        private static List<string> ExtractQuotedStrings(string input)
        {
            var list = new List<string>();
            var matches = Regex.Matches(input, @"[""']([^""']+)[""']");
            foreach (Match m in matches)
            {
                if (m.Success && m.Groups[1].Success)
                {
                    var val = m.Groups[1].Value.Trim();
                    if (!string.IsNullOrEmpty(val))
                        list.Add(val);
                }
            }
            return list;
        }

        /// <summary>
        /// Installs an addon or extension. Standard .zip files are auto-analyzed:
        /// - If blender_manifest.toml is found inside, it is treated as a modern Extension and extracted to extensions/selectedRepository.
        /// - Otherwise, it is extracted to scripts/addons/.
        /// - Single .py files are copied directly to scripts/addons/.
        /// </summary>
        public async Task InstallAddonAsync(string sourceFilePath, string configPath, string selectedRepository = "user_default")
        {
            if (string.IsNullOrEmpty(sourceFilePath) || !File.Exists(sourceFilePath))
                throw new FileNotFoundException("Addon source file not found.", sourceFilePath);

            if (string.IsNullOrEmpty(configPath) || !Directory.Exists(configPath))
                throw new DirectoryNotFoundException($"Blender configuration folder not found at '{configPath}'. Make sure that Blender version has been launched at least once.");

            var ext = Path.GetExtension(sourceFilePath).ToLowerInvariant();

            if (ext == ".py")
            {
                // Single-file legacy addon
                var targetDir = Path.Combine(configPath, "scripts", "addons");
                Directory.CreateDirectory(targetDir);

                var destFile = Path.Combine(targetDir, Path.GetFileName(sourceFilePath));
                await Task.Run(() => File.Copy(sourceFilePath, destFile, true));
            }
            else if (ext == ".zip")
            {
                await Task.Run(() =>
                {
                    using (var archive = ZipFile.OpenRead(sourceFilePath))
                    {
                        // Check if it's a modern extension
                        bool isExtension = archive.Entries.Any(e => e.FullName.EndsWith("blender_manifest.toml", StringComparison.OrdinalIgnoreCase));

                        string targetBaseDir;
                        if (isExtension)
                        {
                            targetBaseDir = Path.Combine(configPath, "extensions", selectedRepository);
                        }
                        else
                        {
                            targetBaseDir = Path.Combine(configPath, "scripts", "addons");
                        }

                        Directory.CreateDirectory(targetBaseDir);

                        // Determine the output directory name
                        var firstEntry = archive.Entries.FirstOrDefault();
                        bool hasCommonParent = false;
                        string commonParent = string.Empty;

                        if (firstEntry != null)
                        {
                            int slashIdx = firstEntry.FullName.IndexOf('/');
                            if (slashIdx == -1) slashIdx = firstEntry.FullName.IndexOf('\\');

                            if (slashIdx != -1)
                            {
                                var parent = firstEntry.FullName.Substring(0, slashIdx + 1);
                                if (archive.Entries.All(e => e.FullName.StartsWith(parent)))
                                {
                                    hasCommonParent = true;
                                    commonParent = parent.TrimEnd('/', '\\');
                                }
                            }
                        }

                        string? extensionId = null;
                        if (isExtension)
                        {
                            var manifestEntry = archive.Entries.FirstOrDefault(e => e.Name.Equals("blender_manifest.toml", StringComparison.OrdinalIgnoreCase) || e.Name.EndsWith("/blender_manifest.toml", StringComparison.OrdinalIgnoreCase));
                            if (manifestEntry != null)
                            {
                                try
                                {
                                    using (var stream = manifestEntry.Open())
                                    using (var reader = new StreamReader(stream))
                                    {
                                        string content = reader.ReadToEnd();
                                        var match = Regex.Match(content, @"(?m)^\s*id\s*=\s*[""']([^""']+)[""']");
                                        if (match.Success)
                                        {
                                            extensionId = match.Groups[1].Value.Trim();
                                        }
                                    }
                                }
                                catch { }
                            }
                        }

                        string extractSubDirName;
                        if (isExtension && !string.IsNullOrEmpty(extensionId))
                        {
                            extractSubDirName = extensionId;
                        }
                        else if (hasCommonParent)
                        {
                            extractSubDirName = commonParent;
                        }
                        else
                        {
                            var name = Path.GetFileNameWithoutExtension(sourceFilePath);
                            // If it matches name_guid (32 hex characters)
                            var guidMatch = Regex.Match(name, @"^(.*)_[0-9a-fA-F]{32}$");
                            if (guidMatch.Success)
                            {
                                extractSubDirName = guidMatch.Groups[1].Value;
                            }
                            else
                            {
                                extractSubDirName = name;
                            }
                        }

                        // Sanitize directory name
                        extractSubDirName = Regex.Replace(extractSubDirName, @"[^a-zA-Z0-9_\-]", "_");

                        var extractPath = Path.Combine(targetBaseDir, extractSubDirName);
                        if (Directory.Exists(extractPath))
                        {
                            Directory.Delete(extractPath, true);
                        }
                        Directory.CreateDirectory(extractPath);

                        try
                        {
                            foreach (var entry in archive.Entries)
                            {
                                if (string.IsNullOrEmpty(entry.Name)) continue; // Skip directory entry markers

                                string relativePath = entry.FullName;
                                if (hasCommonParent && relativePath.StartsWith(commonParent))
                                {
                                    relativePath = relativePath.Substring(commonParent.Length + 1);
                                }

                                string destinationPath = Path.GetFullPath(Path.Combine(extractPath, relativePath));

                                // Directory safety check (prevent zip slip vulnerability)
                                if (!destinationPath.StartsWith(extractPath, StringComparison.OrdinalIgnoreCase))
                                {
                                    throw new InvalidDataException("Malicious zip path detected.");
                                }

                                var entryDir = Path.GetDirectoryName(destinationPath);
                                if (entryDir != null) Directory.CreateDirectory(entryDir);

                                entry.ExtractToFile(destinationPath, true);
                            }
                        }
                        catch
                        {
                            // Clean up partial/corrupted directory extraction
                            try
                            {
                                if (Directory.Exists(extractPath))
                                {
                                    Directory.Delete(extractPath, true);
                                }
                            }
                            catch { }
                            throw;
                        }
                    }
                });
            }
            else
            {
                throw new NotSupportedException("Only .py and .zip Blender addons are supported.");
            }
        }

        /// <summary>
        /// Uninstalls / Deletes an addon or extension.
        /// </summary>
        public async Task UninstallAddonAsync(AddonItem item)
        {
            if (string.IsNullOrEmpty(item.Path))
                throw new ArgumentException("Addon path is invalid.");

            await Task.Run(() =>
            {
                if (Directory.Exists(item.Path))
                {
                    Directory.Delete(item.Path, true);
                }
                else if (File.Exists(item.Path))
                {
                    File.Delete(item.Path);
                }
                else
                {
                    throw new FileNotFoundException("Addon files not found on disk.");
                }
            });
        }

        /// <summary>
        /// Syncs (copies) an installed addon to another Blender version's configuration.
        /// </summary>
        public async Task SyncAddonAsync(AddonItem item, string targetConfigPath)
        {
            if (string.IsNullOrEmpty(item.Path) || (!Directory.Exists(item.Path) && !File.Exists(item.Path)))
                throw new FileNotFoundException("Source addon files not found.", item.Path);

            if (string.IsNullOrEmpty(targetConfigPath) || !Directory.Exists(targetConfigPath))
                throw new DirectoryNotFoundException($"Target Blender configuration folder not found at '{targetConfigPath}'.");

            await Task.Run(() =>
            {
                string destinationPath;
                if (item.Type == "Extension")
                {
                    var repo = !string.IsNullOrEmpty(item.Repository) ? item.Repository : "user_default";
                    destinationPath = Path.Combine(targetConfigPath, "extensions", repo, item.FolderName);
                }
                else
                {
                    // Legacy Addon
                    if (Directory.Exists(item.Path))
                    {
                        destinationPath = Path.Combine(targetConfigPath, "scripts", "addons", item.FolderName);
                    }
                    else
                    {
                        destinationPath = Path.Combine(targetConfigPath, "scripts", "addons", Path.GetFileName(item.Path));
                    }
                }

                // Ensure parent directory exists
                var parentDir = Path.GetDirectoryName(destinationPath);
                if (parentDir != null)
                {
                    Directory.CreateDirectory(parentDir);
                }

                if (Directory.Exists(item.Path))
                {
                    if (Directory.Exists(destinationPath))
                    {
                        Directory.Delete(destinationPath, true);
                    }
                    CopyDirectory(item.Path, destinationPath);
                }
                else if (File.Exists(item.Path))
                {
                    File.Copy(item.Path, destinationPath, true);
                }
            });
        }

        private static void CopyDirectory(string sourceDir, string destinationDir)
        {
            Directory.CreateDirectory(destinationDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(destinationDir, Path.GetFileName(file));
                File.Copy(file, destFile, true);
            }

            foreach (string subDir in Directory.GetDirectories(sourceDir))
            {
                string destSubDir = Path.Combine(destinationDir, Path.GetFileName(subDir));
                CopyDirectory(subDir, destSubDir);
            }
        }
    }
}

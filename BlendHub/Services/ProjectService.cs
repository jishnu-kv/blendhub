using BlendHub.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace BlendHub.Services
{
    public class ProjectService
    {
        private static readonly string ProjectsFilePath;

        static ProjectService()
        {
            var appDataRoaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var BlendHubFolder = Path.Combine(appDataRoaming, "BlendHub");
            if (!Directory.Exists(BlendHubFolder))
            {
                Directory.CreateDirectory(BlendHubFolder);
            }
            ProjectsFilePath = Path.Combine(BlendHubFolder, "projects.json");
        }

        public static List<Project> LoadProjects()
        {
            try
            {
                if (File.Exists(ProjectsFilePath))
                {
                    string json = File.ReadAllText(ProjectsFilePath);
                    return JsonSerializer.Deserialize<List<Project>>(json) ?? new List<Project>();
                }
            }
            catch (Exception) { }
            return new List<Project>();
        }

        public static void SaveProjects(List<Project> projects)
        {
            try
            {
                string json = JsonSerializer.Serialize(projects, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(ProjectsFilePath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ProjectService] Error saving projects: {ex.Message}");
            }
        }

        public static void UpdateProject(Project project)
        {
            var projects = LoadProjects();
            var index = projects.FindIndex(p => p.Name == project.Name && p.Path == project.Path);
            if (index != -1)
            {
                projects[index] = project;
                SaveProjects(projects);
            }
        }

        public static void RemoveProject(Project project)
        {
            var projects = LoadProjects();
            var initialCount = projects.Count;

            // Remove all projects matching both name and path
            projects.RemoveAll(p => p.Name == project.Name && p.Path == project.Path);

            Debug.WriteLine($"[ProjectService] Removed {initialCount - projects.Count} project(s) matching '{project.Name}' at '{project.Path}'");

            SaveProjects(projects);
        }

        public static async Task DetectProjectVersionsAsync(List<Project> projects)
        {
            Debug.WriteLine($"[ProjectService] DetectProjectVersionsAsync started for {projects.Count} project(s)");
            bool anyUpdated = false;

            // Get installed blender to use as Python runner
            string? blenderExe = null;
            try
            {
                var blenderService = new BlenderSettingsService();
                var installed = blenderService.GetInstalledVersions();
                Debug.WriteLine($"[ProjectService] Found {installed.Count} installed/custom Blender versions in service.");
                foreach (var inst in installed)
                {
                    Debug.WriteLine($"[ProjectService] Installed version: {inst.Version}, path: {inst.ExecutablePath}");
                }

                blenderExe = installed.FirstOrDefault(v =>
                    !string.IsNullOrEmpty(v.ExecutablePath) &&
                    !v.ExecutablePath.Contains("WindowsApps", StringComparison.OrdinalIgnoreCase))?.ExecutablePath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProjectService] Error getting installed versions: {ex.Message}");
            }

            if (string.IsNullOrEmpty(blenderExe))
            {
                Debug.WriteLine("[ProjectService] No Blender executable found. Skipping auto-detection.");
                return; // Can't detect without a Blender installation
            }

            Debug.WriteLine($"[ProjectService] Using Blender executable for detection: '{blenderExe}'");

            foreach (var project in projects)
            {
                Debug.WriteLine($"[ProjectService] Checking project: '{project.Name}' (Current version: '{project.BlenderVersion}')");
                if (string.IsNullOrEmpty(project.BlenderVersion) || project.BlenderVersion == "Unknown")
                {
                    string path = project.FullBlendPath;
                    Debug.WriteLine($"[ProjectService] Project full blend path: '{path}'");
                    if (File.Exists(path))
                    {
                        Debug.WriteLine($"[ProjectService] Blend file exists. Starting Python check...");
                        string version = await DetectVersionUsingBlenderAsync(blenderExe, path);
                        Debug.WriteLine($"[ProjectService] Python check result: '{version}'");
                        if (version != "Unknown")
                        {
                            project.BlenderVersion = version;
                            anyUpdated = true;
                        }
                    }
                    else
                    {
                        Debug.WriteLine($"[ProjectService] Blend file does NOT exist at '{path}'");
                    }
                }
            }

            if (anyUpdated)
            {
                Debug.WriteLine("[ProjectService] Saving projects with newly detected versions...");
                SaveProjects(projects);
            }
        }

        private static async Task<string> DetectVersionUsingBlenderAsync(string blenderExe, string blendFilePath)
        {
            string scriptPath = "";
            try
            {
                var appDataFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "BlendHub");
                if (!Directory.Exists(appDataFolder))
                {
                    Directory.CreateDirectory(appDataFolder);
                }

                scriptPath = Path.Combine(appDataFolder, "detect_version.py");
                Debug.WriteLine($"[ProjectService] Writing temporary python script to '{scriptPath}'");

                // Write standard Blender Python script to read saved file version cleanly
                string scriptContent =
                    "import bpy\n" +
                    "try:\n" +
                    "    ver = '.'.join(map(str, bpy.data.version))\n" +
                    "    if not ver or ver == '0.0.0':\n" +
                    "        ver = bpy.app.version_string\n" +
                    "    print('PROJECT_VERSION:', ver, flush=True)\n" +
                    "except Exception as e:\n" +
                    "    try:\n" +
                    "        print('PROJECT_VERSION:', bpy.app.version_string, flush=True)\n" +
                    "    except:\n" +
                    "        print('PROJECT_VERSION: Unknown', flush=True)\n";

                await File.WriteAllTextAsync(scriptPath, scriptContent);

                Debug.WriteLine($"[ProjectService] Launching background Blender process: '{blenderExe}' with arguments: --background \"{blendFilePath}\" --python \"{scriptPath}\"");

                using (var process = new Process())
                {
                    process.StartInfo = new ProcessStartInfo
                    {
                        FileName = blenderExe,
                        Arguments = $"--background \"{blendFilePath}\" --python \"{scriptPath}\"",
                        UseShellExecute = false,
                        RedirectStandardOutput = true,
                        RedirectStandardError = false, // Disable stderr to avoid pipeline blocks
                        CreateNoWindow = true
                    };

                    process.Start();

                    // Read standard output asynchronously to the end
                    string output = await process.StandardOutput.ReadToEndAsync();
                    Debug.WriteLine($"[ProjectService] Blender raw stdout output:\n=== START OUTPUT ===\n{output}\n=== END OUTPUT ===");

                    // WaitForExit with a timeout of 10 seconds
                    var exitTask = process.WaitForExitAsync();
                    var timeoutTask = Task.Delay(10000);
                    var completedTask = await Task.WhenAny(exitTask, timeoutTask);

                    if (completedTask == timeoutTask)
                    {
                        Debug.WriteLine("[ProjectService] Blender process timed out after 10 seconds!");
                        try { process.Kill(); } catch { }
                        return "Unknown";
                    }

                    // Parse the captured output for version signature
                    var lines = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var line in lines)
                    {
                        if (line.Contains("PROJECT_VERSION:"))
                        {
                            var parts = line.Split("PROJECT_VERSION:");
                            if (parts.Length > 1)
                            {
                                string verStr = parts[1].Trim(); // e.g. "3.6.0" or "4.2.0" or "5.1.2"
                                Debug.WriteLine($"[ProjectService] Parsed PROJECT_VERSION line: '{line}' -> extracted: '{verStr}'");
                                if (!string.IsNullOrEmpty(verStr) && verStr != "Unknown")
                                {
                                    var verParts = verStr.Split('.');
                                    if (verParts.Length >= 2)
                                    {
                                        string formatted = $"{verParts[0]}.{verParts[1]}";
                                        Debug.WriteLine($"[ProjectService] Formatted version: '{formatted}'");
                                        return formatted;
                                    }
                                    return verStr;
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ProjectService] Python detection failed with exception: {ex.Message}\nStacktrace: {ex.StackTrace}");
            }
            finally
            {
                // Clean up the script file if it exists
                if (!string.IsNullOrEmpty(scriptPath) && File.Exists(scriptPath))
                {
                    try { File.Delete(scriptPath); } catch { }
                }
            }

            Debug.WriteLine("[ProjectService] Python check completed but returned 'Unknown'.");
            return "Unknown";
        }
    }
}

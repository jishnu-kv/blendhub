using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace src.Models
{
    public enum BoardItemType
    {
        Image,
        Text,
        Drawing,
        Shape
    }

    public class BoardItemData
    {
        public BoardItemType Type { get; set; }
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public double Rotation { get; set; }
        public int ZIndex { get; set; }

        // Specific properties
        public string? Content { get; set; } // Text content or Path for images/drawings
        public string? TextColorHex { get; set; }
        public string? BgColorHex { get; set; }
        public double FontSize { get; set; }
        public string? ShapeType { get; set; } // "Rectangle", "Circle", "Arrow"
        public List<PointData>? Points { get; set; } // For freehand drawings
    }

    public class PointData
    {
        public double X { get; set; }
        public double Y { get; set; }
    }

    public class BoardData
    {
        public string Name { get; set; } = "Untitled";
        public DateTime LastModified { get; set; }
        public double ViewportX { get; set; }
        public double ViewportY { get; set; }
        public float ZoomFactor { get; set; } = 1.0f;
        public List<BoardItemData> Items { get; set; } = new List<BoardItemData>();
    }

    public static class BoardPersistence
    {
        private static string? _saveFolder;
        private static string SaveFolder
        {
            get
            {
                if (_saveFolder == null)
                {
                    string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    _saveFolder = Path.Combine(localAppData, "BlendHub", "Boards");

                    if (!Directory.Exists(_saveFolder))
                    {
                        Directory.CreateDirectory(_saveFolder);
                    }
                }
                return _saveFolder;
            }
        }

        public static async Task SaveBoardAsync(BoardData board)
        {
            board.LastModified = DateTime.Now;
            // Sanitize filename
            string safeName = string.Join("_", board.Name.Split(Path.GetInvalidFileNameChars()));
            string fileName = $"{safeName}.json";
            string filePath = Path.Combine(SaveFolder, fileName);

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals
            };
            string json = JsonSerializer.Serialize(board, options);
            await File.WriteAllTextAsync(filePath, json);
        }

        public static string GetAssetsFolder(string? boardName)
        {
            string folderName = string.IsNullOrEmpty(boardName) ? "Temp" : boardName + "_Assets";
            string folder = Path.Combine(SaveFolder, folderName);
            if (!Directory.Exists(folder)) Directory.CreateDirectory(folder);
            return folder;
        }

        public static async Task<string> CopyImageToAssetsAsync(string sourcePath, string? boardName)
        {
            string assetsFolder = GetAssetsFolder(boardName);
            string fileName = $"{DateTime.Now.Ticks}_{Path.GetFileName(sourcePath)}";
            string destPath = Path.Combine(assetsFolder, fileName);

            try
            {
                await Task.Run(() => File.Copy(sourcePath, destPath, true));
                return destPath;
            }
            catch
            {
                return sourcePath; // Fallback to original if copy fails
            }
        }

        public static async Task<string> SaveStreamToAssetsAsync(Stream stream, string extension, string? boardName)
        {
            string assetsFolder = GetAssetsFolder(boardName);
            string fileName = $"clip_{DateTime.Now.Ticks}{extension}";
            string destPath = Path.Combine(assetsFolder, fileName);

            using (var fileStream = File.Create(destPath))
            {
                stream.Seek(0, SeekOrigin.Begin);
                await stream.CopyToAsync(fileStream);
            }
            return destPath;
        }

        public static string ColorToHex(Windows.UI.Color color)
        {
            return $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";
        }

        public static Windows.UI.Color HexToColor(string hex)
        {
            hex = hex.Replace("#", "");
            if (hex.Length == 6) hex = "FF" + hex;
            byte a = byte.Parse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber);
            byte r = byte.Parse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber);
            byte g = byte.Parse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber);
            byte b = byte.Parse(hex.Substring(6, 2), System.Globalization.NumberStyles.HexNumber);
            return Windows.UI.Color.FromArgb(a, r, g, b);
        }

        public static async Task<BoardData?> LoadBoardAsync(string name)
        {
            string filePath = Path.Combine(SaveFolder, $"{name}.json");
            if (!File.Exists(filePath)) return null;

            string json = await File.ReadAllTextAsync(filePath);
            return JsonSerializer.Deserialize<BoardData>(json);
        }

        public static List<string> GetSavedBoardNames()
        {
            var names = new List<string>();
            if (!Directory.Exists(SaveFolder)) return names;

            foreach (var file in Directory.GetFiles(SaveFolder, "*.json"))
            {
                names.Add(Path.GetFileNameWithoutExtension(file));
            }
            return names;
        }

        public static void DeleteBoard(string name)
        {
            string filePath = Path.Combine(SaveFolder, $"{name}.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
            }

            // Delete assets folder
            string assetsFolder = Path.Combine(SaveFolder, name + "_Assets");
            if (Directory.Exists(assetsFolder))
            {
                try
                {
                    Directory.Delete(assetsFolder, true);
                }
                catch { }
            }
        }
    }
}

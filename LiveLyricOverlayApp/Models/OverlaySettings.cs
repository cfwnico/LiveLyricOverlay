using System.IO;
using System.Text.Json;

namespace LiveLyricOverlayApp.Models
{
    public class OverlaySettings
    {
        public string FontName { get; set; } = "Microsoft YaHei";
        public float FontSize { get; set; } = 44f;
        public float TranslationFontSize { get; set; } = 28f;
        public string TextColor { get; set; } = "#FFFFFF";
        public string TextGradientEndColor { get; set; } = "#FFFFFF";
        public string HighlightColor { get; set; } = "#FF1493";
        public string HighlightGradientEndColor { get; set; } = "#FF1493";
        public bool StrokeEnabled { get; set; } = true;
        public string StrokeColor { get; set; } = "#000000";
        public float StrokeWidth { get; set; } = 4f;
        public bool ShadowEnabled { get; set; } = true;
        public string ShadowColor { get; set; } = "#FF69B4";
        public float ShadowRadius { get; set; } = 8f;
        public float ShadowOffsetX { get; set; } = 0f;
        public float ShadowOffsetY { get; set; } = 0f;
        public int WindowX { get; set; } = 100;
        public int WindowY { get; set; } = 100;
        public int WindowWidth { get; set; } = 800;
        public int WindowHeight { get; set; } = 200;
        public bool KaraokeEnabled { get; set; } = true;
        public string Language { get; set; } = "zh-CN";

        public static OverlaySettings Load(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var settings = JsonSerializer.Deserialize<OverlaySettings>(json);
                    if (settings != null) return settings;
                }
            }
            catch { }

            var defaultSettings = new OverlaySettings();
            defaultSettings.Save(path);
            return defaultSettings;
        }

        public void Save(string path)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = true };
                string json = JsonSerializer.Serialize(this, options);
                File.WriteAllText(path, json);
            }
            catch { }
        }
    }
}

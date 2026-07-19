using System.Collections.Generic;

namespace LiveLyricOverlayApp.Models
{
    public class LyricWord
    {
        public int TimeMs { get; set; }
        public int DurationMs { get; set; }
        public string Text { get; set; } = string.Empty;
    }

    public class LyricLine
    {
        public int TimeMs { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Translation { get; set; } = string.Empty;
        public List<LyricWord> Words { get; set; } = new List<LyricWord>();
    }

    public class LyricDocument
    {
        public int OffsetMs { get; set; } = 0;
        public bool IsBilingual { get; set; } = false;
        public List<LyricLine> Lines { get; set; } = new List<LyricLine>();
    }
}

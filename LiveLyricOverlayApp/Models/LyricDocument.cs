using System.Collections.Generic;

namespace LiveLyricOverlayApp.Models
{
    public class LyricLine
    {
        public int TimeMs { get; set; }
        public string Text { get; set; } = string.Empty;
        public string Translation { get; set; } = string.Empty;
    }

    public class LyricDocument
    {
        public int OffsetMs { get; set; } = 0;
        public List<LyricLine> Lines { get; set; } = new List<LyricLine>();
    }
}

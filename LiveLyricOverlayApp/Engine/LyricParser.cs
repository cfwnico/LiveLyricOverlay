using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using LiveLyricOverlayApp.Models;

namespace LiveLyricOverlayApp.Engine
{
    public class LyricParser
    {
        private static readonly Regex TimeTagRegex = new Regex(@"\[(\d{2,}):(\d{2})(?:[.:](\d{2,3}))?\]", RegexOptions.Compiled);
        private static readonly Regex OffsetRegex = new Regex(@"\[offset:\s*([+-]?\d+)\s*\]", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        public static LyricDocument Parse(string filePath)
        {
            var document = new LyricDocument();
            
            // Normalize foobar2000 path format:
            // - URL-decode (%20 → space, %E4%B8%AD → 中, etc.)
            // - Convert forward slashes to backslashes for Windows
            filePath = Uri.UnescapeDataString(filePath).Replace('/', '\\');

            // Register provider for GBK
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            
            string content;
            byte[] bytes = File.ReadAllBytes(filePath);
            if (IsUtf8(bytes))
            {
                content = Encoding.UTF8.GetString(bytes);
            }
            else
            {
                // Fallback to GBK/System Default
                content = Encoding.GetEncoding("GBK").GetString(bytes);
            }

            var lines = content.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var rawLines = new List<LyricLine>();

            foreach (var line in lines)
            {
                if (string.IsNullOrWhiteSpace(line)) continue;

                var offsetMatch = OffsetRegex.Match(line);
                if (offsetMatch.Success)
                {
                    if (int.TryParse(offsetMatch.Groups[1].Value, out int offset))
                    {
                        document.OffsetMs = offset;
                    }
                    continue;
                }

                var timeMatches = TimeTagRegex.Matches(line);
                if (timeMatches.Count > 0)
                {
                    // Extract lyrics text (everything after the last time tag)
                    string text = line.Substring(timeMatches[timeMatches.Count - 1].Index + timeMatches[timeMatches.Count - 1].Length).Trim();
                    
                    if (string.IsNullOrEmpty(text)) continue;

                    foreach (Match match in timeMatches)
                    {
                        int min = int.Parse(match.Groups[1].Value);
                        int sec = int.Parse(match.Groups[2].Value);
                        int ms = 0;
                        if (match.Groups[3].Success)
                        {
                            string msStr = match.Groups[3].Value;
                            if (msStr.Length == 2) ms = int.Parse(msStr) * 10;
                            else if (msStr.Length == 3) ms = int.Parse(msStr);
                            else if (msStr.Length == 1) ms = int.Parse(msStr) * 100;
                        }
                        
                        int timeMs = min * 60 * 1000 + sec * 1000 + ms;
                        rawLines.Add(new LyricLine { TimeMs = timeMs, Text = text });
                    }
                }
            }

            rawLines = rawLines.OrderBy(l => l.TimeMs).ToList();

            // Merge translations
            var mergedLines = new List<LyricLine>();
            for (int i = 0; i < rawLines.Count; i++)
            {
                var current = rawLines[i];
                // If next line has identical/very close timestamp, treat as translation
                if (i < rawLines.Count - 1 && Math.Abs(rawLines[i + 1].TimeMs - current.TimeMs) <= 10)
                {
                    current.Translation = rawLines[i + 1].Text;
                    mergedLines.Add(current);
                    i++; // skip next line
                }
                else
                {
                    mergedLines.Add(current);
                }
            }

            document.Lines = mergedLines;
            return document;
        }

        private static bool IsUtf8(byte[] bytes)
        {
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF) return true; // UTF-8 BOM

            int i = 0;
            bool hasNonAscii = false;
            while (i < bytes.Length)
            {
                byte b = bytes[i];
                if (b <= 0x7F) { i++; continue; }

                hasNonAscii = true;
                if (b >= 0xC2 && b <= 0xDF)
                {
                    if (i + 1 >= bytes.Length || (bytes[i + 1] & 0xC0) != 0x80) return false;
                    i += 2;
                }
                else if (b >= 0xE0 && b <= 0xEF)
                {
                    if (i + 2 >= bytes.Length || (bytes[i + 1] & 0xC0) != 0x80 || (bytes[i + 2] & 0xC0) != 0x80) return false;
                    i += 3;
                }
                else if (b >= 0xF0 && b <= 0xF4)
                {
                    if (i + 3 >= bytes.Length || (bytes[i + 1] & 0xC0) != 0x80 || (bytes[i + 2] & 0xC0) != 0x80 || (bytes[i + 3] & 0xC0) != 0x80) return false;
                    i += 4;
                }
                else
                {
                    return false;
                }
            }
            // Pure ASCII is technically valid UTF-8 and GBK, but returning false/fallback to GBK won't break ASCII.
            return hasNonAscii || bytes.Length == 0;
        }
    }
}

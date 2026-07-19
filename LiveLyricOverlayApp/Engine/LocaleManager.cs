using System.Collections.Generic;

namespace LiveLyricOverlayApp.Engine
{
    public static class LocaleManager
    {
        public static string CurrentLanguage { get; set; } = "zh-CN";

        private static readonly Dictionary<string, Dictionary<string, string>> Translations = new Dictionary<string, Dictionary<string, string>>
        {
            ["zh-CN"] = new Dictionary<string, string>
            {
                ["MenuExit"] = "退出",
                ["MenuSettings"] = "编辑配置 (config.json)",
                ["MenuTitle"] = "桌面悬浮歌词",
                ["IpcConnecting"] = "正在连接到 named pipe 'LiveLyricOverlayPipe'...",
                ["IpcConnected"] = "已成功连接到 foobar2000 命名管道。",
                ["IpcDisconnected"] = "管道断开。连接丢失。",
                ["LrcLoaded"] = "已加载新歌词：{0}",
                ["LrcLoadFailed"] = "加载歌词失败：{0}",
                ["NoLyrics"] = "♪ 暂无歌词 (等待 IPC 通信...)",
                ["WaitingSong"] = "等待歌曲播放..."
            },
            ["en-US"] = new Dictionary<string, string>
            {
                ["MenuExit"] = "Exit",
                ["MenuSettings"] = "Edit Settings (config.json)",
                ["MenuTitle"] = "Desktop Lyric Overlay",
                ["IpcConnecting"] = "Connecting to named pipe 'LiveLyricOverlayPipe'...",
                ["IpcConnected"] = "Successfully connected to foobar2000 named pipe.",
                ["IpcDisconnected"] = "Pipe closed. Connection lost.",
                ["LrcLoaded"] = "Loaded new LRC: {0}",
                ["LrcLoadFailed"] = "Failed to load LRC: {0}",
                ["NoLyrics"] = "♪ No Lyrics (Waiting for IPC...)",
                ["WaitingSong"] = "Waiting for song to start..."
            }
        };

        public static string Get(string key)
        {
            string lang = CurrentLanguage;
            if (!Translations.ContainsKey(lang)) lang = "zh-CN";

            if (Translations[lang].TryGetValue(key, out string? val))
            {
                return val;
            }
            return key;
        }

        public static string Get(string key, params object[] args)
        {
            return string.Format(Get(key), args);
        }
    }
}

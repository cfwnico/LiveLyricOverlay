using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using SkiaSharp;
using LiveLyricOverlayApp.Engine;
using LiveLyricOverlayApp.Models;

namespace LiveLyricOverlayApp
{
    class Program
    {
        private static LayeredWindow? _window;
        private static System.Threading.Timer? _timer;
        private static Stopwatch _stopwatch = new Stopwatch();
        private static LyricDocument? _document;
        private static IpcClient? _ipcClient;
        private static double _baseTimeSeconds = 0;

        [STAThread]
        static void Main(string[] args)
        {
            Console.WriteLine("Starting LiveLyricOverlayApp...");

            int width = 800;
            int height = 200;
            
            _window = new LayeredWindow("LiveLyricOverlay", 100, 100, width, height);

            // Start IPC Client
            _ipcClient = new IpcClient();
            _ipcClient.OnPlaybackEvent += OnPlaybackEvent;
            _ipcClient.Start();

            _timer = new System.Threading.Timer(RenderCallback, null, 0, 16);
            _window.RunMessageLoop();

            _timer.Dispose();
            _ipcClient.Stop();
            _window.Dispose();
        }

        private static void OnPlaybackEvent(object? sender, PlaybackEvent e)
        {
            switch (e.EventType)
            {
                case "new_track":
                    try {
                        _document = LyricParser.Parse(e.FilePath);
                        _stopwatch.Reset();
                        _baseTimeSeconds = 0;
                        Console.WriteLine($"Loaded new LRC: {e.FilePath}");
                    } catch (Exception ex) {
                        _document = null;
                        Console.WriteLine("Failed to load LRC: " + ex.Message);
                    }
                    break;
                case "play":
                    _baseTimeSeconds = e.Time;
                    _stopwatch.Restart();
                    break;
                case "pause":
                    _baseTimeSeconds = e.Time;
                    _stopwatch.Stop();
                    break;
                case "stop":
                    _document = null;
                    _stopwatch.Reset();
                    break;
                case "seek":
                case "time":
                    _baseTimeSeconds = e.Time;
                    _stopwatch.Restart(); // Resync time accurately
                    break;
            }
        }

        private static void RenderCallback(object? state)
        {
            if (_window == null || _window.Handle == IntPtr.Zero) return;

            // current time in milliseconds = base time + stopwatch elapsed
            int currentMs = (int)(_baseTimeSeconds * 1000) + (int)_stopwatch.ElapsedMilliseconds;

            int width = _window.Width;
            int height = _window.Height;

            var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
            using var bitmap = new SKBitmap(info);
            using var canvas = new SKCanvas(bitmap);

            canvas.Clear(SKColors.Transparent);

            using var font = new SKFont(SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), 44);
            using var smallFont = new SKFont(SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright), 28);

            using var paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
            using var shadowPaint = new SKPaint
            {
                Color = SKColors.HotPink,
                IsAntialias = true,
                ImageFilter = SKImageFilter.CreateDropShadow(0, 0, 8, 8, SKColors.HotPink)
            };

            string text = "LiveLyricOverlay ♪";
            string translation = "";

            if (_document != null && _document.Lines.Count > 0)
            {
                var activeLine = _document.Lines.LastOrDefault(l => l.TimeMs <= currentMs);
                if (activeLine != null)
                {
                    text = activeLine.Text;
                    translation = activeLine.Translation;
                }
                else
                {
                    text = "Waiting for song to start...";
                }
            }
            else
            {
                text = "♪ No Lyrics (Waiting for IPC...)";
            }

            var bounds = new SKRect();
            font.MeasureText(text, out bounds, paint);
            float x = (width - bounds.Width) / 2 - bounds.Left;
            
            float y = (height - bounds.Height) / 2 - bounds.Top;
            if (!string.IsNullOrEmpty(translation)) y -= 15;

            canvas.DrawText(text, x, y, font, shadowPaint);
            canvas.DrawText(text, x, y, font, paint);

            if (!string.IsNullOrEmpty(translation))
            {
                var tBounds = new SKRect();
                smallFont.MeasureText(translation, out tBounds, paint);
                float tx = (width - tBounds.Width) / 2 - tBounds.Left;
                float ty = y + bounds.Height + 10;
                
                canvas.DrawText(translation, tx, ty, smallFont, shadowPaint);
                canvas.DrawText(translation, tx, ty, smallFont, paint);
            }

            _window.UpdatePixels(bitmap.GetPixels(), width, height);
        }
    }
}

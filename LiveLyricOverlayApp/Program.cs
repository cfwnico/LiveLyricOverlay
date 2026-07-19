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
        private static readonly object _stateLock = new object();

        // Shared state — all access must be under _stateLock
        private static Stopwatch _stopwatch = new Stopwatch();
        private static LyricDocument? _document;
        private static double _baseTimeSeconds = 0;

        private static IpcClient? _ipcClient;
        private static int _rendering = 0; // reentrant guard for RenderCallback

        // Cached Skia resources — created once, reused every frame
        private static readonly SKTypeface s_typeface = SKTypeface.FromFamilyName("Microsoft YaHei", SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
        private static readonly SKFont s_font = new SKFont(s_typeface, 44);
        private static readonly SKFont s_smallFont = new SKFont(s_typeface, 28);
        private static readonly SKPaint s_paint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        private static readonly SKPaint s_shadowPaint = new SKPaint
        {
            Color = SKColors.HotPink,
            IsAntialias = true,
            ImageFilter = SKImageFilter.CreateDropShadow(0, 0, 8, 8, SKColors.HotPink)
        };

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
            lock (_stateLock)
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
        }

        private static void RenderCallback(object? state)
        {
            // Reentrant guard: skip if a previous callback is still running
            if (Interlocked.CompareExchange(ref _rendering, 1, 0) != 0) return;
            try
            {
                if (_window == null || _window.Handle == IntPtr.Zero) return;

                // Snapshot shared state under lock
                LyricDocument? doc;
                int currentMs;
                lock (_stateLock)
                {
                    doc = _document;
                    currentMs = (int)(_baseTimeSeconds * 1000) + (int)_stopwatch.ElapsedMilliseconds;
                }

                int width = _window.Width;
                int height = _window.Height;

                var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var bitmap = new SKBitmap(info);
                using var canvas = new SKCanvas(bitmap);

                canvas.Clear(SKColors.Transparent);

                string text = "LiveLyricOverlay ♪";
                string translation = "";

                if (doc != null && doc.Lines.Count > 0)
                {
                    var activeLine = doc.Lines.LastOrDefault(l => l.TimeMs <= currentMs);
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
                s_font.MeasureText(text, out bounds, s_paint);
                float x = (width - bounds.Width) / 2 - bounds.Left;
                
                float y = (height - bounds.Height) / 2 - bounds.Top;
                if (!string.IsNullOrEmpty(translation)) y -= 15;

                canvas.DrawText(text, x, y, s_font, s_shadowPaint);
                canvas.DrawText(text, x, y, s_font, s_paint);

                if (!string.IsNullOrEmpty(translation))
                {
                    var tBounds = new SKRect();
                    s_smallFont.MeasureText(translation, out tBounds, s_paint);
                    float tx = (width - tBounds.Width) / 2 - tBounds.Left;
                    float ty = y + bounds.Height + 10;
                    
                    canvas.DrawText(translation, tx, ty, s_smallFont, s_shadowPaint);
                    canvas.DrawText(translation, tx, ty, s_smallFont, s_paint);
                }

                _window.UpdatePixels(bitmap.GetPixels(), width, height);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Render error: " + ex.Message);
            }
            finally
            {
                Interlocked.Exchange(ref _rendering, 0);
            }
        }
    }
}


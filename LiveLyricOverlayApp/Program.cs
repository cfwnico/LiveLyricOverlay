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
        private static string _currentFileName = "";

        private static IpcClient? _ipcClient;
        private static int _rendering = 0; // reentrant guard for RenderCallback

        private static OverlaySettings _settings = new OverlaySettings();
        private static string _configPath = "config.json";
        private static FileSystemWatcher? _configWatcher;
        private static float _dpiScale = 1.0f;

        // Cached Skia resources — recreated only on configuration changes
        private static string? s_cachedFontName;
        private static float s_cachedFontSize;
        private static float s_cachedTranslationFontSize;
        private static SKTypeface? s_cachedTypeface;
        private static SKFont? s_cachedMainFont;
        private static SKFont? s_cachedSubFont;

        [STAThread]
        static void Main(string[] args)
        {
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            lock (_stateLock)
            {
                _settings = OverlaySettings.Load(_configPath);
                LocaleManager.CurrentLanguage = _settings.Language;
            }

            Console.WriteLine("Starting LiveLyricOverlayApp...");

            // Create layered window
            _window = new LayeredWindow("LiveLyricOverlay", _settings.WindowX, _settings.WindowY, _settings.WindowWidth, _settings.WindowHeight);
            
            // Adjust bounds based on DPI
            lock (_stateLock)
            {
                _dpiScale = _window.GetDpiScale();
                int scaledW = (int)(_settings.WindowWidth * _dpiScale);
                int scaledH = (int)(_settings.WindowHeight * _dpiScale);
                _window.SetBounds(_settings.WindowX, _settings.WindowY, scaledW, scaledH);
            }

            // Start config file watcher for hot-reloads
            _configWatcher = new FileSystemWatcher(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            _configWatcher.NotifyFilter = NotifyFilters.LastWrite;
            _configWatcher.Changed += (s, e) =>
            {
                Thread.Sleep(100); // Wait briefly for file write lock release
                lock (_stateLock)
                {
                    _settings = OverlaySettings.Load(_configPath);
                    LocaleManager.CurrentLanguage = _settings.Language;
                    Console.WriteLine("Overlay config hot-reloaded.");
                    if (_window != null)
                    {
                        _dpiScale = _window.GetDpiScale();
                        int w = (int)(_settings.WindowWidth * _dpiScale);
                        int h = (int)(_settings.WindowHeight * _dpiScale);
                        _window.SetBounds(_settings.WindowX, _settings.WindowY, w, h);
                    }
                }
            };
            _configWatcher.EnableRaisingEvents = true;

            // Start IPC Client
            _ipcClient = new IpcClient();
            _ipcClient.OnPlaybackEvent += OnPlaybackEvent;
            _ipcClient.Start();

            _timer = new System.Threading.Timer(RenderCallback, null, 0, 16);
            _window.RunMessageLoop();

            _timer.Dispose();
            _configWatcher.Dispose();
            _ipcClient.Stop();
            _window.Dispose();

            // Clean up cached Skia resources on exit
            s_cachedMainFont?.Dispose();
            s_cachedSubFont?.Dispose();
            s_cachedTypeface?.Dispose();
        }

        private static void OnPlaybackEvent(object? sender, PlaybackEvent e)
        {
            Console.WriteLine($"[Event Received] Type={e.EventType}, Time={e.Time:F3}s, IsPlaying={e.IsPlaying}, Path={e.FilePath}");
            lock (_stateLock)
            {
                switch (e.EventType)
                {
                    case "new_track":
                        try {
                            _document = LyricParser.Parse(e.FilePath);
                            Console.WriteLine(LocaleManager.Get("LrcLoaded", e.FilePath));
                        } catch (Exception ex) {
                            _document = null;
                            Console.WriteLine(LocaleManager.Get("LrcLoadFailed", ex.Message));
                        }
                        
                        if (!string.IsNullOrEmpty(e.FilePath))
                        {
                            _currentFileName = System.IO.Path.GetFileNameWithoutExtension(e.FilePath);
                        }
                        else
                        {
                            _currentFileName = "";
                        }
                        break;
                    case "stop":
                        _document = null;
                        _currentFileName = "";
                        break;
                }

                _baseTimeSeconds = e.Time;
                if (e.IsPlaying)
                {
                    _stopwatch.Restart();
                }
                else
                {
                    _stopwatch.Reset();
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
                OverlaySettings settings;
                float dpiScale;
                string currentFileName;
                lock (_stateLock)
                {
                    doc = _document;
                    currentMs = (int)(_baseTimeSeconds * 1000) + (int)_stopwatch.ElapsedMilliseconds;
                    settings = _settings;
                    dpiScale = _dpiScale;
                    currentFileName = _currentFileName;
                }

                int width = _window.Width;
                int height = _window.Height;

                var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Premul);
                using var bitmap = new SKBitmap(info);
                using var canvas = new SKCanvas(bitmap);

                canvas.Clear(SKColors.Transparent);

                string text = LocaleManager.Get("MenuTitle");
                string translation = "";
                bool isBilingual = false;

                if (doc != null && doc.Lines.Count > 0)
                {
                    isBilingual = doc.IsBilingual;
                    int adjustedMs = currentMs - doc.OffsetMs;
                    var activeLine = doc.Lines.LastOrDefault(l => l.TimeMs <= adjustedMs);
                    if (activeLine != null)
                    {
                        text = activeLine.Text;
                        translation = activeLine.Translation;
                    }
                    else
                    {
                        text = LocaleManager.Get("WaitingSong");
                    }
                }
                else
                {
                    if (!string.IsNullOrEmpty(currentFileName))
                    {
                        text = currentFileName;
                    }
                    else
                    {
                        text = LocaleManager.Get("NoLyrics");
                    }
                }

                // Dynamic font caching
                if (s_cachedTypeface == null || s_cachedFontName != settings.FontName)
                {
                    s_cachedFontName = settings.FontName;
                    s_cachedTypeface?.Dispose();
                    s_cachedTypeface = SKTypeface.FromFamilyName(s_cachedFontName, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
                }

                float targetMainSize = settings.FontSize * dpiScale;
                float targetSubSize = settings.TranslationFontSize * dpiScale;

                if (s_cachedMainFont == null || s_cachedFontSize != targetMainSize)
                {
                    s_cachedFontSize = targetMainSize;
                    s_cachedMainFont?.Dispose();
                    s_cachedMainFont = new SKFont(s_cachedTypeface, targetMainSize);
                }

                if (s_cachedSubFont == null || s_cachedTranslationFontSize != targetSubSize)
                {
                    s_cachedTranslationFontSize = targetSubSize;
                    s_cachedSubFont?.Dispose();
                    s_cachedSubFont = new SKFont(s_cachedTypeface, targetSubSize);
                }

                // Prepare paints
                using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
                using var strokePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };

                using var fillHighlightPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
                using var strokeHighlightPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };

                strokePaint.StrokeWidth = settings.StrokeWidth * dpiScale;
                strokeHighlightPaint.StrokeWidth = settings.StrokeWidth * dpiScale;

                // Apply drop shadow if enabled
                if (settings.ShadowEnabled)
                {
                    SKColor shadowCol = ParseColor(settings.ShadowColor, SKColors.HotPink);
                    float shadowRad = settings.ShadowRadius * dpiScale;
                    float shadowOx = settings.ShadowOffsetX * dpiScale;
                    float shadowOy = settings.ShadowOffsetY * dpiScale;
                    var shadowFilter = SKImageFilter.CreateDropShadow(shadowOx, shadowOy, shadowRad, shadowRad, shadowCol);
                    fillPaint.ImageFilter = shadowFilter;
                    strokePaint.ImageFilter = shadowFilter;
                    fillHighlightPaint.ImageFilter = shadowFilter;
                    strokeHighlightPaint.ImageFilter = shadowFilter;
                }

                // Measure content bounds
                var mainBounds = new SKRect();
                s_cachedMainFont.MeasureText(text, out mainBounds);
                var subBounds = new SKRect();

                float mainY = 0;
                float subY = 0;
                bool showTranslation = isBilingual && !string.IsNullOrEmpty(translation);

                if (showTranslation)
                {
                    s_cachedSubFont.MeasureText(translation, out subBounds);
                    float spacing = 12 * dpiScale;
                    float totalHeight = mainBounds.Height + subBounds.Height + spacing;
                    float startY = (height - totalHeight) / 2 + mainBounds.Height;
                    mainY = startY;
                    subY = startY + subBounds.Height + spacing;
                }
                else
                {
                    mainY = (height - mainBounds.Height) / 2 + mainBounds.Height;
                }

                // Calculate progress and active width
                float lineProgress = 0f;
                float activeWidth = 0f;
                if (doc != null && doc.Lines.Count > 0)
                {
                    int adjustedMs = currentMs - doc.OffsetMs;
                    var activeLine = doc.Lines.LastOrDefault(l => l.TimeMs <= adjustedMs);
                    if (activeLine != null)
                    {
                        if (activeLine.Words != null && activeLine.Words.Count > 0)
                        {
                            int currentWordIdx = -1;
                            for (int i = 0; i < activeLine.Words.Count; i++)
                            {
                                var w = activeLine.Words[i];
                                if (adjustedMs >= w.TimeMs && adjustedMs < w.TimeMs + w.DurationMs)
                                {
                                    currentWordIdx = i;
                                    break;
                                }
                            }

                            if (currentWordIdx != -1)
                            {
                                var beforeWords = activeLine.Words.Take(currentWordIdx);
                                string beforeText = string.Concat(beforeWords.Select(w => w.Text));
                                float beforeWidth = s_cachedMainFont.MeasureText(beforeText);

                                var currentWord = activeLine.Words[currentWordIdx];
                                float currentWordWidth = s_cachedMainFont.MeasureText(currentWord.Text);
                                float p = (float)(adjustedMs - currentWord.TimeMs) / currentWord.DurationMs;
                                p = Math.Clamp(p, 0f, 1f);

                                activeWidth = beforeWidth + (currentWordWidth * p);
                            }
                            else if (adjustedMs >= activeLine.Words.Last().TimeMs + activeLine.Words.Last().DurationMs)
                            {
                                activeWidth = mainBounds.Width;
                            }
                            else
                            {
                                activeWidth = 0;
                            }
                        }
                        else
                        {
                            // Linear sweep fallback
                            int idx = doc.Lines.IndexOf(activeLine);
                            int nextLineTimeMs = idx >= 0 && idx < doc.Lines.Count - 1 ? doc.Lines[idx + 1].TimeMs : activeLine.TimeMs + 5000;
                            int duration = Math.Max(1000, nextLineTimeMs - activeLine.TimeMs);
                            float p = (float)(adjustedMs - activeLine.TimeMs) / duration;
                            activeWidth = mainBounds.Width * Math.Clamp(p, 0f, 1f);
                        }
                        lineProgress = mainBounds.Width > 0 ? Math.Clamp(activeWidth / mainBounds.Width, 0f, 1f) : 0f;
                    }
                }

                float margin = 20 * dpiScale;
                float mainX;
                if (mainBounds.Width > width - margin * 2)
                {
                    float maxScroll = mainBounds.Width - width + margin * 2;
                    mainX = margin - (maxScroll * lineProgress) - mainBounds.Left;
                }
                else
                {
                    mainX = (width - mainBounds.Width) / 2 - mainBounds.Left;
                }

                // Apply gradient colors or solid colors
                SKShader? mainShader = null;
                SKShader? highlightShader = null;
                try
                {
                    SKColor textCol = ParseColor(settings.TextColor, SKColors.White);
                    SKColor textGradCol = ParseColor(settings.TextGradientEndColor, SKColors.White);
                    if (textCol != textGradCol)
                    {
                        mainShader = SKShader.CreateLinearGradient(
                            new SKPoint(0, mainY - mainBounds.Height),
                            new SKPoint(0, mainY),
                            new SKColor[] { textCol, textGradCol },
                            null,
                            SKShaderTileMode.Clamp);
                        fillPaint.Shader = mainShader;
                    }
                    else
                    {
                        fillPaint.Color = textCol;
                    }

                    SKColor highlightCol = ParseColor(settings.HighlightColor, SKColors.HotPink);
                    SKColor highlightGradCol = ParseColor(settings.HighlightGradientEndColor, SKColors.HotPink);
                    if (highlightCol != highlightGradCol)
                    {
                        highlightShader = SKShader.CreateLinearGradient(
                            new SKPoint(0, mainY - mainBounds.Height),
                            new SKPoint(0, mainY),
                            new SKColor[] { highlightCol, highlightGradCol },
                            null,
                            SKShaderTileMode.Clamp);
                        fillHighlightPaint.Shader = highlightShader;
                    }
                    else
                    {
                        fillHighlightPaint.Color = highlightCol;
                    }

                    SKColor strokeCol = ParseColor(settings.StrokeColor, SKColors.Black);
                    strokePaint.Color = strokeCol;
                    strokeHighlightPaint.Color = strokeCol;

                    // Draw Main Line Background
                    if (settings.StrokeEnabled)
                    {
                        canvas.DrawText(text, mainX, mainY, SKTextAlign.Left, s_cachedMainFont, strokePaint);
                    }
                    canvas.DrawText(text, mainX, mainY, SKTextAlign.Left, s_cachedMainFont, fillPaint);

                    // Draw Karaoke Highlight Overlay
                    if (settings.KaraokeEnabled && activeWidth > 0)
                    {
                        canvas.Save();
                        canvas.ClipRect(new SKRect(mainX, 0, mainX + activeWidth, height));
                        if (settings.StrokeEnabled)
                        {
                            canvas.DrawText(text, mainX, mainY, SKTextAlign.Left, s_cachedMainFont, strokeHighlightPaint);
                        }
                        canvas.DrawText(text, mainX, mainY, SKTextAlign.Left, s_cachedMainFont, fillHighlightPaint);
                        canvas.Restore();
                    }

                    // Draw Sub Line (Translation) if enabled
                    if (showTranslation)
                    {
                        float subX;
                        if (subBounds.Width > width - margin * 2)
                        {
                            float maxScroll = subBounds.Width - width + margin * 2;
                            subX = margin - (maxScroll * lineProgress) - subBounds.Left;
                        }
                        else
                        {
                            subX = (width - subBounds.Width) / 2 - subBounds.Left;
                        }

                        if (settings.StrokeEnabled)
                        {
                            canvas.DrawText(translation, subX, subY, SKTextAlign.Left, s_cachedSubFont, strokePaint);
                        }
                        canvas.DrawText(translation, subX, subY, SKTextAlign.Left, s_cachedSubFont, fillPaint);
                    }
                }
                finally
                {
                    mainShader?.Dispose();
                    highlightShader?.Dispose();
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

        private static SKColor ParseColor(string hex, SKColor fallback)
        {
            if (SKColor.TryParse(hex, out var color))
            {
                return color;
            }
            return fallback;
        }
    }
}


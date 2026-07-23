using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using LiveLyricOverlayApp.Models;
using SkiaSharp;
using SkiaSharp.Views.Desktop;

namespace LiveLyricOverlayConfig
{
    public partial class MainWindow : Window
    {
        private OverlaySettings _settings = new OverlaySettings();
        private string _configPath = "config.json";
        private bool _isLoaded = false;

        public MainWindow()
        {
            InitializeComponent();
            LocateConfigFile();
            LoadSettings();
            _isLoaded = true;
        }

        private void LocateConfigFile()
        {
            _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");
            if (!File.Exists(_configPath))
            {
                // Fallback for debugging in Visual Studio
                string debugPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\LiveLyricOverlayApp\bin\Release\net8.0\config.json"));
                if (File.Exists(debugPath))
                {
                    _configPath = debugPath;
                }
                else
                {
                    debugPath = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, @"..\..\..\..\LiveLyricOverlayApp\bin\Debug\net8.0\config.json"));
                    if (File.Exists(debugPath)) _configPath = debugPath;
                }
            }
        }

        private void LoadSettings()
        {
            _settings = OverlaySettings.Load(_configPath);
            UpdateUIFromSettings();
        }

        private void UpdateUIFromSettings()
        {
            txtFontName.Text = _settings.FontName;
            txtFontSize.Text = _settings.FontSize.ToString();
            txtSubFontSize.Text = _settings.TranslationFontSize.ToString();

            SetButtonColor(btnTextColor, _settings.TextColor);
            SetButtonColor(btnTextGradColor, _settings.TextGradientEndColor);
            SetButtonColor(btnHighlightColor, _settings.HighlightColor);
            SetButtonColor(btnHighlightGradColor, _settings.HighlightGradientEndColor);
            
            chkStroke.IsChecked = _settings.StrokeEnabled;
            SetButtonColor(btnStrokeColor, _settings.StrokeColor);
            txtStrokeWidth.Text = _settings.StrokeWidth.ToString();

            chkShadow.IsChecked = _settings.ShadowEnabled;
            SetButtonColor(btnShadowColor, _settings.ShadowColor);
            txtShadowRadius.Text = _settings.ShadowRadius.ToString();
            txtShadowX.Text = _settings.ShadowOffsetX.ToString();
            txtShadowY.Text = _settings.ShadowOffsetY.ToString();

            chkKaraoke.IsChecked = _settings.KaraokeEnabled;
            cmbLanguage.SelectedIndex = _settings.Language == "en-US" ? 1 : 0;

            txtWinW.Text = _settings.WindowWidth.ToString();
            txtWinH.Text = _settings.WindowHeight.ToString();
            txtWinX.Text = _settings.WindowX.ToString();
            txtWinY.Text = _settings.WindowY.ToString();

            skElement.InvalidateVisual();
        }

        private void UpdateSettingsFromUI()
        {
            if (!_isLoaded) return;

            _settings.FontName = txtFontName.Text;
            if (float.TryParse(txtFontSize.Text, out float fs)) _settings.FontSize = fs;
            if (float.TryParse(txtSubFontSize.Text, out float sfs)) _settings.TranslationFontSize = sfs;

            _settings.TextColor = GetButtonColorHex(btnTextColor);
            _settings.TextGradientEndColor = GetButtonColorHex(btnTextGradColor);
            _settings.HighlightColor = GetButtonColorHex(btnHighlightColor);
            _settings.HighlightGradientEndColor = GetButtonColorHex(btnHighlightGradColor);

            _settings.StrokeEnabled = chkStroke.IsChecked ?? false;
            _settings.StrokeColor = GetButtonColorHex(btnStrokeColor);
            if (float.TryParse(txtStrokeWidth.Text, out float sw)) _settings.StrokeWidth = sw;

            _settings.ShadowEnabled = chkShadow.IsChecked ?? false;
            _settings.ShadowColor = GetButtonColorHex(btnShadowColor);
            if (float.TryParse(txtShadowRadius.Text, out float sr)) _settings.ShadowRadius = sr;
            if (float.TryParse(txtShadowX.Text, out float sx)) _settings.ShadowOffsetX = sx;
            if (float.TryParse(txtShadowY.Text, out float sy)) _settings.ShadowOffsetY = sy;

            _settings.KaraokeEnabled = chkKaraoke.IsChecked ?? false;
            if (cmbLanguage.SelectedItem is ComboBoxItem cbi)
                _settings.Language = cbi.Tag.ToString() ?? "zh-CN";

            if (int.TryParse(txtWinW.Text, out int ww)) _settings.WindowWidth = ww;
            if (int.TryParse(txtWinH.Text, out int wh)) _settings.WindowHeight = wh;
            if (int.TryParse(txtWinX.Text, out int wx)) _settings.WindowX = wx;
            if (int.TryParse(txtWinY.Text, out int wy)) _settings.WindowY = wy;

            skElement.InvalidateVisual();
        }

        private void SetButtonColor(System.Windows.Controls.Button btn, string hex)
        {
            try
            {
                var col = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(hex);
                btn.Background = new SolidColorBrush(col);
            }
            catch { }
        }

        private string GetButtonColorHex(System.Windows.Controls.Button btn)
        {
            if (btn.Background is SolidColorBrush sb)
            {
                return sb.Color.ToString();
            }
            return "#FFFFFF";
        }

        private void Config_Changed(object sender, RoutedEventArgs e)
        {
            UpdateSettingsFromUI();
        }

        private void BtnColor_Click(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.Button btn)
            {
                using var dialog = new System.Windows.Forms.ColorDialog();
                dialog.FullOpen = true;
                if (btn.Background is SolidColorBrush sb)
                {
                    dialog.Color = System.Drawing.Color.FromArgb(sb.Color.A, sb.Color.R, sb.Color.G, sb.Color.B);
                }
                
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var c = dialog.Color;
                    btn.Background = new SolidColorBrush(System.Windows.Media.Color.FromArgb(c.A, c.R, c.G, c.B));
                    UpdateSettingsFromUI();
                }
            }
        }

        private void BtnFont_Click(object sender, RoutedEventArgs e)
        {
            using var dialog = new System.Windows.Forms.FontDialog();
            try
            {
                dialog.Font = new System.Drawing.Font(txtFontName.Text, 12);
            }
            catch { }

            if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                txtFontName.Text = dialog.Font.Name;
                UpdateSettingsFromUI();
            }
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            _settings = new OverlaySettings();
            UpdateUIFromSettings();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            UpdateSettingsFromUI();
            _settings.Save(_configPath);
        }

        private void BtnOk_Click(object sender, RoutedEventArgs e)
        {
            BtnApply_Click(sender, e);
            this.Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        private void OnPaintSurface(object sender, SkiaSharp.Views.Desktop.SKPaintSurfaceEventArgs e)
        {
            var canvas = e.Surface.Canvas;
            int width = e.Info.Width;
            int height = e.Info.Height;
            canvas.Clear(SKColors.Transparent);

            string text = "FB2K 桌面歌词";
            string translation = "Desktop Lyrics Preview";

            using var typeface = SKTypeface.FromFamilyName(_settings.FontName, SKFontStyleWeight.Bold, SKFontStyleWidth.Normal, SKFontStyleSlant.Upright);
            using var mainFont = new SKFont(typeface, _settings.FontSize);
            using var subFont = new SKFont(typeface, _settings.TranslationFontSize);

            using var fillPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            using var strokePaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };
            using var fillHighlightPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
            using var strokeHighlightPaint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Stroke };

            strokePaint.StrokeWidth = _settings.StrokeWidth;
            strokeHighlightPaint.StrokeWidth = _settings.StrokeWidth;

            if (_settings.ShadowEnabled)
            {
                SKColor shadowCol = ParseColor(_settings.ShadowColor, SKColors.HotPink);
                var shadowFilter = SKImageFilter.CreateDropShadow(_settings.ShadowOffsetX, _settings.ShadowOffsetY, _settings.ShadowRadius, _settings.ShadowRadius, shadowCol);
                fillPaint.ImageFilter = shadowFilter;
                strokePaint.ImageFilter = shadowFilter;
                fillHighlightPaint.ImageFilter = shadowFilter;
                strokeHighlightPaint.ImageFilter = shadowFilter;
            }

            var mainBounds = new SKRect();
            mainFont.MeasureText(text, out mainBounds);
            var subBounds = new SKRect();
            subFont.MeasureText(translation, out subBounds);

            float spacing = 12;
            float totalHeight = mainBounds.Height + subBounds.Height + spacing;
            float startY = (height - totalHeight) / 2 + mainBounds.Height;
            float mainY = startY;
            float subY = startY + subBounds.Height + spacing;

            float mainX = (width - mainBounds.Width) / 2 - mainBounds.Left;
            float subX = (width - subBounds.Width) / 2 - subBounds.Left;

            SKShader? mainShader = null;
            SKShader? highlightShader = null;
            try
            {
                SKColor textCol = ParseColor(_settings.TextColor, SKColors.White);
                SKColor textGradCol = ParseColor(_settings.TextGradientEndColor, SKColors.White);
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

                SKColor highlightCol = ParseColor(_settings.HighlightColor, SKColors.HotPink);
                SKColor highlightGradCol = ParseColor(_settings.HighlightGradientEndColor, SKColors.HotPink);
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

                SKColor strokeCol = ParseColor(_settings.StrokeColor, SKColors.Black);
                strokePaint.Color = strokeCol;
                strokeHighlightPaint.Color = strokeCol;

                // Draw Main Line
                if (_settings.StrokeEnabled) canvas.DrawText(text, mainX, mainY, SKTextAlign.Left, mainFont, strokePaint);
                canvas.DrawText(text, mainX, mainY, SKTextAlign.Left, mainFont, fillPaint);

                // Draw Highlight (Karaoke progress at ~55%)
                if (_settings.KaraokeEnabled)
                {
                    float activeWidth = mainBounds.Width * 0.55f;
                    canvas.Save();
                    canvas.ClipRect(new SKRect(mainX, 0, mainX + activeWidth, height));
                    if (_settings.StrokeEnabled) canvas.DrawText(text, mainX, mainY, SKTextAlign.Left, mainFont, strokeHighlightPaint);
                    canvas.DrawText(text, mainX, mainY, SKTextAlign.Left, mainFont, fillHighlightPaint);
                    canvas.Restore();
                }

                // Draw Sub Line
                if (_settings.StrokeEnabled) canvas.DrawText(translation, subX, subY, SKTextAlign.Left, subFont, strokePaint);
                canvas.DrawText(translation, subX, subY, SKTextAlign.Left, subFont, fillPaint);
            }
            finally
            {
                mainShader?.Dispose();
                highlightShader?.Dispose();
            }
        }

        private static SKColor ParseColor(string hex, SKColor fallback)
        {
            if (SKColor.TryParse(hex, out var color)) return color;
            return fallback;
        }
    }
}
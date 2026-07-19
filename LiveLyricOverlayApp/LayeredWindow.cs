using System;
using System.Runtime.InteropServices;

namespace LiveLyricOverlayApp
{
    public class LayeredWindow : IDisposable
    {
        private const string WindowClassName = "LiveLyricOverlayClass";
        private static bool _classRegistered = false;
        private readonly WndProcDelegate _wndProcDelegate; // prevent GC collection
        private IntPtr _hwnd;
        private IntPtr _hInstance;
        private int _width;
        private int _height;

        public IntPtr Handle => _hwnd;
        public int Width => _width;
        public int Height => _height;

        public LayeredWindow(string title, int x, int y, int width, int height)
        {
            _width = width;
            _height = height;

            // Register Window Class
            WNDCLASSEX wcex = new WNDCLASSEX();
            wcex.cbSize = (uint)Marshal.SizeOf(typeof(WNDCLASSEX));
            wcex.style = CS_HREDRAW | CS_VREDRAW;
            _wndProcDelegate = WndProc;
            wcex.lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_wndProcDelegate);
            wcex.cbClsExtra = 0;
            wcex.cbWndExtra = 0;
            _hInstance = GetModuleHandle(string.Empty);
            wcex.hInstance = _hInstance;
            wcex.hIcon = IntPtr.Zero;
            wcex.hCursor = LoadCursor(IntPtr.Zero, IDC_ARROW);
            wcex.hbrBackground = IntPtr.Zero; // No background brush, we paint everything
            wcex.lpszMenuName = string.Empty;
            wcex.lpszClassName = WindowClassName;
            wcex.hIconSm = IntPtr.Zero;

            if (!_classRegistered)
            {
                if (RegisterClassEx(ref wcex) != 0)
                    _classRegistered = true;
            }

            // Extended styles for a transparent, click-through, always-on-top window.
            // Removed WS_EX_TOOLWINDOW so it shows up in Taskbar and OBS/Bilibili window capture lists.
            uint exStyle = WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOPMOST;
            uint style = WS_POPUP;

            _hwnd = CreateWindowEx(
                exStyle,
                WindowClassName,
                title,
                style,
                x, y, width, height,
                IntPtr.Zero,
                IntPtr.Zero,
                wcex.hInstance,
                IntPtr.Zero
            );

            if (_hwnd == IntPtr.Zero)
            {
                throw new Exception("Failed to create layered window.");
            }

            ShowWindow(_hwnd, SW_SHOW);
        }

        public void Resize(int width, int height)
        {
            _width = width;
            _height = height;
            SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, width, height, SWP_NOMOVE | SWP_NOZORDER | SWP_NOACTIVATE);
        }

        public void SetBounds(int x, int y, int width, int height)
        {
            _width = width;
            _height = height;
            SetWindowPos(_hwnd, IntPtr.Zero, x, y, width, height, SWP_NOZORDER | SWP_NOACTIVATE);
        }

        public float GetDpiScale()
        {
            try
            {
                uint dpi = GetDpiForWindow(_hwnd);
                if (dpi > 0) return dpi / 96.0f;
            }
            catch (EntryPointNotFoundException)
            {
                IntPtr hdc = GetDC(IntPtr.Zero);
                int dpiX = GetDeviceCaps(hdc, LOGPIXELSX);
                ReleaseDC(IntPtr.Zero, hdc);
                if (dpiX > 0) return dpiX / 96.0f;
            }
            catch { }
            return 1.0f;
        }

        public void UpdatePixels(IntPtr bgraPixels, int width, int height)
        {
            IntPtr hdcScreen = GetDC(IntPtr.Zero);
            IntPtr hdcMemory = CreateCompatibleDC(hdcScreen);

            // Create a DIB section to hold the pixels
            BITMAPINFOHEADER bmi = new BITMAPINFOHEADER
            {
                biSize = (uint)Marshal.SizeOf(typeof(BITMAPINFOHEADER)),
                biWidth = width,
                biHeight = -height, // top-down
                biPlanes = 1,
                biBitCount = 32,
                biCompression = BI_RGB
            };

            IntPtr pBits;
            IntPtr hBitmap = CreateDIBSection(hdcMemory, ref bmi, DIB_RGB_COLORS, out pBits, IntPtr.Zero, 0);

            if (hBitmap == IntPtr.Zero || pBits == IntPtr.Zero)
            {
                // GDI resource exhaustion — skip this frame
                DeleteDC(hdcMemory);
                ReleaseDC(IntPtr.Zero, hdcScreen);
                return;
            }

            // Copy pixels from Skia surface (bgraPixels) to DIB section (pBits)
            if (bgraPixels != IntPtr.Zero)
            {
                unsafe
                {
                    Buffer.MemoryCopy(bgraPixels.ToPointer(), pBits.ToPointer(), width * height * 4, width * height * 4);
                }
            }

            IntPtr hOldBitmap = SelectObject(hdcMemory, hBitmap);

            POINT ptSrc = new POINT { X = 0, Y = 0 };
            SIZE size = new SIZE { CX = width, CY = height };
            POINT ptPos = new POINT { X = -1, Y = -1 }; // Don't change position

            BLENDFUNCTION blend = new BLENDFUNCTION
            {
                BlendOp = AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = AC_SRC_ALPHA
            };

            // Call UpdateLayeredWindow
            UpdateLayeredWindow(_hwnd, hdcScreen, IntPtr.Zero, ref size, hdcMemory, ref ptSrc, 0, ref blend, ULW_ALPHA);

            // Cleanup
            SelectObject(hdcMemory, hOldBitmap);
            DeleteObject(hBitmap);
            DeleteDC(hdcMemory);
            ReleaseDC(IntPtr.Zero, hdcScreen);
        }

        public void RunMessageLoop()
        {
            MSG msg;
            while (GetMessage(out msg, IntPtr.Zero, 0, 0) > 0)
            {
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }
        }

        private delegate IntPtr WndProcDelegate(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        private IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
        {
            if (msg == WM_DESTROY)
            {
                PostQuitMessage(0);
                return IntPtr.Zero;
            }
            return DefWindowProc(hWnd, msg, wParam, lParam);
        }

        public void Dispose()
        {
            if (_hwnd != IntPtr.Zero)
            {
                DestroyWindow(_hwnd);
                _hwnd = IntPtr.Zero;
            }
            if (_classRegistered)
            {
                UnregisterClass(WindowClassName, _hInstance);
                _classRegistered = false;
            }
        }

        // --- Win32 Interop ---

        [StructLayout(LayoutKind.Sequential)]
        struct POINT
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct SIZE
        {
            public int CX;
            public int CY;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        struct WNDCLASSEX
        {
            public uint cbSize;
            public uint style;
            public IntPtr lpfnWndProc;
            public int cbClsExtra;
            public int cbWndExtra;
            public IntPtr hInstance;
            public IntPtr hIcon;
            public IntPtr hCursor;
            public IntPtr hbrBackground;
            public string lpszMenuName;
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct BITMAPINFOHEADER
        {
            public uint biSize;
            public int biWidth;
            public int biHeight;
            public ushort biPlanes;
            public ushort biBitCount;
            public uint biCompression;
            public uint biSizeImage;
            public int biXPelsPerMeter;
            public int biYPelsPerMeter;
            public uint biClrUsed;
            public uint biClrImportant;
        }

        const uint WS_EX_LAYERED = 0x00080000;
        const uint WS_EX_TRANSPARENT = 0x00000020;
        const uint WS_EX_TOPMOST = 0x00000008;
        const uint WS_EX_TOOLWINDOW = 0x00000080;
        const uint WS_POPUP = 0x80000000;

        const uint CS_HREDRAW = 0x0002;
        const uint CS_VREDRAW = 0x0001;
        const int IDC_ARROW = 32512;
        const uint WM_DESTROY = 0x0002;

        const byte AC_SRC_OVER = 0x00;
        const byte AC_SRC_ALPHA = 0x01;
        const uint ULW_ALPHA = 0x00000002;

        const uint BI_RGB = 0;
        const uint DIB_RGB_COLORS = 0;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern IntPtr CreateWindowEx(
           uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle,
           int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
           IntPtr hInstance, IntPtr lpParam);

        [DllImport("user32.dll")]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        static extern bool DestroyWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        static extern IntPtr DefWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern void PostQuitMessage(int nExitCode);

        [DllImport("user32.dll")]
        static extern sbyte GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        static extern bool TranslateMessage([In] ref MSG lpMsg);

        [DllImport("user32.dll")]
        static extern IntPtr DispatchMessage([In] ref MSG lpMsg);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr ptDst, ref SIZE psize, IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);

        [DllImport("user32.dll", ExactSpelling = true, SetLastError = true)]
        static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport("user32.dll", ExactSpelling = true)]
        static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport("user32.dll")]
        static extern IntPtr LoadCursor(IntPtr hInstance, int lpCursorName);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        static extern bool DeleteDC(IntPtr hdc);

        [DllImport("gdi32.dll", ExactSpelling = true)]
        static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport("gdi32.dll", ExactSpelling = true, SetLastError = true)]
        static extern bool DeleteObject(IntPtr hObject);

        [DllImport("gdi32.dll")]
        static extern IntPtr CreateDIBSection(IntPtr hdc, [In] ref BITMAPINFOHEADER pbmi, uint pila, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("gdi32.dll")]
        static extern int GetDeviceCaps(IntPtr hdc, int nIndex);

        const int LOGPIXELSX = 88;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOZORDER = 0x0004;
        const uint SWP_NOACTIVATE = 0x0010;

        const int SW_SHOW = 5;
    }
}

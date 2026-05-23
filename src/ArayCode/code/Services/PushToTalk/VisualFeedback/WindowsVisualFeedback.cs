using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using ArayCode;

namespace ArayCode.VisualFeedback;

[SupportedOSPlatform("windows")]
internal sealed class WindowsVisualFeedback : IVisualFeedback
{
    private Thread? _uiThread;
    private IntPtr _hwnd = IntPtr.Zero;
    private volatile bool _disposed;
    private readonly ManualResetEvent _windowReady = new ManualResetEvent(false);
    private readonly AppConfig _config;
    private readonly int _dotSize;
    private readonly int _rimThickness;
    private int _screenWidth;
    private int _screenHeight;
    private int _dotX;
    private int _dotY;
    private readonly byte _alpha;
    private readonly byte _colorR;
    private readonly byte _colorG;
    private readonly byte _colorB;
    private readonly string _className = $"ArayCode_VisualFeedback_{Guid.NewGuid():N}";
    private static readonly User32.WndProc _staticWndProc = StaticWndProc;
    private static readonly IntPtr _staticWndProcPtr = Marshal.GetFunctionPointerForDelegate(_staticWndProc);
    private readonly VisualMode _visualMode;

    public WindowsVisualFeedback(AppConfig config)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _visualMode = config.VisualMode;
        _dotSize = Math.Max(1, config.VisualFeedbackSize);
        _rimThickness = Math.Max(0, config.VisualFeedbackRimThickness);
        var (r, g, b) = ParseColor(config.VisualFeedbackColor);
        _colorR = r;
        _colorG = g;
        _colorB = b;
        _alpha = (byte)(Math.Clamp(config.VisualFeedbackOpacity, 0.0, 1.0) * 255);
        _uiThread = new Thread(WindowThread)
        {
            IsBackground = true,
            Name = "VisualFeedbackUI"
        };
        _uiThread.SetApartmentState(ApartmentState.STA);
        _uiThread.Start();
        _windowReady.WaitOne(5000); // Wait up to 5 seconds for window creation
    }

    private static (byte r, byte g, byte b) ParseColor(string hexColor)
    {
        // Remove leading # if present
        hexColor = hexColor.TrimStart('#');
        if (hexColor.Length != 6)
            throw new ArgumentException("Color must be in format #RRGGBB or RRGGBB");
        // Parse RR GG BB
        byte r = (byte)int.Parse(hexColor.Substring(0, 2), NumberStyles.HexNumber);
        byte g = (byte)int.Parse(hexColor.Substring(2, 2), NumberStyles.HexNumber);
        byte b = (byte)int.Parse(hexColor.Substring(4, 2), NumberStyles.HexNumber);
        return (r, g, b);
    }

    public void Show()
    {
        if (_disposed || _hwnd == IntPtr.Zero) return;
        User32.ShowWindow(_hwnd, User32.SW_SHOWNOACTIVATE);
        User32.UpdateWindow(_hwnd);
    }

    public void Hide()
    {
        if (_disposed || _hwnd == IntPtr.Zero) return;
        User32.ShowWindow(_hwnd, User32.SW_HIDE);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_hwnd != IntPtr.Zero)
        {
            User32.PostMessage(_hwnd, User32.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            _uiThread?.Join(1000);
            _hwnd = IntPtr.Zero;
        }
        _windowReady?.Dispose();
        _uiThread = null;
    }

    private void WindowThread()
    {
        // Register window class
        var wc = new User32.WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<User32.WNDCLASSEX>(),
            style = 0,
            lpfnWndProc = _staticWndProcPtr,
            cbClsExtra = 0,
            cbWndExtra = 0,
            hInstance = Kernel32.GetModuleHandle(null),
            hIcon = IntPtr.Zero,
            hCursor = IntPtr.Zero,
            hbrBackground = IntPtr.Zero,
            lpszMenuName = null,
            lpszClassName = _className,
            hIconSm = IntPtr.Zero
        };

        ushort atom = User32.RegisterClassEx(ref wc);
        if (atom == 0)
            throw new InvalidOperationException($"Failed to register window class (Win32 error: {Marshal.GetLastWin32Error()})");

        // Get primary monitor dimensions
        _screenWidth = User32.GetSystemMetrics(User32.SM_CXSCREEN);
        _screenHeight = User32.GetSystemMetrics(User32.SM_CYSCREEN);

        // Compute dot position based on config
        (int x, int y) GetDotPosition(int width, int height, int size)
        {
            const int margin = 10;
            switch (_config.VisualFeedbackPosition)
            {
                case "TopLeft":
                    return (margin, margin);
                case "TopRight":
                    return (width - size - margin, margin);
                case "BottomLeft":
                    return (margin, height - size - margin);
                case "BottomRight":
                    return (width - size - margin, height - size - margin);
                default:
                    return (width - size - margin, margin);
            }
        }

        var (dotX, dotY) = GetDotPosition(_screenWidth, _screenHeight, _dotSize);
        _dotX = dotX;
        _dotY = dotY;

        // Create fullscreen layered window
        _hwnd = User32.CreateWindowEx(
            User32.WS_EX_TOPMOST | User32.WS_EX_TOOLWINDOW | User32.WS_EX_LAYERED | User32.WS_EX_NOACTIVATE,
            _className,
            "",
            User32.WS_POPUP,
            0, 0, _screenWidth, _screenHeight,
            IntPtr.Zero, IntPtr.Zero, wc.hInstance, IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
            throw new InvalidOperationException($"Failed to create window (Win32 error: {Marshal.GetLastWin32Error()})");

        // Store this instance in window's user data
        GCHandle handle = GCHandle.Alloc(this);
        User32.SetWindowLongPtr(_hwnd, User32.GWLP_USERDATA, GCHandle.ToIntPtr(handle));

        UpdateLayeredWindowWithBitmap();

        _windowReady.Set();

        // Message loop
        while (User32.GetMessage(out var msg, IntPtr.Zero, 0, 0) != 0)
        {
            User32.TranslateMessage(ref msg);
            User32.DispatchMessage(ref msg);
        }

        // Cleanup
        User32.UnregisterClass(_className, wc.hInstance);
    }

    private static IntPtr StaticWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        // Retrieve instance from user data
        IntPtr ptr = User32.GetWindowLongPtr(hWnd, User32.GWLP_USERDATA);
        if (ptr != IntPtr.Zero)
        {
            GCHandle handle = GCHandle.FromIntPtr(ptr);
            if (handle.Target is WindowsVisualFeedback instance)
                return instance.InstanceWndProc(hWnd, msg, wParam, lParam);
        }
        return User32.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private IntPtr InstanceWndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam)
    {
        switch (msg)
        {
            case User32.WM_ERASEBKGND:
                return (IntPtr)1; // We handle background ourselves
            case User32.WM_DESTROY:
                // Clean up GCHandle
                IntPtr ptr = User32.GetWindowLongPtr(hWnd, User32.GWLP_USERDATA);
                if (ptr != IntPtr.Zero)
                {
                    GCHandle.FromIntPtr(ptr).Free();
                    User32.SetWindowLongPtr(hWnd, User32.GWLP_USERDATA, IntPtr.Zero);
                }
                User32.PostQuitMessage(0);
                break;
        }
        return User32.DefWindowProc(hWnd, msg, wParam, lParam);
    }

    private void UpdateLayeredWindowWithBitmap()
    {
        if (_hwnd == IntPtr.Zero) return;

        using var bitmap = new Bitmap(_screenWidth, _screenHeight, PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        graphics.CompositingQuality = CompositingQuality.HighQuality;

        var color = Color.FromArgb(_alpha, _colorR, _colorG, _colorB);

        // Draw rim on all 4 edges
        if (_rimThickness > 0)
        {
            using var pen = new Pen(color, _rimThickness);
            // Top edge
            graphics.DrawLine(pen, 0, _rimThickness / 2, _screenWidth, _rimThickness / 2);
            // Bottom edge
            graphics.DrawLine(pen, 0, _screenHeight - _rimThickness / 2 - 1, _screenWidth, _screenHeight - _rimThickness / 2 - 1);
            // Left edge
            graphics.DrawLine(pen, _rimThickness / 2, 0, _rimThickness / 2, _screenHeight);
            // Right edge
            graphics.DrawLine(pen, _screenWidth - _rimThickness / 2 - 1, 0, _screenWidth - _rimThickness / 2 - 1, _screenHeight);
        }

        // Draw dot at configured position
        switch (_visualMode)
        {
            case VisualMode.SolidDot:
                using (var brush = new SolidBrush(color))
                {
                    graphics.FillEllipse(brush, _dotX, _dotY, _dotSize, _dotSize);
                }
                break;
            case VisualMode.GlowDot:
                var path = new GraphicsPath();
                path.AddEllipse(_dotX, _dotY, _dotSize, _dotSize);
                using (var pathBrush = new PathGradientBrush(path))
                {
                    pathBrush.CenterColor = color;
                    pathBrush.SurroundColors = new[] { Color.FromArgb(0, _colorR, _colorG, _colorB) };
                    graphics.FillEllipse(pathBrush, _dotX, _dotY, _dotSize, _dotSize);
                }
                break;
        }

        // Upload to layered window
        IntPtr hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
        IntPtr hdcScreen = Gdi32.CreateCompatibleDC(IntPtr.Zero);
        IntPtr hOldBitmap = Gdi32.SelectObject(hdcScreen, hBitmap);

        try
        {
            var blend = new User32.BLENDFUNCTION
            {
                BlendOp = User32.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = User32.AC_SRC_ALPHA
            };
            var srcPos = new User32.POINT { x = 0, y = 0 };
            var dstPos = new User32.POINT { x = 0, y = 0 };
            var winSize = new User32.SIZE { cx = _screenWidth, cy = _screenHeight };
            User32.UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref dstPos, ref winSize,
                hdcScreen, ref srcPos, 0, ref blend, User32.ULW_ALPHA);
        }
        finally
        {
            Gdi32.SelectObject(hdcScreen, hOldBitmap);
            Gdi32.DeleteObject(hBitmap);
            Gdi32.DeleteDC(hdcScreen);
        }
    }

    private static class User32
    {
        // Constants
        public const int WS_EX_TOPMOST = 0x00000008;
        public const int WS_EX_TOOLWINDOW = 0x00000080;
        public const int WS_EX_LAYERED = 0x00080000;
        public const int WS_EX_NOACTIVATE = 0x08000000;
        public const int WS_POPUP = unchecked((int)0x80000000);
        public const int SW_SHOWNOACTIVATE = 4;
        public const int SW_HIDE = 0;
        public const int WM_CLOSE = 0x0010;
        public const int WM_PAINT = 0x000F;
        public const int WM_ERASEBKGND = 0x0014;
        public const int WM_DESTROY = 0x0002;
        public const int SM_CXSCREEN = 0;
        public const int SM_CYSCREEN = 1;
        public const int GWLP_USERDATA = -21;
        public const int LWA_COLORKEY = 0x00000001;
        public const int LWA_ALPHA = 0x00000002;
        public const int ULW_ALPHA = 0x00000002;
        public const int AC_SRC_OVER = 0x00;
        public const int AC_SRC_ALPHA = 0x01;

        // Structs
        [StructLayout(LayoutKind.Sequential)]
        public struct WNDCLASSEX
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
            [MarshalAs(UnmanagedType.LPTStr)]
            public string? lpszMenuName;
            [MarshalAs(UnmanagedType.LPTStr)]
            public string lpszClassName;
            public IntPtr hIconSm;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct PAINTSTRUCT
        {
            public IntPtr hdc;
            public bool fErase;
            public RECT rcPaint;
            public bool fRestore;
            public bool fIncUpdate;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
            public byte[] rgbReserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int left, top, right, bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public POINT pt;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT
        {
            public int x, y;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SIZE
        {
            public int cx;
            public int cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BLENDFUNCTION
        {
            public byte BlendOp;
            public byte BlendFlags;
            public byte SourceConstantAlpha;
            public byte AlphaFormat;
        }

        // Delegates
        public delegate IntPtr WndProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

        // Functions
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);
        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr CreateWindowEx(int dwExStyle, string lpClassName, string lpWindowName,
            int dwStyle, int x, int y, int nWidth, int nHeight, IntPtr hWndParent, IntPtr hMenu,
            IntPtr hInstance, IntPtr lpParam);
        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
        [DllImport("user32.dll")]
        public static extern bool UpdateWindow(IntPtr hWnd);
        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int nIndex);
        [DllImport("user32.dll")]
        public static extern bool SetLayeredWindowAttributes(IntPtr hwnd, uint crKey, byte bAlpha, uint dwFlags);
        [DllImport("user32.dll")]
        public static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
        public static extern IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
        public static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex);
        [DllImport("user32.dll")]
        public static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);
        [DllImport("user32.dll")]
        public static extern bool TranslateMessage(ref MSG lpMsg);
        [DllImport("user32.dll")]
        public static extern IntPtr DispatchMessage(ref MSG lpMsg);
        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool UnregisterClass(string lpClassName, IntPtr hInstance);
        [DllImport("user32.dll")]
        public static extern IntPtr BeginPaint(IntPtr hWnd, out PAINTSTRUCT lpPaint);
        [DllImport("user32.dll")]
        public static extern bool EndPaint(IntPtr hWnd, ref PAINTSTRUCT lpPaint);
        [DllImport("user32.dll")]
        public static extern IntPtr DefWindowProc(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")]
        public static extern void PostQuitMessage(int nExitCode);

        // Layered window functions
        [DllImport("user32.dll")]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
            IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint dwFlags);
    }

    private static class Gdi32
    {
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateSolidBrush(int crColor);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteObject(IntPtr hObject);
        [DllImport("gdi32.dll")]
        public static extern IntPtr SelectObject(IntPtr hdc, IntPtr h);
        [DllImport("gdi32.dll")]
        public static extern bool Ellipse(IntPtr hdc, int left, int top, int right, int bottom);
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateEllipticRgn(int x1, int y1, int x2, int y2);
        [DllImport("gdi32.dll")]
        public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
        [DllImport("gdi32.dll")]
        public static extern bool DeleteDC(IntPtr hdc);
    }

    private static class Kernel32
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Auto)]
        public static extern IntPtr GetModuleHandle(string? lpModuleName);
    }
}
using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace CodexVoiceKey
{
    internal static class VoiceOverlay
    {
        private static OverlayForm form;

        public static void Initialize()
        {
            form = new OverlayForm();
            IntPtr unused = form.Handle;
        }

        public static void ShowRecording() { Dispatch(delegate { form.SetRecording(); }); }
        public static void ShowTranscribing() { Dispatch(delegate { form.SetTranscribing(); }); }
        public static void ShowSuccess(string message) { Dispatch(delegate { form.SetSuccess(message, 1050); }); }
        public static void ShowCopied(string message) { Dispatch(delegate { form.SetInfo(message, "已经留在剪贴板里", 1800); }); }
        public static void ShowInfo(string message, int milliseconds) { Dispatch(delegate { form.SetInfo(message, "正在处理上一句", milliseconds); }); }
        public static void ShowError(string message) { Dispatch(delegate { form.SetError(message, 2400); }); }

        private static void Dispatch(Action action)
        {
            if (form == null || !form.IsHandleCreated) return;
            try
            {
                if (form.InvokeRequired) form.BeginInvoke(action);
                else action();
            }
            catch { }
        }

        private enum OverlayMode { Hidden, Recording, Transcribing, Success, Error, Info }

        private sealed class OverlayForm : Form
        {
            private const int OverlayWidth = 356;
            private const int OverlayHeight = 72;
            private const int WS_EX_TOPMOST = 0x00000008;
            private const int WS_EX_TOOLWINDOW = 0x00000080;
            private const int WS_EX_LAYERED = 0x00080000;
            private const int WS_EX_NOACTIVATE = 0x08000000;
            private const uint SWP_NOSIZE = 0x0001;
            private const uint SWP_NOMOVE = 0x0002;
            private const uint SWP_NOACTIVATE = 0x0010;
            private const uint SWP_SHOWWINDOW = 0x0040;
            private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

            private readonly Timer animationTimer;
            private readonly Stopwatch elapsed = new Stopwatch();
            private OverlayMode mode = OverlayMode.Hidden;
            private string title = String.Empty;
            private string subtitle = String.Empty;
            private long hideAt;
            private long lastRaisedAt;
            private float renderScale = 1f;
            private int frameWidth = OverlayWidth;
            private int frameHeight = OverlayHeight;
            private IntPtr anchorWindow;

            public OverlayForm()
            {
                AutoScaleMode = AutoScaleMode.None;
                BackColor = Color.FromArgb(251, 252, 250);
                ClientSize = new Size(OverlayWidth, OverlayHeight);
                DoubleBuffered = true;
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                TopMost = true;
                animationTimer = new Timer();
                animationTimer.Interval = 45;
                animationTimer.Tick += delegate
                {
                    if (hideAt != 0 && Stopwatch.GetTimestamp() >= hideAt)
                    {
                        HideOverlay();
                        return;
                    }
                    long now = Stopwatch.GetTimestamp();
                    if (now - lastRaisedAt >= Stopwatch.Frequency / 2)
                    {
                        PositionOnActiveScreen();
                        RaiseNoActivation();
                    }
                    RenderLayeredFrame();
                };
            }

            protected override bool ShowWithoutActivation { get { return true; } }

            protected override CreateParams CreateParams
            {
                get
                {
                    CreateParams cp = base.CreateParams;
                    cp.ExStyle |= WS_EX_TOPMOST | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_LAYERED;
                    return cp;
                }
            }

            public void SetRecording()
            {
                SelectCodexScreen();
                mode = OverlayMode.Recording;
                title = "正在听你说";
                subtitle = "松开 " + Program.Settings.Hotkey + " 结束";
                hideAt = 0;
                elapsed.Restart();
                Present();
            }

            public void SetTranscribing()
            {
                mode = OverlayMode.Transcribing;
                title = "正在整理文字";
                subtitle = "中文 / English · 本地识别";
                hideAt = 0;
                elapsed.Restart();
                Present();
            }

            public void SetSuccess(string message, int milliseconds)
            {
                mode = OverlayMode.Success;
                title = message;
                subtitle = "这一句完成了";
                hideAt = Stopwatch.GetTimestamp() + MillisecondsToTicks(milliseconds);
                elapsed.Restart();
                Present();
            }

            public void SetInfo(string message, string detail, int milliseconds)
            {
                mode = OverlayMode.Info;
                title = message;
                subtitle = detail;
                hideAt = Stopwatch.GetTimestamp() + MillisecondsToTicks(milliseconds);
                elapsed.Restart();
                Present();
            }

            public void SetError(string message, int milliseconds)
            {
                mode = OverlayMode.Error;
                title = message;
                subtitle = "调整一下再试";
                hideAt = Stopwatch.GetTimestamp() + MillisecondsToTicks(milliseconds);
                elapsed.Restart();
                Present();
            }

            private void Present()
            {
                PositionOnActiveScreen();
                RenderLayeredFrame();
                if (!Visible) Show();
                RaiseNoActivation();
                if (!animationTimer.Enabled) animationTimer.Start();
            }

            private void RaiseNoActivation()
            {
                if (!IsHandleCreated) return;
                lastRaisedAt = Stopwatch.GetTimestamp();
                SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                    SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
            }

            private void HideOverlay()
            {
                animationTimer.Stop();
                hideAt = 0;
                mode = OverlayMode.Hidden;
                Hide();
            }

            private void PositionOnActiveScreen()
            {
                if (anchorWindow == IntPtr.Zero || !IsWindow(anchorWindow)) SelectCodexScreen();
                Screen screen = Screen.FromHandle(anchorWindow != IntPtr.Zero ? anchorWindow : GetForegroundWindow());
                Rectangle area = screen.WorkingArea;
                uint dpiX, dpiY;
                NativePoint center = new NativePoint();
                center.X = screen.Bounds.Left + screen.Bounds.Width / 2;
                center.Y = screen.Bounds.Top + screen.Bounds.Height / 2;
                if (GetDpiForMonitor(MonitorFromPoint(center, 2), 0, out dpiX, out dpiY) == 0)
                    renderScale = dpiX / 96f;
                frameWidth = (int)Math.Round(OverlayWidth * renderScale);
                frameHeight = (int)Math.Round(OverlayHeight * renderScale);
                // Center on Codex's display, just above the Windows taskbar.
                int x = screen.Bounds.Left + (screen.Bounds.Width - frameWidth) / 2;
                int y = area.Bottom - frameHeight - (int)Math.Round(Program.Settings.TaskbarGap * renderScale);
                x = Math.Max(area.Left, Math.Min(x, area.Right - frameWidth));
                y = Math.Max(area.Top, Math.Min(y, area.Bottom - frameHeight));
                Bounds = new Rectangle(x, y, frameWidth, frameHeight);
            }

            private void SelectCodexScreen()
            {
                if (Program.IsCodexForeground())
                {
                    anchorWindow = GetForegroundWindow();
                    return;
                }
                if (anchorWindow != IntPtr.Zero && IsWindow(anchorWindow)) return;
                foreach (string processName in Program.Settings.TargetProcesses)
                foreach (Process process in Process.GetProcessesByName(System.IO.Path.GetFileNameWithoutExtension(processName)))
                {
                    using (process)
                    {
                        try
                        {
                            IntPtr window = process.MainWindowHandle;
                            if (window != IntPtr.Zero && IsWindowVisible(window))
                            {
                                anchorWindow = window;
                                return;
                            }
                        }
                        catch (InvalidOperationException) { }
                    }
                }
            }

            // A window Region clips rounded corners to whole pixels. Per-pixel alpha
            // keeps the white capsule, its border and its shadow smooth on any backdrop.
            private void RenderLayeredFrame()
            {
                using (Bitmap frame = new Bitmap(frameWidth, frameHeight, PixelFormat.Format32bppArgb))
                {
                    using (Graphics graphics = Graphics.FromImage(frame))
                    {
                        graphics.Clear(Color.Transparent);
                        graphics.ScaleTransform(renderScale, renderScale);
                        DrawFrame(graphics);
                    }
                    IntPtr screenDc = GetDC(IntPtr.Zero);
                    IntPtr memoryDc = CreateCompatibleDC(screenDc);
                    IntPtr bitmap = frame.GetHbitmap(Color.FromArgb(0));
                    IntPtr previous = SelectObject(memoryDc, bitmap);
                    try
                    {
                        NativeSize size = new NativeSize(frameWidth, frameHeight);
                        NativePoint source = new NativePoint();
                        BlendFunction blend = new BlendFunction();
                        blend.SourceConstantAlpha = 255;
                        blend.AlphaFormat = 1; // AC_SRC_ALPHA
                        if (!UpdateLayeredWindow(Handle, screenDc, IntPtr.Zero, ref size,
                            memoryDc, ref source, 0, ref blend, 2)) // ULW_ALPHA
                            throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
                    }
                    finally
                    {
                        SelectObject(memoryDc, previous);
                        DeleteObject(bitmap);
                        DeleteDC(memoryDc);
                        ReleaseDC(IntPtr.Zero, screenDc);
                    }
                }
            }

            protected override void OnPaintBackground(PaintEventArgs e) { }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                DrawFrame(e.Graphics);
            }

            private void DrawFrame(Graphics g)
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
                using (GraphicsPath shadow = Capsule(new Rectangle(1, 3, OverlayWidth - 3, OverlayHeight - 5)))
                using (SolidBrush shadowBrush = new SolidBrush(Color.FromArgb(18, 62, 86, 73)))
                    g.FillPath(shadowBrush, shadow);
                Rectangle outer = new Rectangle(1, 1, OverlayWidth - 3, OverlayHeight - 5);
                using (GraphicsPath path = Capsule(outer))
                using (LinearGradientBrush fill = new LinearGradientBrush(outer,
                    Color.FromArgb(255, 255, 253), Color.FromArgb(248, 251, 249),
                    LinearGradientMode.Vertical))
                using (Pen border = new Pen(Color.FromArgb(202, 216, 208), 0.8f))
                {
                    g.FillPath(fill, path);
                    g.DrawPath(border, path);
                }
                Color accent = GetAccentColor();
                using (SolidBrush pad = new SolidBrush(Color.FromArgb(235, 244, 239)))
                    g.FillEllipse(pad, 10, 4, 64, 64);
                PixelDoctor.Draw(g, new Rectangle(6, 4, 72, 64), GetMood(), elapsed.Elapsed.TotalMilliseconds);
                DrawText(g);
                DrawStatus(g, accent);
            }

            private void DrawText(Graphics g)
            {
                using (Font titleFont = new Font("Microsoft YaHei UI", 13.6f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (Font subtitleFont = new Font("Microsoft YaHei UI", 10.7f, FontStyle.Regular, GraphicsUnit.Pixel))
                using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(46, 61, 56)))
                using (SolidBrush subtitleBrush = new SolidBrush(Color.FromArgb(116, 130, 123)))
                using (StringFormat trim = new StringFormat())
                {
                    trim.Trimming = StringTrimming.EllipsisCharacter;
                    trim.FormatFlags = StringFormatFlags.NoWrap;
                    g.DrawString(title, titleFont, titleBrush, new RectangleF(84, 15, 217, 23), trim);
                    g.DrawString(subtitle, subtitleFont, subtitleBrush, new RectangleF(85, 39, 216, 18), trim);
                }
            }

            private void DrawStatus(Graphics g, Color accent)
            {
                using (SolidBrush tint = new SolidBrush(Color.FromArgb(17, accent.R, accent.G, accent.B)))
                    g.FillEllipse(tint, 305, 21, 30, 30);
                double seconds = elapsed.Elapsed.TotalSeconds;
                if (mode == OverlayMode.Recording)
                {
                    using (Pen wave = new Pen(accent, 2.2f))
                    {
                        wave.StartCap = LineCap.Round;
                        wave.EndCap = LineCap.Round;
                        for (int i = 0; i < 5; i++)
                        {
                            float h = (float)(3 + 8 * Math.Abs(Math.Sin(seconds * 4.2 + i * 0.85)));
                            float x = 312 + i * 4;
                            g.DrawLine(wave, x, 36 - h / 2, x, 36 + h / 2);
                        }
                    }
                }
                else if (mode == OverlayMode.Transcribing || mode == OverlayMode.Info)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        int alpha = 75 + (int)(180 * (0.5 + 0.5 * Math.Sin(seconds * 5 - i * 1.2)));
                        using (SolidBrush dot = new SolidBrush(Color.FromArgb(alpha, accent.R, accent.G, accent.B)))
                            g.FillEllipse(dot, 312 + i * 6, 34, 4, 4);
                    }
                }
                else if (mode == OverlayMode.Success)
                {
                    using (Pen tick = new Pen(accent, 2f))
                    {
                        tick.StartCap = LineCap.Round;
                        tick.EndCap = LineCap.Round;
                        tick.LineJoin = LineJoin.Round;
                        g.DrawLines(tick, new PointF[] {
                            new PointF(313, 36), new PointF(318, 40), new PointF(326, 32) });
                    }
                }
                else if (mode == OverlayMode.Error)
                {
                    using (Pen mark = new Pen(accent, 2f))
                    using (SolidBrush dot = new SolidBrush(accent))
                    {
                        mark.StartCap = LineCap.Round;
                        mark.EndCap = LineCap.Round;
                        g.DrawLine(mark, 320, 30, 320, 37);
                        g.FillEllipse(dot, 319, 40, 2, 2);
                    }
                }
            }

            private DoctorMood GetMood()
            {
                switch (mode)
                {
                    case OverlayMode.Recording: return DoctorMood.Listening;
                    case OverlayMode.Transcribing: return DoctorMood.Thinking;
                    case OverlayMode.Success: return DoctorMood.Happy;
                    case OverlayMode.Error: return DoctorMood.Oops;
                    default: return DoctorMood.Neutral;
                }
            }

            private Color GetAccentColor()
            {
                switch (mode)
                {
                    case OverlayMode.Recording: return Color.FromArgb(51, 150, 125);
                    case OverlayMode.Transcribing: return Color.FromArgb(84, 143, 184);
                    case OverlayMode.Success: return Color.FromArgb(73, 151, 104);
                    case OverlayMode.Error: return Color.FromArgb(200, 117, 101);
                    default: return Color.FromArgb(123, 139, 170);
                }
            }

            private static long MillisecondsToTicks(int milliseconds)
            {
                return (long)(milliseconds * (Stopwatch.Frequency / 1000.0));
            }

            private static GraphicsPath Capsule(Rectangle bounds)
            {
                int diameter = bounds.Height;
                GraphicsPath path = new GraphicsPath();
                path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 90, 180);
                path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 180);
                path.CloseFigure();
                return path;
            }

            [DllImport("user32.dll")]
            private static extern IntPtr GetForegroundWindow();
            [DllImport("user32.dll")]
            private static extern bool IsWindow(IntPtr window);
            [DllImport("user32.dll")]
            private static extern bool IsWindowVisible(IntPtr window);
            [DllImport("user32.dll")]
            private static extern IntPtr MonitorFromPoint(NativePoint point, uint flags);
            [DllImport("shcore.dll")]
            private static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);
            [DllImport("user32.dll")]
            private static extern bool SetWindowPos(IntPtr window, IntPtr insertAfter,
                int x, int y, int width, int height, uint flags);

            [StructLayout(LayoutKind.Sequential)]
            private struct NativePoint { public int X, Y; }
            [StructLayout(LayoutKind.Sequential)]
            private struct NativeSize
            {
                public int Width, Height;
                public NativeSize(int width, int height) { Width = width; Height = height; }
            }
            [StructLayout(LayoutKind.Sequential, Pack = 1)]
            private struct BlendFunction { public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat; }
            [DllImport("user32.dll", SetLastError = true)]
            private static extern bool UpdateLayeredWindow(IntPtr window, IntPtr destinationDc,
                IntPtr destinationPoint, ref NativeSize size, IntPtr sourceDc, ref NativePoint sourcePoint,
                uint colorKey, ref BlendFunction blend, uint flags);
            [DllImport("user32.dll")]
            private static extern IntPtr GetDC(IntPtr window);
            [DllImport("user32.dll")]
            private static extern int ReleaseDC(IntPtr window, IntPtr dc);
            [DllImport("gdi32.dll")]
            private static extern IntPtr CreateCompatibleDC(IntPtr dc);
            [DllImport("gdi32.dll")]
            private static extern bool DeleteDC(IntPtr dc);
            [DllImport("gdi32.dll")]
            private static extern IntPtr SelectObject(IntPtr dc, IntPtr item);
            [DllImport("gdi32.dll")]
            private static extern bool DeleteObject(IntPtr item);
        }
    }
}

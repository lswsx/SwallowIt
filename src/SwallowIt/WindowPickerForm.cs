using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Runtime.InteropServices;

namespace SwallowIt;

internal sealed class WindowPickerForm : Form
{
    private const int WS_EX_LAYERED = 0x00080000;

    private static readonly Color AccentColor = Color.FromArgb(13, 148, 136);
    private static readonly Color BadgeColor = Color.FromArgb(16, 24, 32);
    private static readonly Color DimColor = Color.FromArgb(122, 0, 0, 0);
    private static readonly Color HintBackColor = Color.FromArgb(250, 250, 250);
    private static readonly Color HintTextColor = Color.FromArgb(32, 40, 48);

    private readonly System.Windows.Forms.Timer _hoverTimer;

    private float _scale = 1f;
    private uint _dpi = 96;
    private Font? _badgeFont;
    private Font? _hintFont;
    private IntPtr _hoveredWindow;
    private Rectangle _hoveredBounds;
    private string _hoveredLabel = string.Empty;
    private Rectangle _hintArea;
    private Point _lastCursor;
    private bool _hasCursorSample;
    private bool _leftButtonDown;

    public WindowPickerForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        AutoScaleMode = AutoScaleMode.None;
        BackColor = Color.Black;
        Cursor = Cursors.Cross;
        KeyPreview = true;
        SetStyle(ControlStyles.Opaque | ControlStyles.OptimizedDoubleBuffer, true);

        Bounds = SystemInformation.VirtualScreen;
        _hintArea = new Rectangle(Point.Empty, Bounds.Size);

        _hoverTimer = new System.Windows.Forms.Timer
        {
            Interval = 35
        };
        _hoverTimer.Tick += (_, _) => UpdateHover(force: false);
    }

    public IntPtr SelectedWindow { get; private set; }

    protected override CreateParams CreateParams
    {
        get
        {
            var createParams = base.CreateParams;
            createParams.ExStyle |= WS_EX_LAYERED;
            return createParams;
        }
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);

        RecreateFonts();

        UpdateHover(force: true);
        _hoverTimer.Start();
    }

    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        base.OnDpiChanged(e);

        if (IsDisposed)
        {
            return;
        }

        Bounds = SystemInformation.VirtualScreen;
        UpdateHover(force: true);
    }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        Bounds = SystemInformation.VirtualScreen;
        RenderOverlay();
        Activate();
        Focus();
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _hoverTimer.Stop();
        _hoverTimer.Dispose();
        _badgeFont?.Dispose();
        _hintFont?.Dispose();
        base.OnFormClosed(e);
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        // The visible surface is owned by UpdateLayeredWindow.
    }

    protected override void OnPaint(PaintEventArgs e)
    {
    }

    protected override void OnMouseDown(MouseEventArgs e)
    {
        base.OnMouseDown(e);

        if (e.Button == MouseButtons.Right)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        if (e.Button != MouseButtons.Left)
        {
            return;
        }

        _leftButtonDown = true;
        Capture = true;
    }

    protected override void OnMouseUp(MouseEventArgs e)
    {
        base.OnMouseUp(e);

        if (e.Button != MouseButtons.Left || !_leftButtonDown)
        {
            return;
        }

        _leftButtonDown = false;
        Capture = false;
        UpdateHover(force: true);

        if (_hoveredWindow == IntPtr.Zero)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return;
        }

        SelectedWindow = _hoveredWindow;
        DialogResult = DialogResult.OK;
        Close();
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (keyData == Keys.Escape)
        {
            DialogResult = DialogResult.Cancel;
            Close();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void UpdateHover(bool force)
    {
        if (IsDisposed || !IsHandleCreated)
        {
            return;
        }

        var cursor = Cursor.Position;
        if (!force && _hasCursorSample && cursor == _lastCursor)
        {
            return;
        }

        _hasCursorSample = true;
        _lastCursor = cursor;

        var window = FindWindowUnderPoint(cursor);
        var bounds = window == IntPtr.Zero ? Rectangle.Empty : WindowTools.GetWindowBounds(window);
        var label = window == IntPtr.Zero ? string.Empty : DescribeWindow(window);
        var monitor = window == IntPtr.Zero
            ? NativeMethods.MonitorFromPoint(
                new NativeMethods.POINT { X = cursor.X, Y = cursor.Y },
                NativeMethods.MONITOR_DEFAULTTONEAREST)
            : NativeMethods.MonitorFromWindow(window, NativeMethods.MONITOR_DEFAULTTONEAREST);
        var dpi = GetMonitorDpi(monitor, window);
        var hintArea = GetMonitorArea(monitor);

        if (!force
            && window == _hoveredWindow
            && bounds == _hoveredBounds
            && label == _hoveredLabel
            && dpi == _dpi
            && hintArea == _hintArea)
        {
            return;
        }

        var dpiChanged = dpi != _dpi;

        _hoveredWindow = window;
        _hoveredBounds = bounds;
        _hoveredLabel = label;
        _hintArea = hintArea;
        _dpi = dpi;
        _scale = dpi / 96f;

        if (dpiChanged)
        {
            RecreateFonts();
        }

        RenderOverlay();
    }

    private static uint GetMonitorDpi(IntPtr monitor, IntPtr window)
    {
        if (monitor != IntPtr.Zero
            && NativeMethods.GetDpiForMonitor(
                monitor, NativeMethods.MDT_EFFECTIVE_DPI, out var monitorDpi, out _) == 0
            && monitorDpi > 0)
        {
            return monitorDpi;
        }

        if (window != IntPtr.Zero)
        {
            var windowDpi = NativeMethods.GetDpiForWindow(window);
            if (windowDpi > 0)
            {
                return windowDpi;
            }
        }

        var systemDpi = NativeMethods.GetDpiForSystem();
        return systemDpi > 0 ? systemDpi : 96u;
    }

    private Rectangle GetMonitorArea(IntPtr monitor)
    {
        if (monitor != IntPtr.Zero)
        {
            var info = new NativeMethods.MONITORINFO
            {
                cbSize = Marshal.SizeOf<NativeMethods.MONITORINFO>()
            };

            if (NativeMethods.GetMonitorInfo(monitor, ref info))
            {
                var work = info.rcWork;
                return new Rectangle(
                    work.Left - Left,
                    work.Top - Top,
                    work.Right - work.Left,
                    work.Bottom - work.Top);
            }
        }

        return new Rectangle(Point.Empty, Bounds.Size);
    }

    private void RecreateFonts()
    {
        _badgeFont?.Dispose();
        _hintFont?.Dispose();

        _badgeFont = new Font("Microsoft YaHei UI", 9.5F * _scale, FontStyle.Bold, GraphicsUnit.Point);
        _hintFont = new Font("Microsoft YaHei UI", 12F * _scale, FontStyle.Bold, GraphicsUnit.Point);
    }

    private IntPtr FindWindowUnderPoint(Point point)
    {
        var window = NativeMethods.GetTopWindow(IntPtr.Zero);
        while (window != IntPtr.Zero)
        {
            var next = NativeMethods.GetWindow(window, NativeMethods.GW_HWNDNEXT);

            if (IsCandidate(window))
            {
                var bounds = WindowTools.GetWindowBounds(window);
                if (!bounds.IsEmpty && bounds.Contains(point))
                {
                    var root = WindowTools.GetRootWindow(window);
                    if (root != IntPtr.Zero && root != Handle)
                    {
                        return root;
                    }
                }
            }

            window = next;
        }

        return IntPtr.Zero;
    }

    private bool IsCandidate(IntPtr window)
    {
        if (window == Handle
            || !NativeMethods.IsWindowVisible(window)
            || NativeMethods.IsIconic(window))
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == Environment.ProcessId)
        {
            return false;
        }

        return !WindowTools.IsWindowCloaked(window)
            && !WindowTools.IsShellWindow(WindowTools.GetClassName(window));
    }

    private static string DescribeWindow(IntPtr window)
    {
        var title = WindowTools.GetTitle(window).ReplaceLineEndings(" ").Trim();
        return string.IsNullOrWhiteSpace(title)
            ? WindowTools.GetProcessName(WindowTools.GetProcessId(window))
            : title;
    }

    private void RenderOverlay()
    {
        if (!IsHandleCreated || IsDisposed || Width <= 0 || Height <= 0)
        {
            return;
        }

        using var bitmap = new Bitmap(Width, Height, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            graphics.Clear(Color.Transparent);

            var hole = GetHoleBounds();
            DrawDimming(graphics, hole);

            if (!hole.IsEmpty)
            {
                DrawSelectionFrame(graphics, hole);
            }

            DrawHint(graphics);

            if (!hole.IsEmpty)
            {
                DrawWindowLabel(graphics, hole);
            }
        }

        UploadLayeredBitmap(bitmap);
    }

    private Rectangle GetHoleBounds()
    {
        if (_hoveredWindow == IntPtr.Zero || _hoveredBounds.IsEmpty)
        {
            return Rectangle.Empty;
        }

        var local = new Rectangle(
            _hoveredBounds.X - Left,
            _hoveredBounds.Y - Top,
            _hoveredBounds.Width,
            _hoveredBounds.Height);

        return Rectangle.Intersect(new Rectangle(0, 0, Width, Height), local);
    }

    private void DrawDimming(Graphics graphics, Rectangle hole)
    {
        using var brush = new SolidBrush(DimColor);

        if (hole.IsEmpty)
        {
            graphics.FillRectangle(brush, 0, 0, Width, Height);
            return;
        }

        graphics.FillRectangle(brush, 0, 0, Width, hole.Top);
        graphics.FillRectangle(brush, 0, hole.Bottom, Width, Height - hole.Bottom);
        graphics.FillRectangle(brush, 0, hole.Top, hole.Left, hole.Height);
        graphics.FillRectangle(brush, hole.Right, hole.Top, Width - hole.Right, hole.Height);

        // Fully transparent pixels in a layered window are click-through; keep the hole barely visible so it still receives input.
        using var holeBrush = new SolidBrush(Color.FromArgb(2, 0, 0, 0));
        graphics.FillRectangle(holeBrush, hole);
    }

    private void DrawSelectionFrame(Graphics graphics, Rectangle hole)
    {
        var stroke = Math.Max(3F, 4F * _scale);
        var frame = new Rectangle(hole.X, hole.Y, Math.Max(1, hole.Width - 1), Math.Max(1, hole.Height - 1));

        using var glow = new Pen(Color.FromArgb(80, AccentColor), stroke + 6F * _scale)
        {
            Alignment = PenAlignment.Center
        };
        graphics.DrawRectangle(glow, frame);

        using var pen = new Pen(AccentColor, stroke)
        {
            Alignment = PenAlignment.Inset
        };
        graphics.DrawRectangle(pen, frame);
    }

    private void DrawWindowLabel(Graphics graphics, Rectangle hole)
    {
        if (_badgeFont is null || string.IsNullOrEmpty(_hoveredLabel))
        {
            return;
        }

        var text = $"{_hoveredLabel}   {_hoveredBounds.Width} × {_hoveredBounds.Height}";
        var paddingX = 11F * _scale;
        var paddingY = 6F * _scale;
        var maxWidth = Math.Min(Width - 16F * _scale, 680F * _scale);
        var maxTextWidth = Math.Max(40F, maxWidth - paddingX * 2);

        if (graphics.MeasureString(text, _badgeFont).Width > maxTextWidth)
        {
            const string ellipsis = "…";
            while (text.Length > 1
                   && graphics.MeasureString(text + ellipsis, _badgeFont).Width > maxTextWidth)
            {
                text = text[..^1];
            }

            text += ellipsis;
        }

        var textSize = graphics.MeasureString(text, _badgeFont);
        var badgeWidth = Math.Min(maxWidth, textSize.Width + paddingX * 2);
        var badgeHeight = textSize.Height + paddingY * 2;
        var badgeX = Math.Clamp(hole.X, 8F * _scale, Math.Max(8F * _scale, Width - badgeWidth - 8F * _scale));
        var badgeY = hole.Y - badgeHeight - 8F * _scale;

        if (badgeY < 8F * _scale)
        {
            badgeY = hole.Y + 8F * _scale;
        }

        if (badgeY + badgeHeight > Height - 8F * _scale)
        {
            badgeY = Math.Max(8F * _scale, Height - badgeHeight - 8F * _scale);
        }

        var badgeBounds = new RectangleF(badgeX, badgeY, badgeWidth, badgeHeight);
        using var path = CreateRoundedRectangle(badgeBounds, 6F * _scale);
        using var background = new SolidBrush(BadgeColor);
        graphics.FillPath(background, path);

        using var accent = new SolidBrush(AccentColor);
        graphics.FillRectangle(accent, badgeX, badgeY + 5F * _scale, 3F * _scale, badgeHeight - 10F * _scale);

        using var textBrush = new SolidBrush(Color.White);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Near,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(
            text,
            _badgeFont,
            textBrush,
            new RectangleF(badgeX + paddingX, badgeY, badgeWidth - paddingX * 2, badgeHeight),
            format);
    }

    private void DrawHint(Graphics graphics)
    {
        if (_hintFont is null)
        {
            return;
        }

        const string hint = "点击选择要收纳的窗口    按 Esc 键或右键取消";
        var textSize = graphics.MeasureString(hint, _hintFont);
        var paddingX = 20F * _scale;
        var paddingY = 9F * _scale;
        var pillWidth = textSize.Width + paddingX * 2;
        var pillHeight = textSize.Height + paddingY * 2;
        var pill = new RectangleF(
            _hintArea.Left + (_hintArea.Width - pillWidth) / 2F,
            _hintArea.Bottom - pillHeight - 30F * _scale,
            pillWidth,
            pillHeight);

        using var path = CreateRoundedRectangle(pill, pillHeight / 2F);
        using var background = new SolidBrush(HintBackColor);
        graphics.FillPath(background, path);

        using var dotBrush = new SolidBrush(AccentColor);
        var dotSize = 8F * _scale;
        graphics.FillEllipse(
            dotBrush,
            pill.X + 14F * _scale,
            pill.Y + (pill.Height - dotSize) / 2F,
            dotSize,
            dotSize);

        using var textBrush = new SolidBrush(HintTextColor);
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
            FormatFlags = StringFormatFlags.NoWrap
        };
        graphics.DrawString(
            hint,
            _hintFont,
            textBrush,
            new RectangleF(pill.X + 18F * _scale, pill.Y, pill.Width - 28F * _scale, pill.Height),
            format);
    }

    private static GraphicsPath CreateRoundedRectangle(RectangleF bounds, float radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Min(radius * 2F, Math.Min(bounds.Width, bounds.Height));

        if (diameter <= 0F)
        {
            path.AddRectangle(bounds);
            return path;
        }

        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void UploadLayeredBitmap(Bitmap bitmap)
    {
        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        var memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        var hBitmap = bitmap.GetHbitmap(Color.FromArgb(0));
        var previousBitmap = NativeMethods.SelectObject(memoryDc, hBitmap);

        try
        {
            var destination = new NativeMethods.POINT
            {
                X = Left,
                Y = Top
            };
            var size = new NativeMethods.SIZE
            {
                Width = bitmap.Width,
                Height = bitmap.Height
            };
            var source = new NativeMethods.POINT();
            var blend = new NativeMethods.BLENDFUNCTION
            {
                BlendOp = NativeMethods.AC_SRC_OVER,
                BlendFlags = 0,
                SourceConstantAlpha = 255,
                AlphaFormat = NativeMethods.AC_SRC_ALPHA
            };

            NativeMethods.UpdateLayeredWindow(
                Handle,
                screenDc,
                ref destination,
                ref size,
                memoryDc,
                ref source,
                0,
                ref blend,
                NativeMethods.ULW_ALPHA);
        }
        finally
        {
            NativeMethods.SelectObject(memoryDc, previousBitmap);
            NativeMethods.DeleteObject(hBitmap);
            NativeMethods.DeleteDC(memoryDc);
            NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }
}

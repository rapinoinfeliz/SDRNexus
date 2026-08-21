using System.ComponentModel;
using System.Drawing.Drawing2D;

namespace DXNexus.SdrSharp.Plugin;

internal static class DxnexusTheme
{
    public static readonly Color Background = Color.FromArgb(18, 21, 24);
    public static readonly Color Card = Color.FromArgb(24, 29, 34);
    public static readonly Color CardRaised = Color.FromArgb(27, 35, 42);
    public static readonly Color Border = Color.FromArgb(63, 72, 79);
    public static readonly Color Accent = Color.FromArgb(72, 176, 230);
    public static readonly Color Teal = Color.FromArgb(85, 220, 199);
    public static readonly Color Text = Color.FromArgb(238, 242, 245);
    public static readonly Color Muted = Color.FromArgb(174, 183, 190);
    public static readonly Color Success = Color.FromArgb(79, 194, 105);
    public static readonly Color Warning = Color.FromArgb(255, 190, 92);
    public static readonly Color Error = Color.FromArgb(255, 124, 124);
    public static readonly Color Target = Color.FromArgb(255, 142, 166);

    public static Font UiFont(float size, FontStyle style = FontStyle.Regular) =>
        new("Segoe UI", size, style, GraphicsUnit.Point);
}

internal sealed class RoundedPanel : Panel
{
    private Color _borderColor = DxnexusTheme.Border;

    public RoundedPanel()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        BackColor = DxnexusTheme.Card;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public int CornerRadius { get; set; } = 7;

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color BorderColor
    {
        get => _borderColor;
        set
        {
            _borderColor = value;
            Invalidate();
        }
    }

    protected override void OnPaintBackground(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.Clear(Parent?.BackColor ?? DxnexusTheme.Background);
        using var path = CreateRoundedPath(ClientRectangle, CornerRadius);
        using var brush = new SolidBrush(BackColor);
        e.Graphics.FillPath(brush, path);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var bounds = Rectangle.Inflate(ClientRectangle, -1, -1);
        using var path = CreateRoundedPath(bounds, CornerRadius);
        using var pen = new Pen(BorderColor);
        e.Graphics.DrawPath(pen, path);
    }

    private static GraphicsPath CreateRoundedPath(Rectangle bounds, int radius)
    {
        var path = new GraphicsPath();
        var diameter = Math.Max(2, radius * 2);
        var arc = new Rectangle(bounds.Location, new Size(diameter, diameter));
        path.AddArc(arc, 180, 90);
        arc.X = bounds.Right - diameter;
        path.AddArc(arc, 270, 90);
        arc.Y = bounds.Bottom - diameter;
        path.AddArc(arc, 0, 90);
        arc.X = bounds.Left;
        path.AddArc(arc, 90, 90);
        path.CloseFigure();
        return path;
    }
}

internal sealed class StatusDot : Control
{
    private Color _dotColor = DxnexusTheme.Muted;

    public StatusDot()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Margin = new Padding(1, 5, 10, 0);
        Size = new Size(14, 14);
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color DotColor
    {
        get => _dotColor;
        set
        {
            _dotColor = value;
            Invalidate();
        }
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var glow = new SolidBrush(Color.FromArgb(55, DotColor));
        using var fill = new SolidBrush(DotColor);
        e.Graphics.FillEllipse(glow, 0, 0, Width - 1, Height - 1);
        e.Graphics.FillEllipse(fill, 2, 2, Width - 5, Height - 5);
    }
}

internal sealed class WaveformGlyph : Control
{
    public WaveformGlyph()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
        Size = new Size(42, 48);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        using var pen = new Pen(DxnexusTheme.Teal, 2.2f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round,
        };
        var middle = Height / 2f;
        var points = new[]
        {
            new PointF(2, middle), new PointF(8, middle), new PointF(12, middle - 7),
            new PointF(16, middle + 10), new PointF(21, middle - 17), new PointF(26, middle + 13),
            new PointF(31, middle - 5), new PointF(35, middle), new PointF(40, middle),
        };
        e.Graphics.DrawLines(pen, points);
    }
}

internal sealed class FrequencyDisplay : Control
{
    private readonly Font _frequencyFont = DxnexusTheme.UiFont(20, FontStyle.Bold);
    private readonly Font _modeFont = DxnexusTheme.UiFont(9.5f, FontStyle.Bold);
    private string _frequency = string.Empty;
    private string _mode = string.Empty;

    public FrequencyDisplay()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public void SetValues(string frequency, string mode)
    {
        _frequency = frequency;
        _mode = mode;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        TextRenderer.DrawText(
            e.Graphics,
            _frequency,
            _frequencyFont,
            new Rectangle(0, 0, Width, 39),
            DxnexusTheme.Text,
            TextFormatFlags.Left | TextFormatFlags.Bottom | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        TextRenderer.DrawText(
            e.Graphics,
            _mode,
            _modeFont,
            new Rectangle(1, 41, Math.Max(0, Width - 1), Math.Max(0, Height - 41)),
            DxnexusTheme.Teal,
            TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _frequencyFont.Dispose();
            _modeFont.Dispose();
        }
        base.Dispose(disposing);
    }
}

internal sealed class ModernButton : Button
{
    private Color _normalColor = DxnexusTheme.CardRaised;

    public ModernButton()
    {
        AutoSize = false;
        BackColor = _normalColor;
        Cursor = Cursors.Hand;
        FlatStyle = FlatStyle.Flat;
        FlatAppearance.BorderColor = DxnexusTheme.Border;
        FlatAppearance.BorderSize = 1;
        FlatAppearance.MouseDownBackColor = Color.FromArgb(35, 65, 82);
        FlatAppearance.MouseOverBackColor = Color.FromArgb(31, 50, 61);
        Font = DxnexusTheme.UiFont(9.5f);
        ForeColor = DxnexusTheme.Text;
        Height = 38;
        UseVisualStyleBackColor = false;
    }

    [Browsable(false)]
    [DesignerSerializationVisibility(DesignerSerializationVisibility.Hidden)]
    public Color NormalColor
    {
        get => _normalColor;
        set
        {
            _normalColor = value;
            BackColor = value;
        }
    }
}

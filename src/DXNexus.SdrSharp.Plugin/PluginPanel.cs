using SDRSharp.Common;

namespace DXNexus.SdrSharp.Plugin;

internal sealed class PluginPanel : UserControl
{
    private readonly ISharpControl _control;
    private readonly Label _frequencyLabel;

    public PluginPanel(ISharpControl control)
    {
        _control = control ?? throw new ArgumentNullException(nameof(control));
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(15, 31, 43);
        ForeColor = Color.WhiteSmoke;
        Padding = new Padding(12);
        MinimumSize = new Size(250, 150);

        var title = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = "DXNexus",
        };

        var status = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(105, 210, 255),
            Text = "Bridge not connected",
        };

        _frequencyLabel = new Label
        {
            AutoSize = true,
            Margin = new Padding(0, 18, 0, 0),
            Text = FormatFrequency(_control.Frequency),
        };

        var description = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(300, 0),
            ForeColor = Color.FromArgb(170, 185, 195),
            Text = "Local scaffold ready. Pairing and station candidates are implemented in later tasks.",
        };

        var layout = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
        };
        layout.Controls.Add(title);
        layout.Controls.Add(status);
        layout.Controls.Add(_frequencyLabel);
        layout.Controls.Add(description);
        Controls.Add(layout);
    }

    private static string FormatFrequency(long frequencyHz)
    {
        return frequencyHz >= 1_000_000
            ? $"{frequencyHz / 1_000_000d:0.000} MHz"
            : $"{frequencyHz / 1_000d:0.0} kHz";
    }
}


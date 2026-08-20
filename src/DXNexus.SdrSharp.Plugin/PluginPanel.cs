using DXNexus.Plugin.Core;

namespace DXNexus.SdrSharp.Plugin;

internal sealed class PluginPanel : UserControl
{
    private readonly RadioStateCollector _collector;
    private readonly Label _frequencyLabel;
    private readonly Label _modeLabel;

    public PluginPanel(RadioStateCollector collector)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
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
        };

        _modeLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(170, 185, 195),
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
        layout.Controls.Add(_modeLabel);
        layout.Controls.Add(description);
        Controls.Add(layout);

        _collector.SnapshotChanged += HandleSnapshotChanged;
        Render(_collector.CaptureFullSnapshot());
    }

    private void HandleSnapshotChanged(object? sender, SequencedRadioSnapshot snapshot)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => Render(snapshot));
            return;
        }

        Render(snapshot);
    }

    private void Render(SequencedRadioSnapshot snapshot)
    {
        _frequencyLabel.Text = FormatFrequency(snapshot.Radio.FrequencyHz);
        _modeLabel.Text = $"{snapshot.Radio.Detector.ToString().ToUpperInvariant()} · {snapshot.Radio.FilterBandwidthHz / 1_000d:0.#} kHz";
    }

    private static string FormatFrequency(long frequencyHz)
    {
        return frequencyHz >= 1_000_000
            ? $"{frequencyHz / 1_000_000d:0.000} MHz"
            : $"{frequencyHz / 1_000d:0.0} kHz";
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _collector.SnapshotChanged -= HandleSnapshotChanged;
        }

        base.Dispose(disposing);
    }
}


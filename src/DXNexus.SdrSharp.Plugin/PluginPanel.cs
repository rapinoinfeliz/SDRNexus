using DXNexus.Contracts;
using DXNexus.Plugin.Core;

namespace DXNexus.SdrSharp.Plugin;

internal sealed class PluginPanel : UserControl
{
    private readonly RadioStateCollector _collector;
    private readonly RadioStatePublisher _publisher;
    private readonly PipeRadioStateSink _sink;
    private readonly Label _statusLabel;
    private readonly Label _frequencyLabel;
    private readonly Label _modeLabel;
    private readonly Label _candidateStatusLabel;
    private readonly FlowLayoutPanel _candidateList;

    public PluginPanel(RadioStateCollector collector, RadioStatePublisher publisher, PipeRadioStateSink sink)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        AutoScaleMode = AutoScaleMode.Dpi;
        BackColor = Color.FromArgb(15, 31, 43);
        ForeColor = Color.WhiteSmoke;
        Padding = new Padding(12);
        MinimumSize = new Size(250, 260);

        var title = new Label
        {
            AutoSize = true,
            Font = new Font(Font, FontStyle.Bold),
            Text = "DXNexus",
        };

        _statusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(105, 210, 255),
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

        _candidateStatusLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(300, 0),
            ForeColor = Color.FromArgb(170, 185, 195),
            Margin = new Padding(0, 16, 0, 6),
            Text = "Tune to a broadcast frequency to see candidates.",
        };

        _candidateList = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = new Padding(0),
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
        layout.Controls.Add(_statusLabel);
        layout.Controls.Add(_frequencyLabel);
        layout.Controls.Add(_modeLabel);
        layout.Controls.Add(_candidateStatusLabel);
        layout.Controls.Add(_candidateList);
        Controls.Add(layout);

        _collector.SnapshotChanged += HandleSnapshotChanged;
        _publisher.ConnectionStateChanged += HandleConnectionStateChanged;
        _sink.CandidatesReceived += HandleCandidatesReceived;
        Render(_collector.CaptureFullSnapshot());
        RenderConnectionState(_publisher.State);
    }

    private void HandleCandidatesReceived(object? sender, CandidateContextResponse response)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(() => RenderCandidates(response));
            return;
        }
        RenderCandidates(response);
    }

    private void RenderCandidates(CandidateContextResponse response)
    {
        _candidateList.SuspendLayout();
        _candidateList.Controls.Clear();
        _candidateStatusLabel.Text = response.Candidates.Length == 0
            ? "No catalog candidate on this exact frequency."
            : $"{response.Candidates.Length} candidate{(response.Candidates.Length == 1 ? "" : "s")} · {response.Band}";
        foreach (var candidate in response.Candidates.Take(6))
        {
            var received = candidate.Received.AtListeningPoint ? " · heard here" : candidate.Wishlisted ? " · target" : "";
            var field = candidate.Estimate.FieldStrengthDbuvM is double value ? $" · {value:0} dBµV/m" : "";
            _candidateList.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(300, 0),
                Margin = new Padding(0, 0, 0, 7),
                Text = $"{candidate.StationName}\n{candidate.DistanceKm:0.#} km · {candidate.BearingDeg:0}° {candidate.BearingCardinal}{field}{received}",
                ForeColor = candidate.Received.AtListeningPoint
                    ? Color.FromArgb(84, 226, 159)
                    : Color.WhiteSmoke,
            });
        }
        _candidateList.ResumeLayout();
    }

    private void HandleConnectionStateChanged(object? sender, BridgeConnectionState state)
    {
        if (IsDisposed)
        {
            return;
        }

        if (InvokeRequired)
        {
            BeginInvoke(() => RenderConnectionState(state));
            return;
        }

        RenderConnectionState(state);
    }

    private void RenderConnectionState(BridgeConnectionState state)
    {
        _statusLabel.Text = state switch
        {
            BridgeConnectionState.Connected => "Bridge connected",
            BridgeConnectionState.Connecting => "Connecting to Bridge...",
            _ => "Bridge offline",
        };
        _statusLabel.ForeColor = state == BridgeConnectionState.Connected
            ? Color.FromArgb(84, 226, 159)
            : Color.FromArgb(105, 210, 255);
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
            _publisher.ConnectionStateChanged -= HandleConnectionStateChanged;
            _sink.CandidatesReceived -= HandleCandidatesReceived;
        }

        base.Dispose(disposing);
    }
}

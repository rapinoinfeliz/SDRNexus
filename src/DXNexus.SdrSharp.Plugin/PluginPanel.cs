using System.Diagnostics;
using DXNexus.Contracts;
using DXNexus.Plugin.Core;

namespace DXNexus.SdrSharp.Plugin;

internal sealed class PluginPanel : UserControl
{
    private readonly RadioStateCollector _collector;
    private readonly RadioStatePublisher _publisher;
    private readonly PipeRadioStateSink _sink;
    private readonly SpectrumOverlayController _spectrumOverlay;
    private readonly Label _statusLabel;
    private readonly Label _cloudStatusLabel;
    private readonly Label _liveStatusLabel;
    private readonly Button _remoteTuneButton;
    private readonly Label _frequencyLabel;
    private readonly Label _modeLabel;
    private readonly Label _candidateStatusLabel;
    private readonly FlowLayoutPanel _candidateList;
    private SequencedRadioSnapshot _latestSnapshot;

    public PluginPanel(RadioStateCollector collector, RadioStatePublisher publisher, PipeRadioStateSink sink, SpectrumOverlayController spectrumOverlay)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _spectrumOverlay = spectrumOverlay ?? throw new ArgumentNullException(nameof(spectrumOverlay));
        _latestSnapshot = _collector.CaptureFullSnapshot();
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

        var spectrumOverlayToggle = new CheckBox
        {
            AutoSize = true,
            Checked = _spectrumOverlay.Enabled,
            ForeColor = Color.FromArgb(155, 215, 255),
            Margin = new Padding(0, 8, 0, 0),
            Text = "Station labels on spectrum",
        };
        spectrumOverlayToggle.CheckedChanged += (_, _) => _spectrumOverlay.Enabled = spectrumOverlayToggle.Checked;

        _statusLabel = new Label
        {
            AutoSize = true,
            ForeColor = Color.FromArgb(105, 210, 255),
        };

        _cloudStatusLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(320, 0),
            ForeColor = Color.FromArgb(170, 185, 195),
            Text = "DXNexus cloud: checking…",
        };

        _liveStatusLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(320, 0),
            ForeColor = Color.FromArgb(170, 185, 195),
            Text = "Browser companion: checking…",
        };

        _remoteTuneButton = new Button
        {
            AutoSize = true,
            FlatStyle = FlatStyle.Flat,
            ForeColor = Color.FromArgb(155, 215, 255),
            Margin = new Padding(0, 7, 0, 0),
            Text = "Enable browser tuning (15 min)",
        };
        _remoteTuneButton.Click += async (_, _) =>
        {
            _remoteTuneButton.Enabled = false;
            try
            {
                await _sink.RequestRemoteTuningAsync(_latestSnapshot.Sequence);
            }
            catch (Exception error)
            {
                _liveStatusLabel.Text = $"Could not request browser tuning: {error.Message}";
                _liveStatusLabel.ForeColor = Color.FromArgb(255, 140, 140);
            }
            finally
            {
                _remoteTuneButton.Enabled = true;
            }
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
        layout.Controls.Add(_cloudStatusLabel);
        layout.Controls.Add(_liveStatusLabel);
        layout.Controls.Add(_remoteTuneButton);
        layout.Controls.Add(_frequencyLabel);
        layout.Controls.Add(_modeLabel);
        layout.Controls.Add(spectrumOverlayToggle);
        layout.Controls.Add(_candidateStatusLabel);
        layout.Controls.Add(_candidateList);
        Controls.Add(layout);

        _collector.SnapshotChanged += HandleSnapshotChanged;
        _publisher.ConnectionStateChanged += HandleConnectionStateChanged;
        _sink.CandidatesReceived += HandleCandidatesReceived;
        _sink.ContextErrorReceived += HandleContextErrorReceived;
        _sink.BridgeStatusReceived += HandleBridgeStatusReceived;
        _sink.CommandCompleted += HandleCommandCompleted;
        Render(_latestSnapshot);
        RenderConnectionState(_publisher.State);
    }

    private void HandleCommandCompleted(object? sender, CommandResult result)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(() => HandleCommandCompleted(sender, result));
            return;
        }
        _candidateStatusLabel.Text = result.Message;
        _candidateStatusLabel.ForeColor = result.Success
            ? Color.FromArgb(84, 226, 159)
            : Color.FromArgb(255, 140, 140);
    }

    private void HandleContextErrorReceived(object? sender, BridgeError error)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(() => HandleContextErrorReceived(sender, error));
            return;
        }
        _candidateList.Controls.Clear();
        _candidateStatusLabel.Text = error.Code == "catalog-unavailable"
            ? "The DXNexus station catalog is temporarily unavailable. Retrying automatically…"
            : error.Message;
        _candidateStatusLabel.ForeColor = error.Transient
            ? Color.FromArgb(255, 196, 96)
            : Color.FromArgb(255, 140, 140);
    }

    private void HandleBridgeStatusReceived(object? sender, BridgeServiceStatus status)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(() => HandleBridgeStatusReceived(sender, status));
            return;
        }

        _cloudStatusLabel.Text = status.CloudState switch
        {
            "connected" => "DXNexus cloud connected",
            "degraded" => $"DXNexus cloud degraded · {status.Message}",
            "action-required" => status.Message,
            _ => "DXNexus cloud: checking…",
        };
        _cloudStatusLabel.ForeColor = status.CloudState == "connected"
            ? Color.FromArgb(84, 226, 159)
            : status.CloudState == "degraded"
                ? Color.FromArgb(255, 196, 96)
                : Color.FromArgb(170, 185, 195);

        _liveStatusLabel.Text = !status.LiveEnabled
            ? "Browser companion disabled in the Bridge"
            : status.LiveConnected
                ? "Live browser connected"
                : "Live browser reconnecting…";
        _liveStatusLabel.ForeColor = status.LiveConnected
            ? Color.FromArgb(84, 226, 159)
            : Color.FromArgb(255, 196, 96);

        if (status.RemoteTuningUntil is { } until && until > DateTimeOffset.UtcNow)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((until - DateTimeOffset.UtcNow).TotalMinutes));
            _remoteTuneButton.Text = $"Browser tuning enabled ({minutes} min)";
            _remoteTuneButton.ForeColor = Color.FromArgb(84, 226, 159);
        }
        else
        {
            _remoteTuneButton.Text = "Enable browser tuning (15 min)";
            _remoteTuneButton.ForeColor = Color.FromArgb(155, 215, 255);
        }
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
        _candidateStatusLabel.ForeColor = Color.FromArgb(170, 185, 195);
        foreach (var candidate in response.Candidates.Take(6))
        {
            var received = candidate.Received.AtListeningPoint ? " · heard here" : candidate.Wishlisted ? " · target" : "";
            var field = candidate.Estimate.FieldStrengthDbuvM is double value ? $" · {value:0} dBµV/m" : "";
            var info = new Label
            {
                AutoSize = true,
                MaximumSize = new Size(300, 0),
                Margin = new Padding(0),
                Text = $"{candidate.StationName}\n{candidate.DistanceKm:0.#} km · {candidate.BearingDeg:0}° {candidate.BearingCardinal}{field}{received}",
                ForeColor = candidate.Received.AtListeningPoint
                    ? Color.FromArgb(84, 226, 159)
                    : Color.WhiteSmoke,
            };
            var target = new Button
            {
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                Text = candidate.Wishlisted ? "Remove target" : "Target",
                ForeColor = candidate.Wishlisted ? Color.FromArgb(255, 150, 170) : Color.FromArgb(155, 215, 255),
                Tag = candidate.Wishlisted,
            };
            target.Click += async (_, _) =>
            {
                try
                {
                    var next = !(target.Tag is true);
                    target.Enabled = false;
                    await _sink.SetWishlistAsync(candidate, next, _latestSnapshot.Sequence);
                    target.Tag = next;
                    target.Text = next ? "Remove target" : "Target";
                    target.Enabled = true;
                }
                catch (Exception error)
                {
                    target.Enabled = true;
                    _candidateStatusLabel.Text = $"Target update failed: {error.Message}";
                }
            };
            var log = new Button { AutoSize = true, FlatStyle = FlatStyle.Flat, Text = "Log", ForeColor = Color.FromArgb(155, 215, 255) };
            log.Click += async (_, _) =>
            {
                using var form = new QuickLogForm(candidate);
                if (form.ShowDialog(this) != DialogResult.OK) return;
                log.Enabled = false;
                try
                {
                    await _sink.LogAsync(new QuickLogCommand(
                        Guid.CreateVersion7(), candidate, form.SignalQuality, form.IdentificationStatus,
                        form.IdentificationMethods, form.Notes, null, _latestSnapshot), _latestSnapshot.Sequence);
                }
                catch (Exception error)
                {
                    _candidateStatusLabel.Text = $"Log failed: {error.Message}";
                }
                finally { log.Enabled = true; }
            };
            var stream = new Button
            {
                AutoSize = true,
                FlatStyle = FlatStyle.Flat,
                Text = "Stream",
                ForeColor = Color.FromArgb(155, 215, 255),
            };
            stream.Click += (_, _) =>
            {
                try
                {
                    Process.Start(new ProcessStartInfo(StationStreamSearch.BuildGoogleUri(candidate).AbsoluteUri)
                    {
                        UseShellExecute = true,
                    });
                    _candidateStatusLabel.Text = $"Opening stream search for {candidate.StationName}…";
                    _candidateStatusLabel.ForeColor = Color.FromArgb(84, 226, 159);
                }
                catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
                {
                    _candidateStatusLabel.Text = $"Could not open stream search: {error.Message}";
                    _candidateStatusLabel.ForeColor = Color.FromArgb(255, 140, 140);
                }
            };
            var actions = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 3, 0, 0) };
            actions.Controls.Add(target);
            actions.Controls.Add(log);
            actions.Controls.Add(stream);
            var row = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                Margin = new Padding(0, 0, 0, 9),
            };
            row.Controls.Add(info);
            row.Controls.Add(actions);
            _candidateList.Controls.Add(row);
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
        if (_latestSnapshot is not null && _latestSnapshot.Radio.FrequencyHz != snapshot.Radio.FrequencyHz)
        {
            _candidateStatusLabel.Text = "Loading station candidates…";
            _candidateStatusLabel.ForeColor = Color.FromArgb(170, 185, 195);
            _candidateList.Controls.Clear();
        }
        _latestSnapshot = snapshot;
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
            _sink.ContextErrorReceived -= HandleContextErrorReceived;
            _sink.BridgeStatusReceived -= HandleBridgeStatusReceived;
            _sink.CommandCompleted -= HandleCommandCompleted;
        }

        base.Dispose(disposing);
    }
}

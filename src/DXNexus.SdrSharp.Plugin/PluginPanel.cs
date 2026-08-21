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
    private readonly StatusDot _bridgeStatusDot = new();
    private readonly StatusDot _cloudStatusDot = new();
    private readonly StatusDot _liveStatusDot = new();
    private readonly Label _statusLabel;
    private readonly Label _cloudStatusLabel;
    private readonly Label _liveStatusLabel;
    private readonly ModernButton _remoteTuneButton;
    private readonly ModernButton _reconnectButton;
    private readonly Label _candidateStatusLabel;
    private readonly FlowLayoutPanel _candidateList;
    private readonly FlowLayoutPanel _content;
    private readonly List<Control> _stretchControls = [];
    private readonly Dictionary<string, LogoTarget> _candidateLogos = new(StringComparer.Ordinal);
    private SequencedRadioSnapshot _latestSnapshot;
    private long _candidateSequence = -1;

    public PluginPanel(
        RadioStateCollector collector,
        RadioStatePublisher publisher,
        PipeRadioStateSink sink,
        SpectrumOverlayController spectrumOverlay)
    {
        _collector = collector ?? throw new ArgumentNullException(nameof(collector));
        _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
        _sink = sink ?? throw new ArgumentNullException(nameof(sink));
        _spectrumOverlay = spectrumOverlay ?? throw new ArgumentNullException(nameof(spectrumOverlay));
        _latestSnapshot = _collector.CaptureFullSnapshot();

        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScroll = true;
        BackColor = DxnexusTheme.Background;
        DoubleBuffered = true;
        Font = DxnexusTheme.UiFont(9.5f);
        ForeColor = DxnexusTheme.Text;
        MinimumSize = new Size(250, 260);
        Padding = new Padding(9, 9, 9, 8);

        var title = new Label
        {
            AutoSize = true,
            BackColor = DxnexusTheme.Background,
            Font = DxnexusTheme.UiFont(15, FontStyle.Bold),
            ForeColor = DxnexusTheme.Text,
            Margin = new Padding(0, 0, 0, 6),
            Text = "DXNexus",
        };

        _statusLabel = StatusLabel("Bridge: checking…");
        _cloudStatusLabel = StatusLabel("DXNexus cloud: checking…");
        _liveStatusLabel = StatusLabel("Browser companion: checking…");
        var statuses = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = DxnexusTheme.Background,
            FlowDirection = FlowDirection.TopDown,
            Margin = new Padding(0),
            WrapContents = false,
        };
        statuses.Controls.Add(StatusRow(_bridgeStatusDot, _statusLabel));
        statuses.Controls.Add(StatusRow(_cloudStatusDot, _cloudStatusLabel));
        statuses.Controls.Add(StatusRow(_liveStatusDot, _liveStatusLabel));

        var separator = new Panel
        {
            BackColor = DxnexusTheme.Border,
            Height = 1,
            Margin = new Padding(0, 6, 0, 6),
        };

        _reconnectButton = new ModernButton
        {
            ForeColor = DxnexusTheme.Warning,
            Margin = new Padding(0, 0, 0, 5),
            Text = "↻  Reconnect DXNexus",
            Visible = false,
        };
        _reconnectButton.Click += async (_, _) =>
        {
            _reconnectButton.Enabled = false;
            try
            {
                await _sink.RequestPairingAsync(_latestSnapshot.Sequence);
            }
            catch (Exception error)
            {
                _cloudStatusLabel.Text = $"Could not open reconnection: {error.Message}";
                _cloudStatusLabel.ForeColor = DxnexusTheme.Error;
                _cloudStatusDot.DotColor = DxnexusTheme.Error;
            }
            finally
            {
                _reconnectButton.Enabled = true;
            }
        };

        _remoteTuneButton = new ModernButton
        {
            ForeColor = DxnexusTheme.Text,
            Height = 32,
            Margin = new Padding(0, 0, 0, 6),
            NormalColor = Color.FromArgb(28, 47, 59),
            Text = "◉  Enable browser tuning (15 min)",
        };
        _remoteTuneButton.FlatAppearance.BorderColor = Color.FromArgb(53, 105, 135);
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
                _liveStatusLabel.ForeColor = DxnexusTheme.Error;
                _liveStatusDot.DotColor = DxnexusTheme.Error;
            }
            finally
            {
                _remoteTuneButton.Enabled = true;
            }
        };

        var spectrumOverlayToggle = new CheckBox
        {
            AutoSize = false,
            BackColor = DxnexusTheme.Background,
            Checked = _spectrumOverlay.Enabled,
            Font = DxnexusTheme.UiFont(9),
            ForeColor = DxnexusTheme.Text,
            Height = 23,
            Margin = new Padding(0, 1, 0, 3),
            Text = "Station labels on spectrum",
            UseVisualStyleBackColor = false,
        };
        spectrumOverlayToggle.CheckedChanged += (_, _) => _spectrumOverlay.Enabled = spectrumOverlayToggle.Checked;

        _candidateStatusLabel = new Label
        {
            AutoEllipsis = true,
            BackColor = DxnexusTheme.Background,
            Font = DxnexusTheme.UiFont(9),
            ForeColor = DxnexusTheme.Muted,
            Height = 24,
            Margin = new Padding(0, 0, 0, 5),
            Text = "◉  Tune to a broadcast frequency to see candidates.",
            TextAlign = ContentAlignment.MiddleLeft,
        };

        _candidateList = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = DxnexusTheme.Background,
            FlowDirection = FlowDirection.TopDown,
            Margin = new Padding(0),
            WrapContents = false,
        };

        var footer = BuildFooter();
        _content = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = DxnexusTheme.Background,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.TopDown,
            Margin = new Padding(0),
            WrapContents = false,
        };
        _content.Controls.Add(title);
        _content.Controls.Add(statuses);
        _content.Controls.Add(separator);
        _content.Controls.Add(_reconnectButton);
        _content.Controls.Add(_remoteTuneButton);
        _content.Controls.Add(spectrumOverlayToggle);
        _content.Controls.Add(_candidateStatusLabel);
        _content.Controls.Add(_candidateList);
        _content.Controls.Add(footer);
        Controls.Add(_content);

        _stretchControls.AddRange([
            statuses, separator, _reconnectButton, _remoteTuneButton,
            spectrumOverlayToggle, _candidateStatusLabel, _candidateList, footer,
        ]);

        _collector.SnapshotChanged += HandleSnapshotChanged;
        _publisher.ConnectionStateChanged += HandleConnectionStateChanged;
        _sink.CandidatesReceived += HandleCandidatesReceived;
        _sink.StationLogoReceived += HandleStationLogoReceived;
        _sink.ContextErrorReceived += HandleContextErrorReceived;
        _sink.BridgeStatusReceived += HandleBridgeStatusReceived;
        _sink.CommandCompleted += HandleCommandCompleted;
        HandleCreated += (_, _) => UpdateResponsiveWidths();
        Render(_latestSnapshot);
        RenderConnectionState(_publisher.State);
    }

    protected override void OnResize(EventArgs eventArgs)
    {
        base.OnResize(eventArgs);
        UpdateResponsiveWidths();
    }

    private static Control BuildFooter()
    {
        var version = new Label
        {
            BackColor = DxnexusTheme.Background,
            Dock = DockStyle.Fill,
            Font = DxnexusTheme.UiFont(9),
            ForeColor = DxnexusTheme.Muted,
            Text = "☁  DXNexus v0.1.0",
            TextAlign = ContentAlignment.MiddleLeft,
        };
        var website = new LinkLabel
        {
            ActiveLinkColor = DxnexusTheme.Teal,
            BackColor = DxnexusTheme.Background,
            Dock = DockStyle.Fill,
            Font = DxnexusTheme.UiFont(9),
            LinkColor = DxnexusTheme.Accent,
            Text = "dxnexus",
            TextAlign = ContentAlignment.MiddleRight,
            VisitedLinkColor = DxnexusTheme.Accent,
        };
        website.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo("https://dxnexus.rapinoinfeliz.workers.dev/")
        {
            UseShellExecute = true,
        });
        var footer = new TableLayoutPanel
        {
            BackColor = DxnexusTheme.Background,
            ColumnCount = 2,
            Height = 30,
            Margin = new Padding(0, 6, 0, 0),
            RowCount = 1,
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 62));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 38));
        footer.Controls.Add(version, 0, 0);
        footer.Controls.Add(website, 1, 0);
        return footer;
    }

    private static Label StatusLabel(string text) => new()
    {
        AutoSize = true,
        BackColor = DxnexusTheme.Background,
        Font = DxnexusTheme.UiFont(9),
        ForeColor = DxnexusTheme.Muted,
        Margin = new Padding(0),
        Text = text,
    };

    private static Control StatusRow(StatusDot dot, Label label)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = DxnexusTheme.Background,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 0, 0, 1),
            WrapContents = false,
        };
        row.Controls.Add(dot);
        row.Controls.Add(label);
        return row;
    }

    private void HandleCommandCompleted(object? sender, CommandResult result)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(() => HandleCommandCompleted(sender, result));
            return;
        }
        _candidateStatusLabel.Text = $"◉  {result.Message}";
        _candidateStatusLabel.ForeColor = result.Success ? DxnexusTheme.Success : DxnexusTheme.Error;
    }

    private void HandleContextErrorReceived(object? sender, BridgeError error)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(() => HandleContextErrorReceived(sender, error));
            return;
        }
        ClearCandidates();
        _candidateStatusLabel.Text = error.Code == "catalog-unavailable"
            ? "◉  The station catalog is temporarily unavailable. Retrying…"
            : $"◉  {error.Message}";
        _candidateStatusLabel.ForeColor = error.Transient ? DxnexusTheme.Warning : DxnexusTheme.Error;
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
        _cloudStatusLabel.ForeColor = status.CloudState switch
        {
            "connected" => DxnexusTheme.Text,
            "degraded" => DxnexusTheme.Warning,
            "action-required" => DxnexusTheme.Error,
            _ => DxnexusTheme.Muted,
        };
        _cloudStatusDot.DotColor = status.CloudState switch
        {
            "connected" => DxnexusTheme.Success,
            "degraded" => DxnexusTheme.Warning,
            "action-required" => DxnexusTheme.Error,
            _ => DxnexusTheme.Muted,
        };

        _liveStatusLabel.Text = !status.LiveEnabled
            ? "Browser companion disabled"
            : status.LiveConnected
                ? "Live browser connected"
                : "Live browser reconnecting…";
        _liveStatusLabel.ForeColor = status.LiveConnected
            ? DxnexusTheme.Text
            : status.LiveEnabled ? DxnexusTheme.Warning : DxnexusTheme.Muted;
        _liveStatusDot.DotColor = status.LiveConnected
            ? DxnexusTheme.Success
            : status.LiveEnabled ? DxnexusTheme.Warning : DxnexusTheme.Muted;

        var authenticationText = $"{status.Code} {status.Message}";
        _reconnectButton.Visible = !status.Paired
            || status.CloudState == "action-required" &&
            (authenticationText.Contains("credential", StringComparison.OrdinalIgnoreCase)
             || authenticationText.Contains("token", StringComparison.OrdinalIgnoreCase)
             || authenticationText.Contains("pair", StringComparison.OrdinalIgnoreCase));

        if (status.RemoteTuningUntil is { } until && until > DateTimeOffset.UtcNow)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((until - DateTimeOffset.UtcNow).TotalMinutes));
            _remoteTuneButton.Text = $"●  Browser tuning enabled ({minutes} min)";
            _remoteTuneButton.ForeColor = DxnexusTheme.Success;
        }
        else
        {
            _remoteTuneButton.Text = "◉  Enable browser tuning (15 min)";
            _remoteTuneButton.ForeColor = DxnexusTheme.Text;
        }
        UpdateResponsiveWidths();
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
        ClearCandidates();
        _candidateSequence = response.Sequence;
        _candidateStatusLabel.Text = response.Candidates.Length == 0
            ? "◉  No catalog candidate on this exact frequency."
            : $"♟  {response.Candidates.Length} candidate{(response.Candidates.Length == 1 ? "" : "s")}  ·  {response.Band}";
        _candidateStatusLabel.ForeColor = DxnexusTheme.Muted;
        foreach (var candidate in response.Candidates.Take(6))
            _candidateList.Controls.Add(BuildCandidateCard(candidate));
        _candidateList.ResumeLayout();
        UpdateResponsiveWidths();
    }

    private RoundedPanel BuildCandidateCard(StationCandidate candidate)
    {
        var card = new RoundedPanel
        {
            BorderColor = candidate.Received.AtListeningPoint
                ? Color.FromArgb(55, 126, 91)
                : candidate.Wishlisted ? Color.FromArgb(117, 69, 87) : DxnexusTheme.Border,
            Height = 126,
            Margin = new Padding(0, 0, 0, 7),
            Padding = new Padding(9),
        };
        var root = new TableLayoutPanel
        {
            BackColor = DxnexusTheme.Card,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            RowCount = 2,
        };
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        var heading = new TableLayoutPanel
        {
            BackColor = DxnexusTheme.Card,
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Margin = new Padding(0),
            RowCount = 1,
        };
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 38));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        heading.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56));

        var logoHost = new Panel { BackColor = DxnexusTheme.Card, Dock = DockStyle.Fill, Margin = new Padding(0) };
        var placeholder = new Label
        {
            BackColor = DxnexusTheme.Card,
            Dock = DockStyle.Fill,
            Font = DxnexusTheme.UiFont(16, FontStyle.Bold),
            ForeColor = candidate.Received.AtListeningPoint ? DxnexusTheme.Success : DxnexusTheme.Teal,
            Text = "◉",
            TextAlign = ContentAlignment.MiddleCenter,
        };
        var logo = new PictureBox
        {
            BackColor = DxnexusTheme.Card,
            Dock = DockStyle.Fill,
            SizeMode = PictureBoxSizeMode.Zoom,
            Visible = false,
        };
        logoHost.Controls.Add(placeholder);
        logoHost.Controls.Add(logo);
        if (!string.IsNullOrWhiteSpace(candidate.LogoUrl))
            _candidateLogos[CandidateKey(candidate.BroadcastId, candidate.SiteId)] = new LogoTarget(logo, placeholder);

        var text = new TableLayoutPanel
        {
            BackColor = DxnexusTheme.Card,
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            Margin = new Padding(7, 0, 5, 0),
            RowCount = 2,
        };
        text.RowStyles.Add(new RowStyle(SizeType.Percent, 58));
        text.RowStyles.Add(new RowStyle(SizeType.Percent, 42));
        var name = new Label
        {
            AutoEllipsis = true,
            BackColor = DxnexusTheme.Card,
            Dock = DockStyle.Fill,
            Font = DxnexusTheme.UiFont(9.5f, FontStyle.Bold),
            ForeColor = DxnexusTheme.Text,
            Margin = new Padding(0),
            Text = candidate.StationName,
            TextAlign = ContentAlignment.BottomLeft,
        };
        var received = candidate.Received.AtListeningPoint ? " · heard here" : candidate.Wishlisted ? " · target" : "";
        var details = new Label
        {
            AutoEllipsis = true,
            BackColor = DxnexusTheme.Card,
            Dock = DockStyle.Fill,
            Font = DxnexusTheme.UiFont(8.4f),
            ForeColor = DxnexusTheme.Muted,
            Margin = new Padding(0),
            Text = $"{candidate.DistanceKm:0.#} km · {candidate.BearingDeg:0}° {candidate.BearingCardinal}{received}",
            TextAlign = ContentAlignment.TopLeft,
        };
        text.Controls.Add(name, 0, 0);
        text.Controls.Add(details, 0, 1);

        var field = new Label
        {
            BackColor = DxnexusTheme.Card,
            Dock = DockStyle.Fill,
            Font = DxnexusTheme.UiFont(8.4f),
            ForeColor = DxnexusTheme.Accent,
            Margin = new Padding(0),
            Text = candidate.Estimate.FieldStrengthDbuvM is double value ? $"{value:0}\ndBµV/m" : "",
            TextAlign = ContentAlignment.MiddleRight,
        };
        heading.Controls.Add(logoHost, 0, 0);
        heading.Controls.Add(text, 1, 0);
        heading.Controls.Add(field, 2, 0);

        var target = ActionButton(candidate.Wishlisted ? "● Remove" : "◎ Target");
        target.ForeColor = candidate.Wishlisted ? DxnexusTheme.Target : DxnexusTheme.Accent;
        target.Tag = candidate.Wishlisted;
        target.Click += async (_, _) =>
        {
            try
            {
                var next = !(target.Tag is true);
                target.Enabled = false;
                await _sink.SetWishlistAsync(candidate, next, _latestSnapshot.Sequence);
                target.Tag = next;
                target.Text = next ? "● Remove" : "◎ Target";
                target.ForeColor = next ? DxnexusTheme.Target : DxnexusTheme.Accent;
            }
            catch (Exception error)
            {
                _candidateStatusLabel.Text = $"◉  Target update failed: {error.Message}";
                _candidateStatusLabel.ForeColor = DxnexusTheme.Error;
            }
            finally
            {
                target.Enabled = true;
            }
        };

        var log = ActionButton("▤ Log");
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
                _candidateStatusLabel.Text = $"◉  Log failed: {error.Message}";
                _candidateStatusLabel.ForeColor = DxnexusTheme.Error;
            }
            finally
            {
                log.Enabled = true;
            }
        };

        var stream = ActionButton("▶ Stream");
        stream.Click += (_, _) =>
        {
            try
            {
                Process.Start(new ProcessStartInfo(StationStreamSearch.BuildGoogleUri(candidate).AbsoluteUri)
                {
                    UseShellExecute = true,
                });
                _candidateStatusLabel.Text = $"◉  Opening stream search for {candidate.StationName}…";
                _candidateStatusLabel.ForeColor = DxnexusTheme.Success;
            }
            catch (Exception error) when (error is InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                _candidateStatusLabel.Text = $"◉  Could not open stream search: {error.Message}";
                _candidateStatusLabel.ForeColor = DxnexusTheme.Error;
            }
        };

        var actions = new TableLayoutPanel
        {
            BackColor = DxnexusTheme.Card,
            ColumnCount = 3,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 3, 0, 0),
            RowCount = 1,
        };
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.333f));
        actions.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.334f));
        target.Margin = new Padding(0, 0, 4, 0);
        log.Margin = new Padding(2, 0, 2, 0);
        stream.Margin = new Padding(4, 0, 0, 0);
        actions.Controls.Add(target, 0, 0);
        actions.Controls.Add(log, 1, 0);
        actions.Controls.Add(stream, 2, 0);

        root.Controls.Add(heading, 0, 0);
        root.Controls.Add(actions, 0, 1);
        card.Controls.Add(root);
        return card;
    }

    private static ModernButton ActionButton(string text) => new()
    {
        Dock = DockStyle.Fill,
        Font = DxnexusTheme.UiFont(8.5f),
        ForeColor = DxnexusTheme.Accent,
        Text = text,
    };

    private void HandleStationLogoReceived(object? sender, StationLogoImage logo)
    {
        if (IsDisposed) return;
        if (InvokeRequired)
        {
            BeginInvoke(() => HandleStationLogoReceived(sender, logo));
            return;
        }
        if (logo.Sequence != _candidateSequence
            || !_candidateLogos.TryGetValue(CandidateKey(logo.BroadcastId, logo.SiteId), out var target)) return;

        try
        {
            using var stream = new MemoryStream(logo.PngBytes, writable: false);
            using var source = Image.FromStream(stream);
            var rendered = new Bitmap(source);
            var previous = target.Picture.Image;
            target.Picture.Image = rendered;
            target.Picture.Visible = true;
            target.Picture.BringToFront();
            target.Placeholder.Visible = false;
            previous?.Dispose();
        }
        catch (ArgumentException)
        {
            // The fallback broadcast glyph remains visible for malformed images.
        }
    }

    private void ClearCandidates()
    {
        foreach (var target in _candidateLogos.Values)
        {
            target.Picture.Image?.Dispose();
            target.Picture.Image = null;
        }
        _candidateLogos.Clear();
        foreach (Control card in _candidateList.Controls.Cast<Control>().ToArray()) card.Dispose();
        _candidateList.Controls.Clear();
        _candidateSequence = -1;
    }

    private void HandleConnectionStateChanged(object? sender, BridgeConnectionState state)
    {
        if (IsDisposed) return;
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
            BridgeConnectionState.Connecting => "Connecting to Bridge…",
            _ => "Bridge offline",
        };
        _statusLabel.ForeColor = state == BridgeConnectionState.Connected
            ? DxnexusTheme.Text
            : state == BridgeConnectionState.Connecting ? DxnexusTheme.Warning : DxnexusTheme.Error;
        _bridgeStatusDot.DotColor = state == BridgeConnectionState.Connected
            ? DxnexusTheme.Success
            : state == BridgeConnectionState.Connecting ? DxnexusTheme.Warning : DxnexusTheme.Error;
    }

    private void HandleSnapshotChanged(object? sender, SequencedRadioSnapshot snapshot)
    {
        if (IsDisposed) return;
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
            _candidateStatusLabel.Text = "◉  Loading station candidates…";
            _candidateStatusLabel.ForeColor = DxnexusTheme.Muted;
            ClearCandidates();
        }
        _latestSnapshot = snapshot;
    }

    private void UpdateResponsiveWidths()
    {
        if (_content is null || ClientSize.Width <= 0) return;
        var scrollbar = VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
        var width = Math.Max(210, ClientSize.Width - Padding.Horizontal - scrollbar - 1);
        _content.Width = width;
        foreach (var control in _stretchControls) control.Width = width;
        foreach (Control card in _candidateList.Controls) card.Width = width;
        _statusLabel.MaximumSize = new Size(Math.Max(160, width - 28), 0);
        _cloudStatusLabel.MaximumSize = new Size(Math.Max(160, width - 28), 0);
        _liveStatusLabel.MaximumSize = new Size(Math.Max(160, width - 28), 0);
    }

    private static string CandidateKey(string broadcastId, string siteId) => $"{broadcastId}\n{siteId}";

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _collector.SnapshotChanged -= HandleSnapshotChanged;
            _publisher.ConnectionStateChanged -= HandleConnectionStateChanged;
            _sink.CandidatesReceived -= HandleCandidatesReceived;
            _sink.StationLogoReceived -= HandleStationLogoReceived;
            _sink.ContextErrorReceived -= HandleContextErrorReceived;
            _sink.BridgeStatusReceived -= HandleBridgeStatusReceived;
            _sink.CommandCompleted -= HandleCommandCompleted;
            ClearCandidates();
        }
        base.Dispose(disposing);
    }

    private sealed record LogoTarget(PictureBox Picture, Label Placeholder);
}

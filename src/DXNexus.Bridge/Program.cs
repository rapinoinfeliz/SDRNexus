using DXNexus.Bridge.Core;
using DXNexus.Contracts;
using DXNexus.LocalTransport;
using System.Text.Json;

namespace DXNexus.Bridge;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new BridgeApplicationContext());
    }
}

internal sealed class BridgeApplicationContext : ApplicationContext
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly NotifyIcon _notifyIcon;
    private readonly BridgePipeServer _pipeServer;
    private readonly SynchronizationContext _uiContext;
    private readonly HttpClient _httpClient;
    private readonly AuthenticatedDeviceApiClient _apiClient;
    private readonly DeviceCredentialStore _credentialStore;
    private readonly BridgePreferencesStore _preferencesStore;
    private readonly OfflineMutationQueue _offlineQueue;
    private readonly LiveCompanionClient _liveCompanion;
    private readonly ToolStripMenuItem _liveMenu;
    private readonly ToolStripMenuItem _remoteTuneMenu;
    private readonly SemaphoreSlim _syncGate = new(1, 1);
    private readonly System.Threading.Timer _syncTimer;
    private readonly System.Threading.Timer _liveHeartbeatTimer;
    private CancellationTokenSource? _candidateQuery;
    private ReceptionSetupContext? _receptionSetup;
    private SequencedRadioSnapshot? _latestRadioSnapshot;
    private CandidateContextResponse? _latestCandidateContext;
    private volatile bool _liveEnabled;
    private DateTimeOffset _remoteTuneUntil;

    public BridgeApplicationContext()
    {
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _credentialStore = new DeviceCredentialStore();
        _preferencesStore = new BridgePreferencesStore();
        _offlineQueue = new OfflineMutationQueue();
        _httpClient = new HttpClient { BaseAddress = PairingApiClient.ProductionBaseUri };
        _apiClient = new AuthenticatedDeviceApiClient(_httpClient, _credentialStore);
        _liveCompanion = new LiveCompanionClient(_apiClient);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Connect to DXNexus…", null, (_, _) => ShowPairing());
        menu.Items.Add("Reception setup…", null, (_, _) => ShowReceptionSetup());
        _liveMenu = new ToolStripMenuItem("Live browser companion") { CheckOnClick = true };
        _liveMenu.CheckedChanged += (_, _) => _ = SetLiveCompanionAsync(_liveMenu.Checked);
        menu.Items.Add(_liveMenu);
        _remoteTuneMenu = new ToolStripMenuItem("Allow browser tuning for 15 minutes…");
        _remoteTuneMenu.Click += (_, _) => EnableRemoteTuning();
        menu.Items.Add(_remoteTuneMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitThread());

        _notifyIcon = new NotifyIcon
        {
            Text = BridgeIdentity.ProductName,
            Icon = SystemIcons.Application,
            ContextMenuStrip = menu,
            Visible = true,
        };

        _pipeServer = new BridgePipeServer(LocalPipeName.ForCurrentWindowsUser());
        _pipeServer.ConnectionChanged += HandleConnectionChanged;
        _pipeServer.MessageReceived += HandleMessageReceived;
        _pipeServer.Start();
        _syncTimer = new System.Threading.Timer(_ => _ = SynchronizeOfflineMutationsAsync(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(15));
        _liveHeartbeatTimer = new System.Threading.Timer(_ => _ = PublishLiveStateAsync(), null, TimeSpan.FromSeconds(15), TimeSpan.FromSeconds(15));
        _liveCompanion.TuneCommandReceived += HandleRemoteTuneCommand;
        _ = InitializePreferencesAsync();
    }

    private void EnableRemoteTuning()
    {
        var choice = MessageBox.Show(
            "For the next 15 minutes, the signed-in DXNexus browser may change the SDR# tuned frequency. " +
            "Commands are accepted only while the Bridge, plugin and current tuner state all match.\n\nAllow temporary browser tuning?",
            "DXNexus browser tuning",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (choice != DialogResult.Yes) return;
        _remoteTuneUntil = DateTimeOffset.UtcNow.AddMinutes(15);
        _remoteTuneMenu.Text = "Browser tuning allowed · 15 min";
    }

    private void HandleRemoteTuneCommand(object? sender, RemoteTuneCommand command) => _ = ApplyRemoteTuneCommandAsync(command);

    private async Task ApplyRemoteTuneCommandAsync(RemoteTuneCommand command)
    {
        var snapshot = _latestRadioSnapshot;
        RemoteTuneResult failure(string message) => new(
            "live.command.result", Protocol.Version, command.CommandId, "tune", false, message);
        try
        {
            if (command.DeviceId != await _apiClient.GetDeviceIdAsync().ConfigureAwait(false))
            {
                await PublishTuneResultSafelyAsync(failure("The tune command targets another Bridge."));
                return;
            }
        }
        catch (InvalidOperationException)
        {
            await PublishTuneResultSafelyAsync(failure("The Bridge is not paired with DXNexus."));
            return;
        }
        var now = DateTimeOffset.UtcNow;
        var rejection = RemoteTunePolicy.RejectionReason(command, snapshot, _remoteTuneUntil, now);
        if (rejection is not null)
        {
            if (now >= _remoteTuneUntil) _remoteTuneUntil = default;
            await PublishTuneResultSafelyAsync(failure(rejection));
            return;
        }
        if (!await _pipeServer.SendAsync("command.tune", snapshot!.Sequence, command, CancellationToken.None))
            await PublishTuneResultSafelyAsync(failure("The SDR# plugin is not connected."));
    }

    private async Task PublishTuneResultSafelyAsync(RemoteTuneResult result)
    {
        try { await _liveCompanion.PublishCommandResultAsync(result).ConfigureAwait(false); }
        catch (Exception error) when (IsTransient(error) || error is InvalidOperationException) { }
    }

    private async Task InitializePreferencesAsync()
    {
        try
        {
            var preferences = await _preferencesStore.LoadAsync().ConfigureAwait(false);
            _liveEnabled = preferences.LiveBrowserCompanion;
            _uiContext.Post(_ => _liveMenu.Checked = _liveEnabled, null);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException)
        {
            _liveEnabled = false;
        }
    }

    private async Task SetLiveCompanionAsync(bool enabled)
    {
        try
        {
            _liveEnabled = enabled;
            var preferences = await _preferencesStore.LoadAsync().ConfigureAwait(false);
            if (preferences.LiveBrowserCompanion != enabled)
                await _preferencesStore.SaveAsync(preferences with { LiveBrowserCompanion = enabled }).ConfigureAwait(false);
            if (enabled) await PublishLiveStateAsync().ConfigureAwait(false);
            else await _liveCompanion.DisconnectAsync().ConfigureAwait(false);
        }
        catch (Exception error) when (IsTransient(error) || error is IOException or UnauthorizedAccessException)
        {
            _uiContext.Post(_ => _notifyIcon.ShowBalloonTip(
                4_000,
                "DXNexus live companion",
                "The live connection could not be changed. Check the network and try again.",
                ToolTipIcon.Warning), null);
        }
    }

    private static void ShowPairing()
    {
        using var form = new PairingForm();
        form.ShowDialog();
    }

    private void ShowReceptionSetup()
    {
        using var form = new ReceptionSetupForm(_apiClient, _preferencesStore);
        if (form.ShowDialog() == DialogResult.OK) _receptionSetup = form.SelectedSetup;
    }

    private void HandleConnectionChanged(object? sender, bool connected)
    {
        _uiContext.Post(_ =>
        {
            _notifyIcon.Text = connected
                ? "DXNexus Bridge — SDR# connected"
                : BridgeIdentity.ProductName;
        }, null);
    }

    private void HandleMessageReceived(object? sender, PipeEnvelope message)
    {
        if (message.Type == "command.tune.result")
        {
            var result = message.Payload.Deserialize<RemoteTuneResult>(JsonOptions);
            if (result is not null) _ = PublishTuneResultSafelyAsync(result);
            return;
        }
        if (message.Type == "command.wishlist")
        {
            var command = message.Payload.Deserialize<WishlistCommand>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (command is not null) _ = ApplyWishlistCommandAsync(command, message.Sequence);
            return;
        }
        if (message.Type == "command.logbook")
        {
            var command = message.Payload.Deserialize<QuickLogCommand>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
            if (command is not null) _ = ApplyLogbookCommandAsync(command, message.Sequence);
            return;
        }
        if (message.Type != "radio.snapshot") return;

        var snapshot = message.Payload.Deserialize<SequencedRadioSnapshot>(JsonOptions);
        if (snapshot is null)
        {
            return;
        }

        _latestRadioSnapshot = snapshot;
        _latestCandidateContext = null;

        _uiContext.Post(_ =>
        {
            _notifyIcon.Text = $"DXNexus — {FormatFrequency(snapshot.Radio.FrequencyHz)}";
        }, null);
        _ = RefreshCandidatesAsync(snapshot);
        _ = PublishLiveStateAsync();
    }

    private async Task ApplyWishlistCommandAsync(WishlistCommand command, long sequence)
    {
        var request = new WishlistMutationRequest(
            Protocol.Version,
            command.ClientMutationId,
            command.Wishlisted ? "add" : "remove",
            command.Candidate.BroadcastId,
            command.Wishlisted ? StationMutationContext.FromCandidate(command.Candidate) : null);
        try
        {
            var result = await _apiClient.SetWishlistAsync(request);
            await SendCommandResultAsync(sequence, new CommandResult(
                result.ClientMutationId, "wishlist", true,
                result.Wishlisted ? "Added to Want to hear." : "Removed from Want to hear."));
        }
        catch (Exception error) when (IsTransient(error))
        {
            await _offlineQueue.EnqueueAsync(command.ClientMutationId, "wishlist", JsonSerializer.Serialize(request, JsonOptions));
            await SendCommandResultAsync(sequence, new CommandResult(
                command.ClientMutationId, "wishlist", true, "Target update saved offline and queued for synchronization."));
        }
        catch (Exception error)
        {
            await SendCommandResultAsync(sequence, new CommandResult(
                command.ClientMutationId, "wishlist", false, $"Target update failed: {error.Message}"));
        }
    }

    private async Task ApplyLogbookCommandAsync(QuickLogCommand command, long sequence)
    {
        LogbookMutationRequest? request = null;
        try
        {
            var setup = await ResolveReceptionSetupAsync(CancellationToken.None).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Choose a reception setup in the Bridge first.");
            var deviceId = await _apiClient.GetDeviceIdAsync().ConfigureAwait(false);
            var radio = command.Snapshot.Radio;
            var measurements = new[]
            {
                new SdrMeasurement("visual-snr", radio.Signal.VisualSnrDb, "display-dB", radio.Signal.Source, false),
                new SdrMeasurement("visual-peak", radio.Signal.VisualPeakDb, "display-dB", radio.Signal.Source, false),
                new SdrMeasurement("visual-floor", radio.Signal.VisualFloorDb, "display-dB", radio.Signal.Source, false),
            };
            request = new LogbookMutationRequest(
                Protocol.Version,
                command.ClientMutationId,
                command.Snapshot.CapturedAtUtc,
                command.Candidate.Band,
                command.Candidate.FrequencyHz,
                StationMutationContext.FromCandidate(command.Candidate),
                setup,
                command.SignalQuality,
                command.IdentificationStatus,
                command.IdentificationMethods,
                command.Notes,
                command.Propagation,
                new SdrSnapshotContext(
                    deviceId, radio.FrequencyHz, radio.CenterFrequencyHz,
                    radio.Detector.ToString().ToUpperInvariant(), radio.FilterBandwidthHz,
                    radio.InputSampleRateHz, measurements, false, radio.Rds,
                    "0.1.0", "0.1.0", 1));
            var result = await _apiClient.CreateLogbookEntryAsync(request).ConfigureAwait(false);
            await SendCommandResultAsync(sequence, new CommandResult(
                result.ClientMutationId, "logbook", true,
                result.Created == false ? "Reception was already saved." : "Reception saved in DXNexus."));
        }
        catch (Exception error) when (request is not null && IsTransient(error))
        {
            await _offlineQueue.EnqueueAsync(command.ClientMutationId, "logbook", JsonSerializer.Serialize(request, JsonOptions));
            await SendCommandResultAsync(sequence, new CommandResult(
                command.ClientMutationId, "logbook", true, "Reception saved offline and queued for synchronization."));
        }
        catch (Exception error)
        {
            await SendCommandResultAsync(sequence, new CommandResult(
                command.ClientMutationId, "logbook", false, $"Log failed: {error.Message}"));
        }
    }

    private Task<bool> SendCommandResultAsync(long sequence, CommandResult result) =>
        _pipeServer.SendAsync("command.result", sequence, result, CancellationToken.None);

    private async Task<ReceptionSetupContext?> ResolveReceptionSetupAsync(CancellationToken cancellationToken)
    {
        if (_receptionSetup is not null) return _receptionSetup;
        var preferences = await _preferencesStore.LoadAsync(cancellationToken).ConfigureAwait(false);
        if (preferences.ReceptionSetup is not null)
        {
            _receptionSetup = preferences.ReceptionSetup;
            return _receptionSetup;
        }
        var availableTask = _apiClient.GetReceptionSetupAsync(cancellationToken);
        var available = await availableTask.ConfigureAwait(false);
        var point = available.ListeningPoints.FirstOrDefault(item => item.Id == preferences.ListeningPointId)
            ?? available.ListeningPoints.FirstOrDefault(item => item.IsDefault)
            ?? available.ListeningPoints.FirstOrDefault();
        var receiver = available.Receivers.FirstOrDefault(item => item.Id == preferences.ReceiverProfileId)
            ?? available.Receivers.FirstOrDefault(item => item.IsDefault)
            ?? available.Receivers.FirstOrDefault();
        if (point is null || receiver is null) return null;
        _receptionSetup = new ReceptionSetupContext(point.Id, receiver.Id);
        return _receptionSetup;
    }

    private async Task SynchronizeOfflineMutationsAsync()
    {
        if (!await _syncGate.WaitAsync(0).ConfigureAwait(false)) return;
        try
        {
            foreach (var queued in await _offlineQueue.DueAsync().ConfigureAwait(false))
            {
                try
                {
                    if (queued.Type == "wishlist")
                    {
                        var request = JsonSerializer.Deserialize<WishlistMutationRequest>(queued.PayloadJson, JsonOptions)
                            ?? throw new InvalidDataException("Queued wishlist mutation is empty.");
                        await _apiClient.SetWishlistAsync(request).ConfigureAwait(false);
                    }
                    else
                    {
                        var request = JsonSerializer.Deserialize<LogbookMutationRequest>(queued.PayloadJson, JsonOptions)
                            ?? throw new InvalidDataException("Queued logbook mutation is empty.");
                        await _apiClient.CreateLogbookEntryAsync(request).ConfigureAwait(false);
                    }
                    await _offlineQueue.CompleteAsync(queued.Id).ConfigureAwait(false);
                }
                catch (Exception error) when (IsTransient(error))
                {
                    await _offlineQueue.RetryLaterAsync(queued.Id, queued.Attempts).ConfigureAwait(false);
                    break;
                }
                catch
                {
                    // A permanent validation failure cannot be repaired by retries.
                    await _offlineQueue.CompleteAsync(queued.Id).ConfigureAwait(false);
                }
            }
        }
        catch
        {
            // Synchronization is best-effort and must never terminate the tray process.
        }
        finally { _syncGate.Release(); }
    }

    private static bool IsTransient(Exception error) => error switch
    {
        TaskCanceledException => true,
        HttpRequestException http => http.StatusCode is null
            || http.StatusCode == System.Net.HttpStatusCode.RequestTimeout
            || http.StatusCode == System.Net.HttpStatusCode.TooManyRequests
            || (int)http.StatusCode >= 500,
        System.Net.WebSockets.WebSocketException => true,
        IOException => true,
        _ => false,
    };

    private async Task RefreshCandidatesAsync(SequencedRadioSnapshot snapshot)
    {
        _candidateQuery?.Cancel();
        _candidateQuery?.Dispose();
        var query = new CancellationTokenSource();
        _candidateQuery = query;
        try
        {
            await Task.Delay(450, query.Token).ConfigureAwait(false);
            if (!_credentialStore.Exists) return;
            var setup = await ResolveReceptionSetupAsync(query.Token).ConfigureAwait(false);
            if (setup is null) return;
            var response = await _apiClient.GetCandidatesAsync(new CandidateContextRequest(
                Protocol.Version,
                Guid.CreateVersion7(),
                snapshot.Sequence,
                DateTimeOffset.UtcNow,
                snapshot.Radio.FrequencyHz,
                null,
                snapshot.Radio.Detector.ToString().ToUpperInvariant(),
                snapshot.Radio.FilterBandwidthHz,
                setup,
                20), query.Token).ConfigureAwait(false);
            if (!query.IsCancellationRequested)
            {
                _latestCandidateContext = CompactLiveCandidates(response);
                await _pipeServer.SendAsync("context.candidates", response.Sequence, response, query.Token)
                    .ConfigureAwait(false);
                await PublishLiveStateAsync(query.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (query.IsCancellationRequested)
        {
        }
        catch (HttpRequestException error)
        {
            await _pipeServer.SendAsync("context.error", snapshot.Sequence, new
            {
                code = "cloud-unavailable",
                message = error.StatusCode is null
                    ? "DXNexus is temporarily unreachable."
                    : $"DXNexus returned HTTP {(int)error.StatusCode}.",
            }, CancellationToken.None).ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // The device is not paired yet; the tray pairing action is the recovery path.
        }
    }

    private static CandidateContextResponse CompactLiveCandidates(CandidateContextResponse response) => response with
    {
        Candidates = response.Candidates.Take(6).ToArray(),
        NextCursor = null,
    };

    private async Task PublishLiveStateAsync(CancellationToken cancellationToken = default)
    {
        if (_remoteTuneUntil != default && DateTimeOffset.UtcNow >= _remoteTuneUntil)
        {
            _remoteTuneUntil = default;
            _uiContext.Post(_ => _remoteTuneMenu.Text = "Allow browser tuning for 15 minutes…", null);
        }
        else if (_remoteTuneUntil > DateTimeOffset.UtcNow)
        {
            var minutes = Math.Max(1, (int)Math.Ceiling((_remoteTuneUntil - DateTimeOffset.UtcNow).TotalMinutes));
            _uiContext.Post(_ => _remoteTuneMenu.Text = $"Browser tuning allowed · {minutes} min", null);
        }
        var snapshot = _latestRadioSnapshot;
        if (!_liveEnabled || snapshot is null || !_credentialStore.Exists) return;
        try
        {
            var state = new LiveBrowserState(
                "live.state",
                Protocol.Version,
                snapshot.Sequence,
                DateTimeOffset.UtcNow,
                snapshot,
                _latestCandidateContext);
            try
            {
                await _liveCompanion.PublishAsync(state, cancellationToken).ConfigureAwait(false);
            }
            catch (InvalidDataException) when (state.Candidates is not null)
            {
                await _liveCompanion.PublishAsync(state with { Candidates = null }, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception error) when (IsTransient(error) || error is InvalidOperationException)
        {
            // Live state is transient. A later snapshot or heartbeat reconnects; it is never queued.
        }
    }

    private static string FormatFrequency(long frequencyHz) => frequencyHz >= 1_000_000
        ? $"{frequencyHz / 1_000_000d:0.000} MHz"
        : $"{frequencyHz / 1_000d:0.0} kHz";

    protected override void ExitThreadCore()
    {
        _notifyIcon.Visible = false;
        _candidateQuery?.Cancel();
        _candidateQuery?.Dispose();
        _syncTimer.Dispose();
        _liveHeartbeatTimer.Dispose();
        _pipeServer.ConnectionChanged -= HandleConnectionChanged;
        _pipeServer.MessageReceived -= HandleMessageReceived;
        _liveCompanion.TuneCommandReceived -= HandleRemoteTuneCommand;
        _pipeServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _liveCompanion.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _apiClient.Dispose();
        _offlineQueue.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _syncGate.Dispose();
        _httpClient.Dispose();
        _notifyIcon.Dispose();
        base.ExitThreadCore();
    }
}

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
    private readonly NotifyIcon _notifyIcon;
    private readonly BridgePipeServer _pipeServer;
    private readonly SynchronizationContext _uiContext;
    private readonly HttpClient _httpClient;
    private readonly AuthenticatedDeviceApiClient _apiClient;
    private readonly DeviceCredentialStore _credentialStore;
    private readonly BridgePreferencesStore _preferencesStore;
    private CancellationTokenSource? _candidateQuery;
    private ReceptionSetupContext? _receptionSetup;

    public BridgeApplicationContext()
    {
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _credentialStore = new DeviceCredentialStore();
        _preferencesStore = new BridgePreferencesStore();
        _httpClient = new HttpClient { BaseAddress = PairingApiClient.ProductionBaseUri };
        _apiClient = new AuthenticatedDeviceApiClient(_httpClient, _credentialStore);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Connect to DXNexus…", null, (_, _) => ShowPairing());
        menu.Items.Add("Reception setup…", null, (_, _) => ShowReceptionSetup());
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

        var snapshot = message.Payload.Deserialize<SequencedRadioSnapshot>(new JsonSerializerOptions(JsonSerializerDefaults.Web));
        if (snapshot is null)
        {
            return;
        }

        _uiContext.Post(_ =>
        {
            _notifyIcon.Text = $"DXNexus — {FormatFrequency(snapshot.Radio.FrequencyHz)}";
        }, null);
        _ = RefreshCandidatesAsync(snapshot);
    }

    private async Task ApplyWishlistCommandAsync(WishlistCommand command, long sequence)
    {
        try
        {
            var result = await _apiClient.SetWishlistAsync(new WishlistMutationRequest(
                Protocol.Version,
                command.ClientMutationId,
                command.Wishlisted ? "add" : "remove",
                command.Candidate.BroadcastId,
                command.Wishlisted ? StationMutationContext.FromCandidate(command.Candidate) : null));
            await SendCommandResultAsync(sequence, new CommandResult(
                result.ClientMutationId, "wishlist", true,
                result.Wishlisted ? "Added to Want to hear." : "Removed from Want to hear."));
        }
        catch (Exception error)
        {
            await SendCommandResultAsync(sequence, new CommandResult(
                command.ClientMutationId, "wishlist", false, $"Target update failed: {error.Message}"));
        }
    }

    private async Task ApplyLogbookCommandAsync(QuickLogCommand command, long sequence)
    {
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
            var result = await _apiClient.CreateLogbookEntryAsync(new LogbookMutationRequest(
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
                    "0.1.0", "0.1.0", 1))).ConfigureAwait(false);
            await SendCommandResultAsync(sequence, new CommandResult(
                result.ClientMutationId, "logbook", true,
                result.Created == false ? "Reception was already saved." : "Reception saved in DXNexus."));
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
        var availableTask = _apiClient.GetReceptionSetupAsync(cancellationToken);
        var preferencesTask = _preferencesStore.LoadAsync(cancellationToken);
        await Task.WhenAll(availableTask, preferencesTask).ConfigureAwait(false);
        var available = await availableTask.ConfigureAwait(false);
        var preferences = await preferencesTask.ConfigureAwait(false);
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
                await _pipeServer.SendAsync("context.candidates", response.Sequence, response, query.Token)
                    .ConfigureAwait(false);
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

    private static string FormatFrequency(long frequencyHz) => frequencyHz >= 1_000_000
        ? $"{frequencyHz / 1_000_000d:0.000} MHz"
        : $"{frequencyHz / 1_000d:0.0} kHz";

    protected override void ExitThreadCore()
    {
        _notifyIcon.Visible = false;
        _candidateQuery?.Cancel();
        _candidateQuery?.Dispose();
        _pipeServer.ConnectionChanged -= HandleConnectionChanged;
        _pipeServer.MessageReceived -= HandleMessageReceived;
        _pipeServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _apiClient.Dispose();
        _httpClient.Dispose();
        _notifyIcon.Dispose();
        base.ExitThreadCore();
    }
}

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
    private CancellationTokenSource? _candidateQuery;
    private ReceptionSetupContext? _receptionSetup;

    public BridgeApplicationContext()
    {
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _credentialStore = new DeviceCredentialStore();
        _httpClient = new HttpClient { BaseAddress = PairingApiClient.ProductionBaseUri };
        _apiClient = new AuthenticatedDeviceApiClient(_httpClient, _credentialStore);
        var menu = new ContextMenuStrip();
        menu.Items.Add("Connect to DXNexus…", null, (_, _) => ShowPairing());
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
        if (message.Type != "radio.snapshot")
        {
            return;
        }

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
            var setup = _receptionSetup;
            if (setup is null)
            {
                var available = await _apiClient.GetReceptionSetupAsync(query.Token).ConfigureAwait(false);
                var point = available.ListeningPoints.FirstOrDefault(item => item.IsDefault)
                    ?? available.ListeningPoints.FirstOrDefault();
                var receiver = available.Receivers.FirstOrDefault(item => item.IsDefault)
                    ?? available.Receivers.FirstOrDefault();
                if (point is null || receiver is null) return;
                setup = new ReceptionSetupContext(point.Id, receiver.Id);
                _receptionSetup = setup;
            }
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

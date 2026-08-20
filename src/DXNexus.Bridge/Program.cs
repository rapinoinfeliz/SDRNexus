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

    public BridgeApplicationContext()
    {
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
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
    }

    private static string FormatFrequency(long frequencyHz) => frequencyHz >= 1_000_000
        ? $"{frequencyHz / 1_000_000d:0.000} MHz"
        : $"{frequencyHz / 1_000d:0.0} kHz";

    protected override void ExitThreadCore()
    {
        _notifyIcon.Visible = false;
        _pipeServer.ConnectionChanged -= HandleConnectionChanged;
        _pipeServer.MessageReceived -= HandleMessageReceived;
        _pipeServer.DisposeAsync().AsTask().GetAwaiter().GetResult();
        _notifyIcon.Dispose();
        base.ExitThreadCore();
    }
}

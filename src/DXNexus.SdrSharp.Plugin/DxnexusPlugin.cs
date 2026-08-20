using SDRSharp.Common;
using DXNexus.LocalTransport;
using DXNexus.Plugin.Core;

namespace DXNexus.SdrSharp.Plugin;

public sealed class DxnexusPlugin : ISharpPlugin, ICanLazyLoadGui, ISupportStatus, IExtendedNameProvider
{
    private ISharpControl? _control;
    private SdrSharpHostAdapter? _host;
    private RadioStateCollector? _collector;
    private RadioStatePublisher? _publisher;
    private PipeRadioStateSink? _sink;
    private PluginPanel? _panel;
    private SynchronizationContext? _uiContext;
    private bool _closed;

    public string DisplayName => "DXNexus";
    public string Category => "Radio tools";
    public string MenuItemName => DisplayName;
    public bool IsActive => _panel?.Visible == true;

    public UserControl Gui
    {
        get
        {
            LoadGui();
            return _panel!;
        }
    }

    public void Initialize(ISharpControl control)
    {
        ArgumentNullException.ThrowIfNull(control);
        if (_control is not null)
        {
            throw new InvalidOperationException("DXNexus is already initialized.");
        }

        _control = control;
        _uiContext = SynchronizationContext.Current ?? new WindowsFormsSynchronizationContext();
        _host = new SdrSharpHostAdapter(control);
        _collector = new RadioStateCollector(_host);
        _sink = new PipeRadioStateSink(LocalPipeName.ForCurrentWindowsUser());
        _sink.TuneRequested += HandleTuneRequested;
        _publisher = new RadioStatePublisher(_collector, _sink);
        _publisher.Start();
    }

    private void HandleTuneRequested(object? sender, DXNexus.Contracts.RemoteTuneCommand command)
    {
        var context = _uiContext;
        if (context is null) return;
        context.Post(_ =>
        {
            var host = _host;
            var sink = _sink;
            if (host is null || sink is null) return;
            var result = host.ApplyTune(command);
            _ = SendTuneResultSafelyAsync(sink, result, command.ExpectedSequence);
        }, null);
    }

    private static async Task SendTuneResultSafelyAsync(PipeRadioStateSink sink, DXNexus.Contracts.RemoteTuneResult result, long sequence)
    {
        try { await sink.SendTuneResultAsync(result, sequence).ConfigureAwait(false); }
        catch (Exception error) when (error is IOException or InvalidOperationException or ObjectDisposedException) { }
    }

    public void LoadGui()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        _panel ??= new PluginPanel(
            _collector ?? throw new InvalidOperationException("DXNexus has not been initialized."),
            _publisher ?? throw new InvalidOperationException("DXNexus has not been initialized."),
            _sink ?? throw new InvalidOperationException("DXNexus has not been initialized."));
    }

    public void Close()
    {
        if (_closed)
        {
            return;
        }

        _closed = true;
        _panel?.Dispose();
        _panel = null;
        _publisher?.Dispose();
        _publisher = null;
        if (_sink is not null) _sink.TuneRequested -= HandleTuneRequested;
        _sink = null;
        _collector?.Dispose();
        _collector = null;
        _host = null;
        _control = null;
        _uiContext = null;
    }
}

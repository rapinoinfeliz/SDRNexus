using SDRSharp.Common;

namespace DXNexus.SdrSharp.Plugin;

public sealed class DxnexusPlugin : ISharpPlugin, ICanLazyLoadGui, ISupportStatus, IExtendedNameProvider
{
    private ISharpControl? _control;
    private PluginPanel? _panel;
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
    }

    public void LoadGui()
    {
        ObjectDisposedException.ThrowIf(_closed, this);
        _panel ??= new PluginPanel(_control ?? throw new InvalidOperationException("DXNexus has not been initialized."));
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
        _control = null;
    }
}


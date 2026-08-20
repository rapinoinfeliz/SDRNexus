namespace DXNexus.Plugin.Core;

public interface IRadioHost : IDisposable
{
    event EventHandler? StateChanged;

    RadioHostSnapshot CaptureSnapshot();
}


namespace DXNexus.Plugin.Core;

using DXNexus.Contracts;

public interface IRadioHost : IDisposable
{
    event EventHandler? StateChanged;

    RadioHostSnapshot CaptureSnapshot();
}

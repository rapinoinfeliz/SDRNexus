using System.ComponentModel;
using DXNexus.Plugin.Core;
using SDRSharp.Common;
using SDRSharp.Radio;

namespace DXNexus.SdrSharp.Plugin;

internal sealed class SdrSharpHostAdapter : IRadioHost
{
    private static readonly HashSet<string> RelevantProperties = new(StringComparer.Ordinal)
    {
        nameof(ISharpControl.Frequency),
        nameof(ISharpControl.CenterFrequency),
        nameof(ISharpControl.DetectorType),
        nameof(ISharpControl.FilterBandwidth),
        nameof(ISharpControl.RFDisplayBandwidth),
        nameof(ISharpControl.InputSampleRate),
        nameof(ISharpControl.IsPlaying),
        nameof(ISharpControl.SourceName),
        nameof(ISharpControl.VisualSNR),
        nameof(ISharpControl.VisualPeak),
        nameof(ISharpControl.VisualFloor),
        nameof(ISharpControl.RdsPICode),
        nameof(ISharpControl.RdsProgramService),
        nameof(ISharpControl.RdsRadioText),
    };

    private readonly ISharpControl _control;
    private bool _disposed;

    public SdrSharpHostAdapter(ISharpControl control)
    {
        _control = control ?? throw new ArgumentNullException(nameof(control));
        _control.PropertyChanged += HandlePropertyChanged;
    }

    public event EventHandler? StateChanged;

    public RadioHostSnapshot CaptureSnapshot()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        return new RadioHostSnapshot(
            _control.Frequency,
            _control.CenterFrequency,
            MapDetector(_control.DetectorType),
            _control.FilterBandwidth,
            _control.RFDisplayBandwidth,
            SafeSampleRate(_control.InputSampleRate),
            _control.IsPlaying,
            EmptyAsNull(_control.SourceName),
            new RelativeSignalMetrics(
                _control.VisualSNR,
                _control.VisualPeak,
                _control.VisualFloor),
            new RdsSnapshot(
                _control.RdsPICode == 0 ? null : _control.RdsPICode.ToString("X4"),
                EmptyAsNull(_control.RdsProgramService),
                EmptyAsNull(_control.RdsRadioText)));
    }

    private void HandlePropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (_disposed || (args.PropertyName is not null && !RelevantProperties.Contains(args.PropertyName)))
        {
            return;
        }

        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    private static RadioDetector MapDetector(DetectorType detector) => detector switch
    {
        DetectorType.WFM => RadioDetector.Wfm,
        DetectorType.NFM => RadioDetector.Nfm,
        DetectorType.AM => RadioDetector.Am,
        DetectorType.DSB => RadioDetector.Dsb,
        DetectorType.LSB => RadioDetector.Lsb,
        DetectorType.USB => RadioDetector.Usb,
        DetectorType.CW => RadioDetector.Cw,
        DetectorType.RAW => RadioDetector.Raw,
        _ => RadioDetector.Unknown,
    };

    private static int SafeSampleRate(double sampleRate)
    {
        return !double.IsFinite(sampleRate) || sampleRate <= 0
            ? 0
            : (int)Math.Min(int.MaxValue, Math.Round(sampleRate));
    }

    private static string? EmptyAsNull(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _control.PropertyChanged -= HandlePropertyChanged;
    }
}


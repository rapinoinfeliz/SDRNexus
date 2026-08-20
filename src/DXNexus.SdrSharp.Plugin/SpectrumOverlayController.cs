using DXNexus.Contracts;
using DXNexus.Plugin.Core;
using SDRSharp.Common;
using SDRSharp.PanView;

namespace DXNexus.SdrSharp.Plugin;

internal sealed class SpectrumOverlayController : IDisposable
{
    private readonly ISharpControl _control;
    private readonly PipeRadioStateSink _sink;
    private readonly SynchronizationContext _uiContext;
    private readonly Font _font = new(SystemFonts.MessageBoxFont!.FontFamily, 8f, FontStyle.Bold, GraphicsUnit.Point);
    private readonly Pen _standardPen = new(Color.FromArgb(205, 80, 205, 255), 1.4f);
    private readonly Pen _receivedPen = new(Color.FromArgb(220, 74, 226, 142), 1.7f);
    private readonly Pen _targetPen = new(Color.FromArgb(220, 255, 101, 128), 1.7f);
    private readonly Brush _labelBackground = new SolidBrush(Color.FromArgb(210, 10, 27, 39));
    private readonly Brush _standardBrush = new SolidBrush(Color.FromArgb(235, 146, 222, 255));
    private readonly Brush _receivedBrush = new SolidBrush(Color.FromArgb(240, 112, 242, 174));
    private readonly Brush _targetBrush = new SolidBrush(Color.FromArgb(240, 255, 142, 161));
    private SpectrumOverlayMarker[] _markers = [];
    private bool _enabled = true;
    private bool _disposed;

    public SpectrumOverlayController(ISharpControl control, PipeRadioStateSink sink, SynchronizationContext uiContext)
    {
        _control = control;
        _sink = sink;
        _uiContext = uiContext;
        _sink.CandidatesReceived += HandleCandidatesReceived;
        _control.SpectrumAnalyzerCustomPaint += HandlePaint;
    }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            Invalidate();
        }
    }

    private void HandleCandidatesReceived(object? sender, CandidateContextResponse response)
    {
        _markers = SpectrumOverlayModel.Build(response);
        Invalidate();
    }

    private void Invalidate()
    {
        if (_disposed) return;
        _uiContext.Post(_ =>
        {
            if (!_disposed) _control.InvalidateSpectrumGraphics();
        }, null);
    }

    private void HandlePaint(object sender, CustomPaintEventArgs args)
    {
        if (_disposed || !_enabled || _markers.Length == 0) return;
        var bounds = args.Graphics.VisibleClipBounds;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        var center = _control.CenterFrequency;
        var bandwidth = _control.RFDisplayBandwidth;
        for (var index = 0; index < _markers.Length; index++)
        {
            var marker = _markers[index];
            var relativeX = SpectrumOverlayModel.PixelForFrequency(marker.FrequencyHz, center, bandwidth, bounds.Width);
            if (relativeX is not float mapped) continue;
            var x = bounds.Left + mapped;
            var pen = marker.Emphasis switch
            {
                SpectrumMarkerEmphasis.Received => _receivedPen,
                SpectrumMarkerEmphasis.Wishlisted => _targetPen,
                _ => _standardPen,
            };
            var brush = marker.Emphasis switch
            {
                SpectrumMarkerEmphasis.Received => _receivedBrush,
                SpectrumMarkerEmphasis.Wishlisted => _targetBrush,
                _ => _standardBrush,
            };
            args.Graphics.DrawLine(pen, x, bounds.Top + 2, x, bounds.Bottom - 2);
            var text = marker.Label + " · " + marker.Detail;
            var size = args.Graphics.MeasureString(text, _font);
            var labelX = Math.Clamp(x + 4, bounds.Left + 2, Math.Max(bounds.Left + 2, bounds.Right - size.Width - 8));
            var labelY = bounds.Top + 5 + index % 3 * (size.Height + 3);
            args.Graphics.FillRectangle(_labelBackground, labelX - 3, labelY - 1, size.Width + 6, size.Height + 2);
            args.Graphics.DrawString(text, _font, brush, labelX, labelY);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _sink.CandidatesReceived -= HandleCandidatesReceived;
        _control.SpectrumAnalyzerCustomPaint -= HandlePaint;
        _font.Dispose();
        _standardPen.Dispose();
        _receivedPen.Dispose();
        _targetPen.Dispose();
        _labelBackground.Dispose();
        _standardBrush.Dispose();
        _receivedBrush.Dispose();
        _targetBrush.Dispose();
    }
}

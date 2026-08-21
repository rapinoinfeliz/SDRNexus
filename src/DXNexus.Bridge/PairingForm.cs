using DXNexus.Bridge.Core;
using System.Diagnostics;

namespace DXNexus.Bridge;

internal sealed class PairingForm : Form
{
    private readonly Label _code = new();
    private readonly Label _status = new();
    private readonly Button _openBrowser = new();
    private readonly CancellationTokenSource _lifetime = new();
    private readonly DeviceCredentialStore _credentialStore = new();
    private Uri? _verificationUri;

    public PairingForm()
    {
        Text = "Connect SDRNexus to DXNexus";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(430, 230);
        Padding = new Padding(24);

        var title = new Label
        {
            Text = "Connect to DXNexus",
            AutoSize = true,
            Font = new Font(Font.FontFamily, 15, FontStyle.Bold),
        };
        var instructions = new Label
        {
            Text = "Approve this Windows device from your private DXNexus account.",
            AutoSize = true,
            MaximumSize = new Size(375, 0),
            ForeColor = SystemColors.GrayText,
        };
        _code.Text = "Preparing secure code…";
        _code.AutoSize = true;
        _code.Font = new Font(FontFamily.GenericMonospace, 17, FontStyle.Bold);
        _code.Margin = new Padding(0, 15, 0, 4);
        _status.Text = "Creating an ephemeral P-256 device key.";
        _status.AutoSize = true;
        _status.MaximumSize = new Size(375, 0);
        _status.ForeColor = SystemColors.GrayText;
        _openBrowser.Text = "Open DXNexus";
        _openBrowser.AutoSize = true;
        _openBrowser.Enabled = false;
        _openBrowser.Click += (_, _) => OpenVerificationPage();
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 14, 0, 0),
        };
        buttons.Controls.Add(_openBrowser);
        buttons.Controls.Add(cancel);
        var layout = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoScroll = false,
        };
        layout.Controls.Add(title);
        layout.Controls.Add(instructions);
        layout.Controls.Add(_code);
        layout.Controls.Add(_status);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        Shown += async (_, _) => await RunPairingAsync();
    }

    private async Task RunPairingAsync()
    {
        using var httpClient = new HttpClient { BaseAddress = PairingApiClient.ProductionBaseUri };
        var client = new PairingApiClient(httpClient);
        PairingSession? pairing = null;
        try
        {
            pairing = await client.StartAsync(
                $"{Environment.MachineName} · SDR#",
                new PairingClientInfo("0.1.0", "0.1.0", 1),
                _lifetime.Token);
            _verificationUri = pairing.Start.VerificationUri;
            _code.Text = pairing.Start.UserCode;
            _status.Text = "Enter this code in DXNexus → Account → Connected devices.";
            _openBrowser.Enabled = true;
            OpenVerificationPage();

            while (!_lifetime.IsCancellationRequested)
            {
                try
                {
                    var credential = await client.PollAsync(pairing, _lifetime.Token);
                    await _credentialStore.SaveAsync(credential, _lifetime.Token);
                    _status.Text = "Connected securely. You can close this window.";
                    _status.ForeColor = Color.SeaGreen;
                    _openBrowser.Text = "Connected";
                    _openBrowser.Enabled = false;
                    DialogResult = DialogResult.OK;
                    return;
                }
                catch (PairingPendingException pending)
                {
                    _status.Text = "Waiting for approval in DXNexus…";
                    await Task.Delay(pending.RetryAfter, _lifetime.Token);
                }
            }
        }
        catch (OperationCanceledException) when (_lifetime.IsCancellationRequested)
        {
            // The user closed the dialog.
        }
        catch (Exception error)
        {
            _status.Text = $"Connection failed: {error.Message}";
            _status.ForeColor = Color.Firebrick;
            _openBrowser.Enabled = _verificationUri is not null;
        }
        finally
        {
            pairing?.Dispose();
        }
    }

    private void OpenVerificationPage()
    {
        if (_verificationUri is null) return;
        Process.Start(new ProcessStartInfo(_verificationUri.ToString()) { UseShellExecute = true });
    }

    protected override void OnFormClosed(FormClosedEventArgs eventArgs)
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
        base.OnFormClosed(eventArgs);
    }
}

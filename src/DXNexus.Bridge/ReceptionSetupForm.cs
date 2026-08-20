using DXNexus.Bridge.Core;
using DXNexus.Contracts;

namespace DXNexus.Bridge;

internal sealed class ReceptionSetupForm : Form
{
    private readonly AuthenticatedDeviceApiClient _apiClient;
    private readonly BridgePreferencesStore _preferencesStore;
    private readonly ComboBox _listeningPoint = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 350 };
    private readonly ComboBox _receiver = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 350 };
    private readonly Label _status = new() { AutoSize = true, MaximumSize = new Size(350, 0) };
    private readonly Button _save = new() { Text = "Save setup", AutoSize = true, Enabled = false };

    public ReceptionSetupForm(AuthenticatedDeviceApiClient apiClient, BridgePreferencesStore preferencesStore)
    {
        _apiClient = apiClient;
        _preferencesStore = preferencesStore;
        Text = "DXNexus reception setup";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(430, 300);
        Padding = new Padding(24);

        var title = new Label { Text = "Reception setup", AutoSize = true, Font = new Font(Font.FontFamily, 15, FontStyle.Bold) };
        var copy = new Label
        {
            Text = "Choose which saved DXNexus location and receiver should be used to rank stations in SDR#.",
            AutoSize = true,
            MaximumSize = new Size(365, 0),
            ForeColor = SystemColors.GrayText,
        };
        var pointLabel = new Label { Text = "Listening point", AutoSize = true, Margin = new Padding(0, 16, 0, 3) };
        var receiverLabel = new Label { Text = "Receiver", AutoSize = true, Margin = new Padding(0, 12, 0, 3) };
        _status.ForeColor = SystemColors.GrayText;
        _status.Margin = new Padding(0, 12, 0, 0);
        _save.Click += async (_, _) => await SaveAsync();
        var close = new Button { Text = "Cancel", AutoSize = true };
        close.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight, Margin = new Padding(0, 12, 0, 0) };
        buttons.Controls.Add(_save);
        buttons.Controls.Add(close);
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
        layout.Controls.Add(title);
        layout.Controls.Add(copy);
        layout.Controls.Add(pointLabel);
        layout.Controls.Add(_listeningPoint);
        layout.Controls.Add(receiverLabel);
        layout.Controls.Add(_receiver);
        layout.Controls.Add(_status);
        layout.Controls.Add(buttons);
        Controls.Add(layout);
        Shown += async (_, _) => await LoadSetupAsync();
    }

    public ReceptionSetupContext? SelectedSetup { get; private set; }

    private async Task LoadSetupAsync()
    {
        try
        {
            _status.Text = "Loading saved DXNexus setup…";
            var availableTask = _apiClient.GetReceptionSetupAsync();
            var preferencesTask = _preferencesStore.LoadAsync();
            await Task.WhenAll(availableTask, preferencesTask);
            var available = await availableTask;
            var preferences = await preferencesTask;
            _listeningPoint.DataSource = available.ListeningPoints;
            _listeningPoint.DisplayMember = nameof(SavedListeningPoint.Name);
            _receiver.DataSource = available.Receivers;
            _receiver.DisplayMember = nameof(SavedReceiverProfile.Name);
            _listeningPoint.SelectedItem = available.ListeningPoints.FirstOrDefault(item => item.Id == preferences.ListeningPointId)
                ?? available.ListeningPoints.FirstOrDefault(item => item.IsDefault)
                ?? available.ListeningPoints.FirstOrDefault();
            _receiver.SelectedItem = available.Receivers.FirstOrDefault(item => item.Id == preferences.ReceiverProfileId)
                ?? available.Receivers.FirstOrDefault(item => item.IsDefault)
                ?? available.Receivers.FirstOrDefault();
            _save.Enabled = _listeningPoint.SelectedItem is not null && _receiver.SelectedItem is not null;
            _status.Text = _save.Enabled ? "This setup is used for distance, bearing and reception estimates." : "Create a Listening Point and receiver in DXNexus first.";
        }
        catch (Exception error)
        {
            _status.Text = $"Could not load setup: {error.Message}";
            _status.ForeColor = Color.Firebrick;
        }
    }

    private async Task SaveAsync()
    {
        if (_listeningPoint.SelectedItem is not SavedListeningPoint point
            || _receiver.SelectedItem is not SavedReceiverProfile receiver) return;
        _save.Enabled = false;
        var existing = await _preferencesStore.LoadAsync();
        await _preferencesStore.SaveAsync(existing with { ListeningPointId = point.Id, ReceiverProfileId = receiver.Id });
        SelectedSetup = new ReceptionSetupContext(point.Id, receiver.Id);
        DialogResult = DialogResult.OK;
        Close();
    }
}

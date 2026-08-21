using DXNexus.Contracts;

namespace DXNexus.SdrSharp.Plugin;

internal sealed class QuickLogForm : Form
{
    private readonly ComboBox _quality = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly ComboBox _identification = new() { DropDownStyle = ComboBoxStyle.DropDownList, Width = 210 };
    private readonly CheckedListBox _methods = new() { Height = 82, Width = 260, CheckOnClick = true };
    private readonly TextBox _notes = new() { Multiline = true, Width = 310, Height = 55 };

    public QuickLogForm(StationCandidate candidate)
    {
        Text = "Log reception in DXNexus";
        StartPosition = FormStartPosition.CenterParent;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(390, 480);
        _quality.Items.AddRange(["Barely audible", "Poor", "Fair", "Good", "Excellent"]);
        _quality.SelectedItem = "Good";
        _identification.Items.AddRange(["Confirmed", "Probable", "Tentative", "Unidentified"]);
        _identification.SelectedItem = "Confirmed";
        _methods.Items.AddRange(["Station ID", "RDS PI", "RDS PS", "RDS RT", "Official stream", "Program content", "Schedule", "Other"]);
        _methods.SetItemChecked(0, true);

        var title = new Label { AutoSize = true, Font = new Font(Font, FontStyle.Bold), Text = candidate.StationName };
        var context = new Label
        {
            AutoSize = true,
            ForeColor = SystemColors.GrayText,
            Text = $"{FormatFrequency(candidate.FrequencyHz)} · {candidate.DistanceKm:0.#} km · {candidate.BearingDeg:0}° {candidate.BearingCardinal}",
        };
        var save = new Button { Text = "Save reception", AutoSize = true, DialogResult = DialogResult.OK };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        AcceptButton = save;
        CancelButton = cancel;

        var buttons = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(20, 8, 20, 20),
            WrapContents = false,
        };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);

        var layout = new FlowLayoutPanel
        {
            AutoScroll = true,
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            Padding = new Padding(20, 20, 20, 0),
            WrapContents = false,
        };
        layout.Controls.Add(title);
        layout.Controls.Add(context);
        layout.Controls.Add(FieldLabel("Signal quality"));
        layout.Controls.Add(_quality);
        layout.Controls.Add(FieldLabel("Identification status"));
        layout.Controls.Add(_identification);
        layout.Controls.Add(FieldLabel("Identification method"));
        layout.Controls.Add(_methods);
        layout.Controls.Add(FieldLabel("Notes (optional)"));
        layout.Controls.Add(_notes);

        var root = new TableLayoutPanel
        {
            ColumnCount = 1,
            Dock = DockStyle.Fill,
            RowCount = 2,
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.Controls.Add(layout, 0, 0);
        root.Controls.Add(buttons, 0, 1);
        Controls.Add(root);
    }

    public string SignalQuality => Slug(_quality.SelectedItem?.ToString() ?? "good");
    public string IdentificationStatus => Slug(_identification.SelectedItem?.ToString() ?? "confirmed");
    public string[] IdentificationMethods => _methods.CheckedItems.Cast<string>().Select(Slug).ToArray();
    public string? Notes => string.IsNullOrWhiteSpace(_notes.Text) ? null : _notes.Text.Trim();

    private static Label FieldLabel(string text) => new() { Text = text, AutoSize = true, Margin = new Padding(0, 12, 0, 2) };
    private static string Slug(string value) => value.Trim().ToLowerInvariant().Replace(' ', '-');
    private static string FormatFrequency(long frequencyHz) => frequencyHz >= 1_000_000
        ? $"{frequencyHz / 1_000_000d:0.000} MHz"
        : $"{frequencyHz / 1_000d:0.0} kHz";
}

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
        ClientSize = new Size(370, 390);
        Padding = new Padding(20);
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
        var save = new Button { Text = "Save reception", AutoSize = true };
        save.Click += (_, _) => { DialogResult = DialogResult.OK; Close(); };
        var cancel = new Button { Text = "Cancel", AutoSize = true };
        cancel.Click += (_, _) => Close();
        var buttons = new FlowLayoutPanel { AutoSize = true, FlowDirection = FlowDirection.LeftToRight };
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        var layout = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false };
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
        layout.Controls.Add(buttons);
        Controls.Add(layout);
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

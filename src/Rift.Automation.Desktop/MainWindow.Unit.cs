using System.Globalization;
using System.Windows;
using Microsoft.Win32;
using Rift.Automation.Windows;

namespace Rift.Automation.Desktop;

public partial class MainWindow
{
    private void OpenUnitClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open a manual RIFT unit export",
            Filter = "RAC unit exports (*.tsv)|*.tsv|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
            return;
        ApplyUnitResult(ManualUnitSnapshotReader.OpenFile(dialog.FileName));
    }

    private void ApplyUnitResult(ManualUnitReadResult result)
    {
        if (!result.Success || result.Snapshot is not { } snapshot)
        {
            UnitSummaryText.Text = "File rejected: " + result.ErrorCode +
                ". Previous imported fields have been cleared.";
            UnitFieldsText.Text = "No imported fields.";
            return;
        }

        UnitSummaryText.Text = "Saved manual " + snapshot.Subject + " record: " +
            snapshot.RecordStatus + ". No collection timestamp or live connection.";
        string Number(ManualNumber item) => item.Value is double n
            ? n.ToString("G17", CultureInfo.InvariantCulture)
            : item.Status + (item.Reason is null ? "" : " (" + item.Reason + ")");
        string Flag(ManualFlag item) => item.Value is bool b
            ? (b ? "true" : "false")
            : item.Status + (item.Reason is null ? "" : " (" + item.Reason + ")");
        UnitFieldsText.Text = "Addon version: " + snapshot.AddonVersion +
            "\nSequence: " + snapshot.Sequence.ToString(CultureInfo.InvariantCulture) +
            "\nName: " + (snapshot.Name ?? "unavailable") +
            "\nX: " + Number(snapshot.X) +
            "\nY: " + Number(snapshot.Y) +
            "\nZ: " + Number(snapshot.Z) +
            "\nCombat: " + Flag(snapshot.Combat) +
            "\nComplete XYZ: " + (snapshot.HasCompletePosition ? "yes" : "no") +
            "\nSample age: unknown" +
            "\nAutomate: no";
    }

    internal bool RunUnitSmokeCheck()
    {
        ObserverTabs.SelectedItem = UnitTab;
        var sample = new ManualUnitSnapshot(
            "ManualSavedFile", "player", "available", null, "0.5.0", 7, "fixture",
            new("present", 0, null), new("missing", null, "api_nil"),
            new("present", -5, null), new("present", false, null));
        ApplyUnitResult(new(true, "", sample));
        bool partial = UnitFieldsText.Text.Contains("X: 0", StringComparison.Ordinal)
            && UnitFieldsText.Text.Contains("Y: missing", StringComparison.Ordinal)
            && UnitFieldsText.Text.Contains("Complete XYZ: no", StringComparison.Ordinal)
            && UnitFieldsText.Text.Contains("Automate: no", StringComparison.Ordinal);
        ApplyUnitResult(new(false, "invalid_tsv", null));
        bool cleared = UnitFieldsText.Text == "No imported fields."
            && UnitSummaryText.Text.Contains("invalid_tsv", StringComparison.Ordinal);
        return partial && cleared;
    }
}

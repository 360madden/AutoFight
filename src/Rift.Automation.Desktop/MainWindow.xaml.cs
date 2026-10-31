using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;
using Rift.Automation.Core;
using Rift.Automation.Windows;

namespace Rift.Automation.Desktop;

public partial class MainWindow : Window
{
    private ReplayScenario _scenario;
    private ReplayPlayer _player;

    public MainWindow(ReplayScenario scenario,
        Func<CancellationToken, Task<ClientInventorySnapshot>>? scanClients = null)
    {
        ArgumentNullException.ThrowIfNull(scenario);
        _scenario = scenario;
        _player = new ReplayPlayer(scenario);
        _scanClients = scanClients ?? (token => Task.Run(() => RiftClientInventory.Scan(token), token));
        InitializeComponent();
        Refresh();
    }

    private void NextClicked(object sender, RoutedEventArgs e)
    {
        _player.Step();
        Refresh();
    }

    private void ResetClicked(object sender, RoutedEventArgs e)
    {
        _player.Reset();
        Refresh();
    }

    private void DemoClicked(object sender, RoutedEventArgs e)
    {
        Load(DemoReplay.Create());
    }

    private void OpenClicked(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open a synthetic replay",
            Filter = "Replay JSON (*.json)|*.json",
            CheckFileExists = true,
            Multiselect = false
        };
        if (dialog.ShowDialog(this) != true)
        {
            return;
        }

        ReplayReadResult result = App.ReadReplay(dialog.FileName);
        if (!result.Success || result.Scenario is null)
        {
            ResultText.Text = $"File rejected ({result.ErrorCode}); the current replay is unchanged.";
            return;
        }
        Load(result.Scenario);
    }

    private void Load(ReplayScenario scenario)
    {
        _scenario = scenario;
        _player = new ReplayPlayer(scenario);
        Refresh();
    }

    private void Refresh()
    {
        PositionView view = _player.View;
        ScenarioText.Text = _scenario.Name;
        ProgressText.Text = $"Step {_player.Index}/{_player.Count}  •  Replay clock {_player.NowMs} ms  •  Revision {view.Revision}";
        IdentityText.Text = $"Session: {view.SessionId}  •  Build: {view.BuildId}  •  Epoch: {view.Epoch}";
        StatusText.Text = view.Status.ToString();
        StatusText.Foreground = view.Status switch
        {
            ViewStatus.Present => Brushes.LightGreen,
            ViewStatus.Stale or ViewStatus.Gap => Brushes.Khaki,
            ViewStatus.Invalid or ViewStatus.Error => Brushes.LightCoral,
            _ => Brushes.LightSteelBlue
        };
        PositionText.Text = view.Position is PlayerPosition position
            ? Format(position)
            : "X: unknown   Y: unknown   Z: unknown";
        LastKnownText.Text = view.LastKnownPosition is PlayerPosition last
            ? $"Last known (diagnostic only): {Format(last)}"
            : "Last known: unavailable";
        AgeText.Text = $"Acquisition age: {Age(view.AcquisitionAgeMs)}  •  Receipt age: {Age(view.ReceiveAgeMs)}";
        SourceText.Text = $"Source: {view.SourceId}  •  Origin: {view.Mode}";
        ReasonText.Text = $"Reason: {view.Reason}";
        ObservationCapabilityText.Text = view.CanObservePosition
            ? "Synthetic position observation: available in this replay."
            : "Synthetic position observation: unavailable for the current replay state.";
        ResultText.Text = $"Last transition: {_player.LastResultCode}";
        NextButton.IsEnabled = _player.CanStep;
    }

    private static string Age(long? value) =>
        value is long age ? age.ToString(CultureInfo.InvariantCulture) + " ms" : "unavailable";

    private static string Format(PlayerPosition position) =>
        string.Create(CultureInfo.InvariantCulture, $"X: {position.X:0.###}   Y: {position.Y:0.###}   Z: {position.Z:0.###}");

    // Exercises real XAML construction and presenter controls without showing a window.
    internal bool RunSmokeCheck()
    {
        ShowReplay();
        Measure(new Size(900, 690));
        Arrange(new Rect(0, 0, 900, 690));
        UpdateLayout();
        if (!ModeBanner.Text.Contains("SYNTHETIC REPLAY", StringComparison.Ordinal)
            || _player.Index != 0 || !NextButton.IsEnabled || _player.View.CanAutomate)
        {
            return false;
        }

        int count = _player.Count;
        for (int index = 0; index < count; index++)
        {
            NextClicked(this, new RoutedEventArgs());
            if (_player.Index != index + 1 || _player.View.CanAutomate)
            {
                return false;
            }
        }
        if (_player.CanStep || NextButton.IsEnabled)
        {
            return false;
        }

        ResetClicked(this, new RoutedEventArgs());
        return _player.Index == 0 && _player.NowMs == 0
            && _player.View.Position is null && NextButton.IsEnabled;
    }
}

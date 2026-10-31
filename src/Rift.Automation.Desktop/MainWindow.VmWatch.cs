using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Rift.Automation.Windows;

namespace Rift.Automation.Desktop;

public partial class MainWindow
{
    private async void VmWatchStartClicked(object sender, RoutedEventArgs e)
    {
        if (_closing) return;
        if (!int.TryParse((VmWatchDuration.SelectedItem as ComboBoxItem)?.Tag?.ToString(),
                NumberStyles.None, CultureInfo.InvariantCulture, out int seconds)
            || seconds is not (15 or 30 or 60))
        {
            VmWatchSummary.Text = "Choose a 15-, 30-, or 60-second watch.";
            return;
        }

        // Fixed paths for the duration: editing a source pin does not silently
        // switch the reader under an already-running watch.
        string lab1Pin = VmLab1Pin.Text;
        string lab2Pin = VmLab2Pin.Text;
        var one = new VmTelemetryWatchTracker("lab1");
        var two = new VmTelemetryWatchTracker("lab2");
        CancellationTokenSource? cancellation = TryBeginVmOperation(VmOperation.ShortWatch);
        if (cancellation is null) return;
        VmWatchSummary.Text = "Watching both labs. This does not send game input.";

        var clock = Stopwatch.StartNew();
        bool stopped = false;
        try
        {
            while (!_closing && !cancellation.IsCancellationRequested)
            {
                var first = Task.Run(() => VmTelemetryReader.ReadPinned(lab1Pin), cancellation.Token);
                var second = Task.Run(() => VmTelemetryReader.ReadPinned(lab2Pin), cancellation.Token);
                await Task.WhenAll(first, second);
                if (_closing || cancellation.IsCancellationRequested) break;
                long elapsedMs = clock.ElapsedMilliseconds;
                VmTelemetryWatchReport status1 = one.Observe(first.Result, elapsedMs);
                VmTelemetryWatchReport status2 = two.Observe(second.Result, elapsedMs);
                VmWatchLab1.Text = WatchLine(status1);
                VmWatchLab2.Text = WatchLine(status2);
                VmWatchSummary.Text = string.Create(CultureInfo.InvariantCulture,
                    $"Read-only watch {elapsedMs / 1000d:0.0}/{seconds}s. " +
                    $"New samples require both outer and addon sequences to advance.");
                if (elapsedMs >= seconds * 1000L) break;
                int delayMs = (int)Math.Min(500, seconds * 1000L - elapsedMs);
                await Task.Delay(delayMs, cancellation.Token);
            }
        }
        catch (OperationCanceledException)
        {
            stopped = true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException
            or ArgumentException or InvalidOperationException)
        {
            if (!_closing) VmWatchSummary.Text = "Watch stopped: failed to inspect a telemetry stream.";
            stopped = true;
        }
        finally
        {
            clock.Stop();
            if (!_closing)
            {
                var current1 = one.View();
                var current2 = two.View();
                VmWatchLab1.Text = WatchLine(current1);
                VmWatchLab2.Text = WatchLine(current2);
                VmWatchSummary.Text = string.Create(CultureInfo.InvariantCulture,
                    $"{(stopped || cancellation.IsCancellationRequested ? "Stopped" : "Completed")} " +
                    $"after {clock.Elapsed.TotalSeconds:0.0}s. " +
                    $"Lab 1 new {current1.NewSamples}, longest without advance " +
                    $"{current1.LongestNoAdvanceMs / 1000d:0.0}s; " +
                    $"Lab 2 new {current2.NewSamples}, longest without advance " +
                    $"{current2.LongestNoAdvanceMs / 1000d:0.0}s. " +
                    $"Diagnostic only; this is not sustained movement qualification.");
            }
            EndVmOperation(VmOperation.ShortWatch, cancellation);
        }
    }

    private static string WatchLine(VmTelemetryWatchReport report)
    {
        string summary = string.Create(CultureInfo.InvariantCulture,
            $"{report.Lab}: {report.State} ({report.Reason}). " +
            $"New {report.NewSamples}, repeats {report.Repeated}, unavailable " +
            $"{report.Unavailable}, conflicts {report.Reordered}, " +
            $"stream changes {report.SessionChanges}, " +
            $"last advance {report.SinceLastAdvanceMs} ms.");
        if (report.Current is not { HasPosition: true } s) return summary + " No current XYZ.";
        return summary + string.Create(CultureInfo.InvariantCulture,
            $" X {s.X:0.###}, Y {s.Y:0.###}, Z {s.Z:0.###} " +
            $"({s.SourceScope}, outer {s.OuterSequence}, addon {s.AddonSequence}).");
    }

    private void VmWatchStopClicked(object sender, RoutedEventArgs e)
    {
        VmWatchStopButton.IsEnabled = false;
        if (_vmOperation == VmOperation.ShortWatch) CancelVmOperation();
    }

}

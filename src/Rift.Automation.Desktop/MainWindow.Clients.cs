using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Rift.Automation.Core;
using Rift.Automation.Windows;

namespace Rift.Automation.Desktop;

public partial class MainWindow
{
    private readonly Func<CancellationToken, Task<ClientInventorySnapshot>> _scanClients;
    private CancellationTokenSource? _clientRefresh;
    private bool _closing;

    private async void RefreshClientsClicked(object sender, RoutedEventArgs e) => await RefreshClientsAsync();

    private async Task RefreshClientsAsync()
    {
        if (_closing || _clientRefresh is not null) return;
        var refresh = new CancellationTokenSource();
        _clientRefresh = refresh;
        RefreshClientsButton.IsEnabled = false;
        ClearClients();
        ClientSummaryText.Text = "Checking local RIFT processes...";
        try
        {
            ClientInventorySnapshot snapshot = await _scanClients(refresh.Token);
            if (!_closing && !refresh.IsCancellationRequested)
                ApplyClients(snapshot);
        }
        catch (OperationCanceledException)
        {
            if (!_closing) ClientSummaryText.Text = "Client refresh cancelled.";
        }
        catch (Exception ex)
        {
            // UI boundary: a failed inventory must not leave old selections looking current.
            System.Diagnostics.Debug.WriteLine(ex);
            if (!_closing) ClientSummaryText.Text = "Client inspection failed. Refresh to retry.";
        }
        finally
        {
            _clientRefresh = null;
            refresh.Dispose();
            if (!_closing) RefreshClientsButton.IsEnabled = true;
        }
    }

    private void ClearClients()
    {
        ClientsList.ItemsSource = null;
        ClientDetailsText.Text = "No client selected.";
    }

    private void ApplyClients(ClientInventorySnapshot snapshot)
    {
        ClearClients();
        if (snapshot.ErrorCode is not null)
        {
            ClientSummaryText.Text = "Windows could not list RIFT processes. Refresh to retry.";
            return;
        }
        ClientsList.ItemsSource = snapshot.Clients;
        string result = snapshot.Clients.Count == 0
            ? "No RIFT client detected on host Windows. VMware guest clients are not included."
            : $"{snapshot.Clients.Count} candidate client(s). Select a row for details.";
        ClientSummaryText.Text = $"{result}\nChecked {snapshot.CheckedAtUtc.ToLocalTime():HH:mm:ss} | {snapshot.ElapsedMs:0.#} ms"
            + (snapshot.Truncated ? " | List limited to 64 clients." : "");
    }

    private void ClientSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ClientDetailsText is null) return; // XAML construction may raise selection changes.
        if (ClientsList.SelectedItem is not RiftClientInfo client)
        {
            ClientDetailsText.Text = "No client selected.";
            return;
        }
        string start = client.StartedAtUtc?.ToString("u", CultureInfo.InvariantCulture) ?? "unavailable";
        string handle = client.WindowHandle is long h ? $"0x{h:X}" : "unavailable";
        ClientDetailsText.Text = $"PID: {client.ProcessId} | Status: {client.Status}\n"
            + $"Started (UTC): {start}\nWindows session: {client.WindowsSessionId?.ToString(CultureInfo.InvariantCulture) ?? "unavailable"}\n"
            + $"Window: {handle}\nExecutable: {client.ImagePath ?? "unavailable"}\n"
            + $"Identity: {client.Identity ?? "unavailable"}\n{client.Detail}\n"
            + "Game-data provider: not connected. Build compatibility: not checked.";
    }

    internal void ShowReplay() => ObserverTabs.SelectedItem = ReplayTab;

    private void CancelClientRefresh()
    {
        _closing = true;
        _clientRefresh?.Cancel();
        CancelVmOperation();
        UpdateVmOperationControls();
    }

    protected override void OnClosed(EventArgs e)
    {
        CancelClientRefresh();
        base.OnClosed(e);
    }

    internal static async Task<bool> RunClientSmokeCheckAsync()
    {
        var client = new RiftClientInfo(123, DateTimeOffset.UnixEpoch, @"C:\fixture\rift_x64.exe",
            1, 100, "Synthetic client", "Detected", "Test fixture only.");
        var populated = new ClientInventorySnapshot(DateTimeOffset.UnixEpoch, 1,
            Array.AsReadOnly(new[] { client }), null, false);
        var empty = populated with { Clients = Array.Empty<RiftClientInfo>() };
        var late = new TaskCompletionSource<ClientInventorySnapshot>(TaskCreationOptions.RunContinuationsAsynchronously);
        int calls = 0;
        var window = new MainWindow(DemoReplay.Create(), _ => ++calls switch
        {
            1 => Task.FromResult(populated),
            2 => Task.FromResult(empty with { ErrorCode = "process_list_unavailable" }),
            3 => Task.FromResult(empty),
            4 => Task.FromException<ClientInventorySnapshot>(new InvalidOperationException("fixture failure")),
            _ => late.Task
        });
        try
        {
            if (calls != 0) return false; // Construction must never enumerate processes.
            await window.RefreshClientsAsync();
            window.ClientsList.SelectedIndex = 0;
            if (!window.ClientDetailsText.Text.Contains("PID: 123", StringComparison.Ordinal)) return false;
            await window.RefreshClientsAsync();
            if (window.ClientsList.Items.Count != 0 || window.ClientDetailsText.Text != "No client selected."
                || !window.ClientSummaryText.Text.Contains("could not list", StringComparison.Ordinal)) return false;
            await window.RefreshClientsAsync();
            if (!window.ClientSummaryText.Text.Contains("VMware guest clients are not included", StringComparison.Ordinal)) return false;
            await window.RefreshClientsAsync();
            if (!window.ClientSummaryText.Text.Contains("inspection failed", StringComparison.Ordinal)
                || !window.RefreshClientsButton.IsEnabled) return false;
            Task refresh = window.RefreshClientsAsync();
            await window.RefreshClientsAsync(); // Overlapping clicks must not run a second scan.
            if (calls != 5) return false;
            window.CancelClientRefresh();
            late.SetResult(populated);
            await refresh;
            return window.ClientsList.Items.Count == 0 && calls == 5;
        }
        finally
        {
            late.TrySetResult(empty);
            window.Close();
        }
    }
}

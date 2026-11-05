using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;

namespace Rift.Automation.Windows;

/// <summary>
/// In-process guest observation receiver, independent of RiftReader2.
/// Binds to a specific private IPv4 interface and accepts one configured guest.
/// Never exposes gameplay input or command endpoints.
/// </summary>
public sealed class StandaloneTelemetryHttpListener : IDisposable
{
    public const int MaxBodyBytes = 131_072;

    private const int MaxHeaderBytes = 8_192;
    private const int MaxConnections = 4;

    private static readonly TimeSpan ConnectionDeadline =
        TimeSpan.FromSeconds(3);

    private readonly TcpListener _listener;
    private readonly IPAddress _guestAddress;
    private readonly byte[] _expectedTokenDigest;

    private readonly Func<
        ReadOnlyMemory<byte>,
        string,
        string,
        DateTimeOffset,
        bool> _accept;

    private readonly TimeProvider _time;
    private readonly CancellationTokenSource _stop = new();

    private int _started;
    private int _disposed;

    /// <param name="accept">
    /// Processes an authenticated observation.
    /// Return true for an accepted delivery, regardless of whether
    /// the resulting game position is usable for automation.
    /// </param>
    public StandaloneTelemetryHttpListener(
        string bindIpv4,
        int port,
        string expectedGuestIpv4,
        string deliveryToken,
        Func<ReadOnlyMemory<byte>, string, string, DateTimeOffset, bool> accept,
        TimeProvider? timeProvider = null)
        : this(bindIpv4, port, expectedGuestIpv4, deliveryToken, accept,
            timeProvider, allowLoopback: false)
    {
    }

    // The only nonprivate-host bind seam is internal and used by offline tests.
    internal static StandaloneTelemetryHttpListener CreateLoopbackForTests(
        string token,
        Func<ReadOnlyMemory<byte>, string, string, DateTimeOffset, bool> accept,
        TimeProvider? timeProvider = null) =>
        new("127.0.0.1", 0, "127.0.0.1", token, accept,
            timeProvider, allowLoopback: true);

    internal int BoundPort => (_listener.LocalEndpoint as IPEndPoint)?.Port ?? 0;

    private StandaloneTelemetryHttpListener(
        string bindIpv4,
        int port,
        string expectedGuestIpv4,
        string deliveryToken,
        Func<ReadOnlyMemory<byte>, string, string, DateTimeOffset, bool> accept,
        TimeProvider? timeProvider,
        bool allowLoopback)
    {
        IPAddress host = ParsePrivateIpv4(
            bindIpv4, nameof(bindIpv4), allowLoopback);

        _guestAddress = ParsePrivateIpv4(
            expectedGuestIpv4, nameof(expectedGuestIpv4), allowLoopback);

        if ((!allowLoopback && port is < 1024 or > 65535) ||
            (allowLoopback && port != 0))
            throw new ArgumentOutOfRangeException(nameof(port));

        if (deliveryToken is null ||
            deliveryToken.Length is < 32 or > 512 ||
            deliveryToken.Any(c => c < '!' || c > '~'))
        {
            throw new ArgumentException(
                "A 32..512-character printable token is required.",
                nameof(deliveryToken));
        }

        _accept = accept ?? throw new ArgumentNullException(nameof(accept));
        _listener = new TcpListener(host, port);
        _expectedTokenDigest = SHA256.HashData(
            Encoding.ASCII.GetBytes(deliveryToken));
        _time = timeProvider ?? TimeProvider.System;
    }

    /// <summary>
    /// Runs until cancelled. The caller owns this task's lifetime.
    /// No separate Windows service or process is installed.
    /// </summary>
    public async Task RunAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);

        if (Interlocked.Exchange(ref _started, 1) != 0)
            throw new InvalidOperationException(
                "Receiver can only be started once.");

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken, _stop.Token);

        var tasks = new List<Task>();

        _listener.Start(backlog: 16);

        try
        {
            while (!linked.Token.IsCancellationRequested)
            {
                TcpClient client = await _listener
                    .AcceptTcpClientAsync(linked.Token)
                    .ConfigureAwait(false);

                // Observe every completed handler, including faulted tasks.
                foreach (Task completed in tasks.Where(task => task.IsCompleted).ToArray())
                {
                    try { await completed.ConfigureAwait(false); }
                    catch (Exception) { /* A connection cannot stop the receiver. */ }
                    tasks.Remove(completed);
                }

                if (tasks.Count >= MaxConnections)
                {
                    client.Dispose();
                    continue;
                }

                tasks.Add(HandleClientAsync(client, linked.Token));
            }
        }
        catch (OperationCanceledException)
            when (linked.IsCancellationRequested)
        {
        }
        catch (ObjectDisposedException)
            when (linked.IsCancellationRequested)
        {
        }
        catch (SocketException)
            when (linked.IsCancellationRequested)
        {
        }
        finally
        {
            _listener.Stop();
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
    }

    private async Task HandleClientAsync(
        TcpClient client,
        CancellationToken outerToken)
    {
        using (client)
        using (var deadline =
            CancellationTokenSource.CreateLinkedTokenSource(outerToken))
        {
            deadline.CancelAfter(ConnectionDeadline);

            try
            {
                if (client.Client.RemoteEndPoint is not IPEndPoint peer ||
                    !peer.Address.Equals(_guestAddress))
                {
                    await ReplyAsync(
                        client.GetStream(), 403, "forbidden", deadline.Token);
                    return;
                }

                NetworkStream stream = client.GetStream();

                string header = await ReadHeaderAsync(
                    stream, deadline.Token);

                if (!TryReadRequest(
                    header,
                    out string token,
                    out int length,
                    out int failureCode,
                    out string failureReason))
                {
                    await ReplyAsync(
                        stream,
                        failureCode,
                        failureReason,
                        deadline.Token);

                    return;
                }

                byte[] suppliedDigest = SHA256.HashData(
                    Encoding.ASCII.GetBytes(token));

                if (!CryptographicOperations.FixedTimeEquals(
                    suppliedDigest,
                    _expectedTokenDigest))
                {
                    await ReplyAsync(
                        stream, 403, "forbidden", deadline.Token);
                    return;
                }

                byte[] body = new byte[length];

                await stream.ReadExactlyAsync(
                    body.AsMemory(),
                    deadline.Token);

                bool accepted;

                try
                {
                    accepted = _accept(
                        body,
                        token,
                        peer.Address.ToString(),
                        _time.GetUtcNow());
                }
                catch (Exception ex) when (
                    ex is ArgumentException or
                    FormatException or
                    InvalidDataException or
                    System.Text.Json.JsonException)
                {
                    accepted = false;
                }

                await ReplyAsync(
                    stream,
                    accepted ? 200 : 400,
                    accepted ? "recorded" : "invalid-observation",
                    deadline.Token);
            }
            catch (OperationCanceledException)
                when (deadline.IsCancellationRequested)
            {
            }
            catch (Exception)
            {
                // Connection-local failures cannot kill the receiver or leak
                // headers, tokens, payloads, or a previous game's XYZ.
                try
                {
                    await ReplyAsync(
                        client.GetStream(), 500, "receiver-error", deadline.Token)
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The client may already be gone. No delivery was recorded.
                }
            }
        }
    }

    private static async Task<string> ReadHeaderAsync(
        NetworkStream stream,
        CancellationToken ct)
    {
        byte[] bytes = new byte[MaxHeaderBytes];
        byte[] one = new byte[1];

        int count = 0;

        while (count < bytes.Length)
        {
            int read = await stream.ReadAsync(
                one.AsMemory(), ct);

            if (read != 1)
                throw new EndOfStreamException();

            byte value = one[0];

            if (value > 127 ||
                (value < 32 && value is not (9 or 10 or 13)))
            {
                throw new InvalidDataException(
                    "Invalid HTTP header byte.");
            }

            bytes[count++] = value;

            if (count >= 4 &&
                bytes[count - 4] == '\r' &&
                bytes[count - 3] == '\n' &&
                bytes[count - 2] == '\r' &&
                bytes[count - 1] == '\n')
            {
                return Encoding.ASCII.GetString(bytes, 0, count);
            }
        }

        throw new InvalidDataException(
            "HTTP header limit exceeded.");
    }

    private static bool TryReadRequest(
        string header,
        out string token,
        out int bodyLength,
        out int status,
        out string reason)
    {
        token = "";
        bodyLength = 0;
        status = 400;
        reason = "invalid-request";

        string[] lines = header.Split(
            "\r\n", StringSplitOptions.None);

        string[] first = lines[0].Split(
            ' ', StringSplitOptions.None);

        if (first.Length != 3 ||
            first[2] is not ("HTTP/1.1" or "HTTP/1.0"))
            return false;

        if (first[0] != "POST")
        {
            status = 405;
            reason = "method-not-allowed";
            return false;
        }

        if (first[1] != "/observation")
        {
            status = 404;
            reason = "not-found";
            return false;
        }

        var fields = new Dictionary<string, string>(
            StringComparer.OrdinalIgnoreCase);

        for (int i = 1; i < lines.Length && lines[i].Length > 0; ++i)
        {
            int colon = lines[i].IndexOf(':');

            if (colon <= 0)
                return false;

            string name = lines[i][..colon];

            if (name.Any(c =>
                !char.IsAsciiLetterOrDigit(c) && c != '-'))
                return false;

            string value = lines[i][(colon + 1)..]
                .Trim(' ', '\t');

            if (!fields.TryAdd(name, value))
                return false;
        }

        if (fields.ContainsKey("Transfer-Encoding") ||
            fields.ContainsKey("Content-Encoding") ||
            !fields.TryGetValue("Content-Length", out string? length) ||
            !int.TryParse(
                length,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out bodyLength) ||
            bodyLength is < 1 or > MaxBodyBytes ||
            !fields.TryGetValue(
                "X-RiftReader-Token",
                out string? supplied) ||
            supplied.Length is < 1 or > 512 ||
            supplied.Any(c => c < '!' || c > '~'))
        {
            return false;
        }

        token = supplied;
        return true;
    }

    private static async Task ReplyAsync(
        NetworkStream stream,
        int status,
        string message,
        CancellationToken ct)
    {
        string phrase = status switch
        {
            200 => "OK",
            400 => "Bad Request",
            403 => "Forbidden",
            404 => "Not Found",
            405 => "Method Not Allowed",
            500 => "Internal Server Error",
            _ => "Error"
        };

        string body = "{\"status\":\"" + message + "\"}";
        byte[] payload = Encoding.ASCII.GetBytes(body);

        string head =
            $"HTTP/1.1 {status} {phrase}\r\n" +
            "Content-Type: application/json\r\n" +
            $"Content-Length: {payload.Length}\r\n" +
            "Connection: close\r\n\r\n";

        await stream.WriteAsync(
            Encoding.ASCII.GetBytes(head), ct);

        await stream.WriteAsync(payload, ct);
    }

    private static IPAddress ParsePrivateIpv4(
        string text,
        string parameter,
        bool allowLoopback)
    {
        if (!IPAddress.TryParse(text, out IPAddress? ip) ||
            ip.AddressFamily != AddressFamily.InterNetwork)
        {
            throw new ArgumentException(
                "A specific private IPv4 address is required.",
                parameter);
        }

        byte[] b = ip.GetAddressBytes();

        bool privateRange =
            b[0] == 10 ||
            (b[0] == 172 && b[1] is >= 16 and <= 31) ||
            (b[0] == 192 && b[1] == 168);

        if (!privateRange &&
            !(allowLoopback && IPAddress.IsLoopback(ip)))
        {
            throw new ArgumentException(
                "A specific private IPv4 address is required.",
                parameter);
        }

        return ip;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;

        _stop.Cancel();
        _listener.Stop();
        _stop.Dispose();
    }
}

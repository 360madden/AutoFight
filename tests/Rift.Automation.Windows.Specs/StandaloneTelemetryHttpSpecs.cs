using System.Net.Sockets;
using System.Text;
using Rift.Automation.Windows;

internal static class StandaloneTelemetryHttpSpecs
{
    private const string Token =
        "synthetic-loopback-only-token-123456789012345";

    private static string Send(int port, string request)
    {
        using var client = new TcpClient
        {
            ReceiveTimeout = 5000,
            SendTimeout = 5000
        };

        client.Connect("127.0.0.1", port);
        using NetworkStream stream = client.GetStream();
        byte[] bytes = Encoding.ASCII.GetBytes(request);
        stream.Write(bytes);
        client.Client.Shutdown(SocketShutdown.Send);

        using var output = new MemoryStream();
        stream.CopyTo(output);
        return Encoding.ASCII.GetString(output.ToArray());
    }

    private static string Request(
        string body,
        string token = Token,
        string method = "POST",
        string endpoint = "/observation",
        string extraHeaders = "",
        string? length = null)
    {
        int bodyBytes = Encoding.ASCII.GetByteCount(body);

        return $"{method} {endpoint} HTTP/1.1\r\n" +
            "Host: 127.0.0.1\r\n" +
            $"X-RiftReader-Token: {token}\r\n" +
            $"Content-Length: {length ?? bodyBytes.ToString(System.Globalization.CultureInfo.InvariantCulture)}\r\n" +
            extraHeaders +
            "\r\n" + body;
    }

    private static string HeadersOnly(string request)
    {
        int end = request.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        if (end < 0) throw new InvalidDataException("Missing header terminator.");
        return request[..(end + 4)];
    }

    internal static void Run(Action<string, Action> test,
        Action<bool, string> check)
    {
        test("Standalone receiver records valid and game-unavailable HTTP deliveries", () =>
        {
            int deliveries = 0;
            using var listener = StandaloneTelemetryHttpListener.CreateLoopbackForTests(
                Token, (bytes, token, peer, time) =>
                {
                    check(token == Token && peer == "127.0.0.1" &&
                        bytes.Length > 0 && time != default,
                        "actual socket peer, auth and bytes reach handler");
                    Interlocked.Increment(ref deliveries);
                    return true; // Even an unavailable game is a valid delivery.
                });
            using var cancelled = new CancellationTokenSource();
            Task run = listener.RunAsync(cancelled.Token);

            try
            {
                check(listener.BoundPort > 0, "test-only ephemeral port bound");
                string first = Send(listener.BoundPort, Request(
                    "{\"schemaVersion\":1,\"readerStatus\":\"running\"}"));
                string unavailable = Send(listener.BoundPort, Request(
                    "{\"schemaVersion\":1,\"readerStatus\":\"terminated\",\"target\":null}"));

                check(first.StartsWith("HTTP/1.1 200", StringComparison.Ordinal) &&
                    unavailable.StartsWith("HTTP/1.1 200", StringComparison.Ordinal) &&
                    deliveries == 2,
                    "HTTP receipt accepted regardless of usable game XYZ");
            }
            finally
            {
                cancelled.Cancel();
                run.GetAwaiter().GetResult();
            }
        });

        test("Standalone receiver rejects invalid method path token and headers", () =>
        {
            int delivered = 0;
            using var listener = StandaloneTelemetryHttpListener.CreateLoopbackForTests(
                Token, (body, token, peer, time) =>
                {
                    Interlocked.Increment(ref delivered);
                    return true;
                });
            using var cancelled = new CancellationTokenSource();
            Task run = listener.RunAsync(cancelled.Token);

            try
            {
                int port = listener.BoundPort;
                string body = "{}";
                check(Send(port, HeadersOnly(Request(body, method: "GET")))
                    .StartsWith("HTTP/1.1 405", StringComparison.Ordinal),
                    "unsupported method");
                check(Send(port, HeadersOnly(Request(body, endpoint: "/command")))
                    .StartsWith("HTTP/1.1 404", StringComparison.Ordinal),
                    "no input endpoint");
                check(Send(port, HeadersOnly(Request(body, token: new string('x', Token.Length))))
                    .StartsWith("HTTP/1.1 403", StringComparison.Ordinal),
                    "wrong bearer");
                check(Send(port, HeadersOnly(Request(body, extraHeaders:
                    "X-RiftReader-Token: duplicate\r\n")))
                    .StartsWith("HTTP/1.1 400", StringComparison.Ordinal),
                    "duplicate bearer");
                check(Send(port, HeadersOnly(Request(body, extraHeaders:
                    "Transfer-Encoding: chunked\r\n")))
                    .StartsWith("HTTP/1.1 400", StringComparison.Ordinal),
                    "chunked request");
                check(Send(port, HeadersOnly(Request(body, length: "131073")))
                    .StartsWith("HTTP/1.1 400", StringComparison.Ordinal),
                    "oversized request before allocation");
                check(Send(port, HeadersOnly(Request(body, length: "0")))
                    .StartsWith("HTTP/1.1 400", StringComparison.Ordinal),
                    "empty body");
                check(delivered == 0, "bad transport never reaches ingest callback");
            }
            finally
            {
                cancelled.Cancel();
                run.GetAwaiter().GetResult();
            }
        });

        test("Standalone receiver isolates callback faults and continues", () =>
        {
            int count = 0;
            using var listener = StandaloneTelemetryHttpListener.CreateLoopbackForTests(
                Token, (body, token, peer, time) =>
                {
                    if (Interlocked.Increment(ref count) == 1)
                        throw new InvalidOperationException("Synthetic ingest fault.");
                    return true;
                });
            using var cancelled = new CancellationTokenSource();
            Task run = listener.RunAsync(cancelled.Token);

            try
            {
                string failure = Send(listener.BoundPort, Request("{}"));
                string recovery = Send(listener.BoundPort, Request("{}"));

                check(failure.StartsWith("HTTP/1.1 500", StringComparison.Ordinal) &&
                    recovery.StartsWith("HTTP/1.1 200", StringComparison.Ordinal) &&
                    count == 2,
                    "an interrupted client cannot kill receiver or fake success");
            }
            finally
            {
                cancelled.Cancel();
                run.GetAwaiter().GetResult();
            }
        });
    }
}

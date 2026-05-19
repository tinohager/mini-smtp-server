using System.Buffers;
using System.Net;
using System.Net.Sockets;

public static class SmtpServer11
{
    private static Socket _listener = null!;
    private static volatile bool _running;

    private static readonly byte[] RespReady = "220 localhost ESMTP Service Ready\r\n"u8.ToArray();
    private static readonly byte[] RespEhlo = "250-localhost\r\n250 PIPELINING\r\n250 OK\r\n"u8.ToArray();
    private static readonly byte[] RespOk = "250 OK\r\n"u8.ToArray();
    private static readonly byte[] RespDataStart = "354 End data with <CRLF>.<CRLF>\r\n"u8.ToArray();
    private static readonly byte[] RespDataOk = "250 OK Message accepted\r\n"u8.ToArray();
    private static readonly byte[] RespBye = "221 Bye\r\n"u8.ToArray();

    public static void Start()
    {
        Console.WriteLine("SMTP Server11 (Turbo - Fixed)");

        _listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        _listener.Bind(new IPEndPoint(IPAddress.IPv6Any, 25));
        _listener.Listen(1024);
        _running = true;

        _ = AcceptLoop();

        Thread.Sleep(Timeout.Infinite);
    }

    private static async Task AcceptLoop()
    {
        while (_running)
        {
            try
            {
                var client = await _listener.AcceptAsync().ConfigureAwait(false);
                _ = HandleClientAsync(client);
            }
            catch
            {
                break;
            }
        }
    }

    private static async Task HandleClientAsync(Socket socket)
    {
        using (socket)
        {
            var buffer = ArrayPool<byte>.Shared.Rent(64 * 1024);
            try
            {
                await socket.SendAsync(RespReady, SocketFlags.None).ConfigureAwait(false);

                int count = 0;
                bool dataMode = false;

                while (true)
                {
                    int read = await socket.ReceiveAsync(buffer.AsMemory(count, buffer.Length - count), SocketFlags.None).ConfigureAwait(false);
                    if (read <= 0) break;

                    count += read;
                    int start = 0;

                    if (dataMode)
                    {
                        var dataSpan = buffer.AsSpan(0, count);
                        int endOfDataIdx = dataSpan.IndexOf("\r\n.\r\n"u8);

                        if (endOfDataIdx >= 0)
                        {
                            dataMode = false;
                            await socket.SendAsync(RespDataOk, SocketFlags.None).ConfigureAwait(false);
                            start = endOfDataIdx + 5;
                        }
                        else
                        {
                            int keep = Math.Min(count, 4);
                            Buffer.BlockCopy(buffer, count - keep, buffer, 0, keep);
                            count = keep;
                            continue;
                        }
                    }

                    for (int i = start; i < count; i++)
                    {
                        if (buffer[i] != (byte)'\n') continue;

                        var line = new ReadOnlySpan<byte>(buffer, start, i - start);
                        start = i + 1;

                        if (line.Length == 0) continue;
                        if (line[^1] == (byte)'\r') line = line[..^1];

                        byte[] response = ProcessCommand(line, ref dataMode, out bool shouldQuit);

                        await socket.SendAsync(response, SocketFlags.None).ConfigureAwait(false);

                        if (shouldQuit)
                        {
                            socket.Shutdown(SocketShutdown.Both);
                            return;
                        }
                    }

                    if (start > 0 && count > start)
                    {
                        Buffer.BlockCopy(buffer, start, buffer, 0, count - start);
                        count -= start;
                    }
                    else if (start >= count)
                    {
                        count = 0;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    private static byte[] ProcessCommand(ReadOnlySpan<byte> line, ref bool dataMode, out bool shouldQuit)
    {
        shouldQuit = false;

        if (line.StartsWith("EHLO"u8) || line.StartsWith("HELO"u8))
        {
            return RespEhlo;
        }

        if (line.StartsWith("MAIL FROM"u8) || line.StartsWith("RCPT TO"u8))
        {
            return RespOk;
        }

        if (line.StartsWith("DATA"u8))
        {
            dataMode = true;
            return RespDataStart;
        }

        if (line.StartsWith("QUIT"u8))
        {
            shouldQuit = true;
            return RespBye;
        }

        return RespOk;
    }
}

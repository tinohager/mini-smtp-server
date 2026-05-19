using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;

public static class SmtpServer10
{
    private static Socket _listener = null!;
    private static volatile bool _running;

    public static void Start()
    {
        Console.WriteLine("SMTP Server10 (Zero Alloc)");

        _listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        _listener.Bind(new IPEndPoint(IPAddress.IPv6Any, 25));
        _listener.Listen(1024);

        _running = true;

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _running = false;

            try { _listener.Close(); } catch { }
        };

        _ = AcceptLoop();

        Console.WriteLine("SMTP ready");

        Thread.Sleep(Timeout.Infinite);
    }

    // =========================
    // ACCEPT LOOP
    // =========================
    private static async Task AcceptLoop()
    {
        while (_running)
        {
            Socket client;

            try
            {
                client = await _listener.AcceptAsync();
            }
            catch
            {
                break;
            }

            _ = Task.Run(() => Handle(client));
        }
    }

    // =========================
    // CONNECTION
    // =========================
    private static void Handle(Socket socket)
    {
        using (socket)
        {
            using var stream = new NetworkStream(socket, ownsSocket: false);

            var buffer = ArrayPool<byte>.Shared.Rent(4096);
            try
            {
                stream.Write(Encoding.ASCII.GetBytes("220 localhost ESMTP Service Ready\r\n"));

                int count = 0;
                bool dataMode = false;

                while (true)
                {
                    int read = stream.Read(buffer, count, buffer.Length - count);
                    if (read <= 0)
                        break;

                    count += read;

                    int start = 0;

                    for (int i = 0; i < count; i++)
                    {
                        if (buffer[i] != (byte)'\n')
                            continue;

                        var line = new ReadOnlySpan<byte>(buffer, start, i - start);

                        start = i + 1;

                        if (line.Length == 0)
                            continue;

                        if (line[^1] == (byte)'\r')
                            line = line[..^1];

                        if (dataMode)
                        {
                            if (line.Length == 1 && line[0] == (byte)'.')
                            {
                                dataMode = false;
                                Write(stream, "250 OK Message accepted\r\n");
                            }

                            continue;
                        }

                        Process(line, stream, ref dataMode);
                    }

                    if (start > 0)
                    {
                        Buffer.BlockCopy(buffer, start, buffer, 0, count - start);
                        count -= start;
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }

    // =========================
    // SMTP STATE MACHINE (FAST)
    // =========================
    private static void Process(ReadOnlySpan<byte> line, NetworkStream stream, ref bool dataMode)
    {
        if (Starts(line, "EHLO") || Starts(line, "HELO"))
        {
            Write(stream,
                "250-localhost\r\n" +
                "250 PIPELINING\r\n" +
                "250 OK\r\n");
            return;
        }

        if (Starts(line, "MAIL FROM") || Starts(line, "RCPT TO"))
        {
            Write(stream, "250 OK\r\n");
            return;
        }

        if (Starts(line, "DATA"))
        {
            dataMode = true;
            Write(stream, "354 End data with <CRLF>.<CRLF>\r\n");
            return;
        }

        if (Starts(line, "QUIT"))
        {
            Write(stream, "221 Bye\r\n");
            stream.Close();
            return;
        }

        Write(stream, "250 OK\r\n");
    }

    // =========================
    // WRITE (FAST PATH)
    // =========================
    private static void Write(NetworkStream stream, string text)
    {
        stream.Write(Encoding.ASCII.GetBytes(text));
    }

    // =========================
    // ZERO ALLOC PREFIX CHECK
    // =========================
    private static bool Starts(ReadOnlySpan<byte> span, string value)
    {
        var v = value.AsSpan();

        if (span.Length < v.Length)
            return false;

        for (int i = 0; i < v.Length; i++)
        {
            if ((char)span[i] != v[i])
                return false;
        }

        return true;
    }
}

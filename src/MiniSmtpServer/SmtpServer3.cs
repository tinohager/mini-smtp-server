using System.Net;
using System.Net.Sockets;
using System.Text;

public static class SmtpServer3
{
    private static volatile bool _running;
    private static Socket? _listener;

    public static void Start()
    {
        Console.WriteLine("Start Mini SMTP Server 3 [Port:25]");

        _listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        _listener.Bind(new IPEndPoint(IPAddress.IPv6Any, 25));
        _listener.Listen(1024);

        Console.WriteLine("Mini SMTP Server is ready");

        _running = true;

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Stop();
        };

        _ = AcceptLoop();
    }

    public static void Stop()
    {
        if (!_running) return;

        _running = false;

        try { _listener?.Close(); } catch { }

        Console.WriteLine("Mini SMTP Server stopped");
    }

    private static async Task AcceptLoop()
    {
        while (_running)
        {
            Socket socket;

            try
            {
                socket = _listener!.Accept();
            }
            catch
            {
                break;
            }

            _ = ThreadPool.UnsafeQueueUserWorkItem(_ =>
            {
                Handle(socket);
            }, null);
        }
    }

    private static void Handle(Socket socket)
    {
        var buffer = new byte[4096];
        int received = 0;

        try
        {
            Send(socket, "220 localhost SMTP Ready\r\n");

            while (true)
            {
                int read;

                try
                {
                    read = socket.Receive(buffer, received, buffer.Length - received, SocketFlags.None);
                }
                catch
                {
                    break;
                }

                if (read <= 0)
                    break;

                received += read;

                int start = 0;

                for (int i = 0; i < received; i++)
                {
                    if (buffer[i] != (byte)'\n')
                        continue;

                    var line = new ReadOnlySpan<byte>(buffer, start, i - start);
                    start = i + 1;

                    if (line.Length == 0)
                        continue;

                    if (line[^1] == (byte)'\r')
                        line = line[..^1];

                    if (!Process(line, socket))
                        return;
                }

                if (start > 0)
                {
                    Buffer.BlockCopy(buffer, start, buffer, 0, received - start);
                    received -= start;
                }
            }
        }
        finally
        {
            socket.Dispose();
        }
    }

    private static bool Process(ReadOnlySpan<byte> line, Socket socket)
    {
        if (Starts(line, "EHLO") || Starts(line, "HELO"))
        {
            Send(socket, "250-localhost\r\n250-PIPELINING\r\n250 OK\r\n");
            return true;
        }

        if (Starts(line, "MAIL FROM") || Starts(line, "RCPT TO"))
        {
            Send(socket, "250 OK\r\n");
            return true;
        }

        if (Starts(line, "NOOP"))
        {
            Send(socket, "250 OK\r\n");
            return true;
        }

        if (Starts(line, "DATA"))
        {
            Send(socket, "354 End data with <CR><LF>.<CR><LF>\r\n");
            return true;
        }

        if (Starts(line, "QUIT"))
        {
            Send(socket, "221 Bye\r\n");
            return false;
        }

        Send(socket, "250 OK\r\n");
        return true;
    }

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

    private static void Send(Socket socket, string text)
    {
        var data = Encoding.ASCII.GetBytes(text);
        socket.Send(data);
    }
}

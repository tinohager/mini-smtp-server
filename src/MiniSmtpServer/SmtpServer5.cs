using System.Net;
using System.Net.Sockets;
using System.Text;

public static class SmtpServer5
{
    private static Socket _listener = null!;
    private static volatile bool _running;

    private static readonly List<Connection> Connections = new();

    public static void Start()
    {
        Console.WriteLine("SMTP Server5 starting (REACTOR MODE)");

        _listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        _listener.Bind(new IPEndPoint(IPAddress.IPv6Any, 25));
        _listener.Listen(1024);

        _running = true;

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _running = false;
        };

        RunLoop();
    }

    // =========================
    // MAIN REACTOR LOOP
    // =========================
    private static void RunLoop()
    {
        var read = new List<Socket>();
        var error = new List<Socket>();

        while (_running)
        {
            read.Clear();
            error.Clear();

            read.Add(_listener);

            foreach (var c in Connections)
                read.Add(c.Socket);

            Socket.Select(read, null, error, 1000);

            // ACCEPT
            if (read.Contains(_listener))
            {
                var client = _listener.Accept();
                Connections.Add(new Connection(client));
            }

            // IO
            for (int i = Connections.Count - 1; i >= 0; i--)
            {
                var c = Connections[i];

                if (read.Contains(c.Socket))
                {
                    if (!c.Tick())
                    {
                        Connections.RemoveAt(i);
                    }
                }
            }
        }

        Shutdown();
    }

    private static void Shutdown()
    {
        foreach (var c in Connections)
            c.Close();

        try { _listener.Close(); } catch { }

        Console.WriteLine("SMTP Server5 stopped");
    }

    // =========================
    // CONNECTION STATE MACHINE
    // =========================
    private sealed class Connection
    {
        public Socket Socket { get; }

        private readonly byte[] _buffer = new byte[4096];
        private int _received;

        public Connection(Socket socket)
        {
            Socket = socket;
            Send("220 localhost SMTP Ready\r\n");
        }

        public bool Tick()
        {
            int read;

            try
            {
                read = Socket.Receive(_buffer, _received, _buffer.Length - _received, SocketFlags.None);
            }
            catch
            {
                return false;
            }

            if (read <= 0)
                return false;

            _received += read;

            int start = 0;

            for (int i = 0; i < _received; i++)
            {
                if (_buffer[i] != (byte)'\n')
                    continue;

                var line = new ReadOnlySpan<byte>(_buffer, start, i - start);
                start = i + 1;

                if (line.Length == 0)
                    continue;

                if (line[^1] == (byte)'\r')
                    line = line[..^1];

                if (!Process(line))
                    return false;
            }

            if (start > 0)
            {
                Buffer.BlockCopy(_buffer, start, _buffer, 0, _received - start);
                _received -= start;
            }

            return true;
        }

        private bool Process(ReadOnlySpan<byte> line)
        {
            if (Starts(line, "EHLO") || Starts(line, "HELO"))
            {
                Send("250-localhost\r\n250 PIPELINING\r\n250 OK\r\n");
                return true;
            }

            if (Starts(line, "MAIL FROM") || Starts(line, "RCPT TO"))
            {
                Send("250 OK\r\n");
                return true;
            }

            if (Starts(line, "DATA"))
            {
                Send("354 End data\r\n");
                return true;
            }

            if (Starts(line, "QUIT"))
            {
                Send("221 Bye\r\n");
                return false;
            }

            Send("250 OK\r\n");
            return true;
        }

        private void Send(string text)
        {
            var data = Encoding.ASCII.GetBytes(text);
            Socket.Send(data);
        }

        public void Close()
        {
            try { Socket.Close(); } catch { }
        }

        private static bool Starts(ReadOnlySpan<byte> span, string s)
        {
            var v = s.AsSpan();
            if (span.Length < v.Length) return false;

            for (int i = 0; i < v.Length; i++)
                if ((char)span[i] != v[i])
                    return false;

            return true;
        }
    }
}

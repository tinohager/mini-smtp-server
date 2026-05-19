using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;

public static class SmtpServer6
{
    private static Socket _listener = null!;
    private static volatile bool _running;

    private static readonly List<Connection> Connections = new();

    // PRE-ENCODED RESPONSES (NO ALLOCATION IN HOT PATH)
    private static readonly byte[] R_220 = Encoding.ASCII.GetBytes("220 localhost SMTP Ready\r\n");
    private static readonly byte[] R_250 = Encoding.ASCII.GetBytes("250 OK\r\n");
    private static readonly byte[] R_250_PIPE = Encoding.ASCII.GetBytes("250-localhost\r\n250 PIPELINING\r\n250 OK\r\n");
    private static readonly byte[] R_354 = Encoding.ASCII.GetBytes("354 End data\r\n");
    private static readonly byte[] R_221 = Encoding.ASCII.GetBytes("221 Bye\r\n");

    public static void Start()
    {
        Console.WriteLine("SMTP Server6 (ULTRA MODE)");

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
    // REACTOR LOOP
    // =========================
    private static void RunLoop()
    {
        var read = new List<Socket>();

        while (_running)
        {
            read.Clear();
            read.Add(_listener);

            foreach (var c in Connections)
                read.Add(c.Socket);

            Socket.Select(read, null, null, 1000);

            // ACCEPT
            if (read.Contains(_listener))
            {
                var socket = _listener.Accept();
                var conn = new Connection(socket);
                Connections.Add(conn);
            }

            // PROCESS CONNECTIONS
            for (int i = Connections.Count - 1; i >= 0; i--)
            {
                var c = Connections[i];

                if (read.Contains(c.Socket))
                {
                    if (!c.Tick())
                    {
                        Connections.RemoveAt(i);
                        c.Dispose();
                    }
                }
            }
        }

        Shutdown();
    }

    private static void Shutdown()
    {
        foreach (var c in Connections)
            c.Dispose();

        try { _listener.Close(); } catch { }

        Console.WriteLine("SMTP Server6 stopped");
    }

    // =========================
    // CONNECTION
    // =========================
    private sealed class Connection
    {
        public Socket Socket { get; }

        private byte[] _buffer;
        private int _received;

        public Connection(Socket socket)
        {
            Socket = socket;
            _buffer = ArrayPool<byte>.Shared.Rent(4096);

            Send(R_220);
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
                Send(R_250_PIPE);
                return true;
            }

            if (Starts(line, "MAIL FROM") || Starts(line, "RCPT TO"))
            {
                Send(R_250);
                return true;
            }

            if (Starts(line, "DATA"))
            {
                Send(R_354);
                return true;
            }

            if (Starts(line, "QUIT"))
            {
                Send(R_221);
                return false;
            }

            Send(R_250);
            return true;
        }

        private void Send(byte[] response)
        {
            Socket.Send(response, SocketFlags.None);
        }

        private static bool Starts(ReadOnlySpan<byte> span, string s)
        {
            var v = s.AsSpan();

            if (span.Length < v.Length)
                return false;

            for (int i = 0; i < v.Length; i++)
                if ((char)span[i] != v[i])
                    return false;

            return true;
        }

        public void Dispose()
        {
            try { Socket.Close(); } catch { }
            ArrayPool<byte>.Shared.Return(_buffer);
        }
    }
}

using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Channels;

public static class SmtpServer8
{
    private static Socket _listener = null!;
    private static volatile bool _running;

    private static readonly Reactor[] _reactors = new Reactor[Environment.ProcessorCount];

    public static void Start()
    {
        Console.WriteLine("SMTP Server8 running");

        _listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        _listener.Bind(new IPEndPoint(IPAddress.IPv6Any, 25));
        _listener.Listen(1024);

        _running = true;

        for (int i = 0; i < _reactors.Length; i++)
            _reactors[i] = new Reactor();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _running = false;
            try { _listener.Close(); } catch { }
        };

        AcceptLoop();

        RunLoop();
    }

    private static void RunLoop()
    {
        while (_running)
        {
            for (int i = 0; i < _reactors.Length; i++)
            {
                _reactors[i].Tick();
            }

            Thread.Sleep(1);
        }
    }

    // =========================
    // ACCEPT LOOP
    // =========================
    private static async void AcceptLoop()
    {
        int rr = 0;

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

            _reactors[rr++ % _reactors.Length].Assign(client);
        }
    }

    // =========================
    // REACTOR
    // =========================
    private sealed class Reactor
    {
        private readonly byte[] _buffer = ArrayPool<byte>.Shared.Rent(8192);
        private readonly List<Connection> _connections = new();

        public void Assign(Socket socket)
        {
            var conn = new Connection(socket, _buffer);
            _connections.Add(conn);
        }

        // In v8 we poll instead of event callbacks (batch model)
        public void Tick()
        {
            for (int i = _connections.Count - 1; i >= 0; i--)
            {
                if (!_connections[i].Tick())
                {
                    _connections[i].Dispose();
                    _connections.RemoveAt(i);
                }
            }
        }
    }

    // =========================
    // CONNECTION
    // =========================
    private sealed class Connection
    {
        private readonly Socket _socket;
        private readonly byte[] _buffer;

        private int _received;

        private readonly Channel<byte[]> _sendQueue = Channel.CreateUnbounded<byte[]>();

        public Connection(Socket socket, byte[] sharedBuffer)
        {
            _socket = socket;
            _buffer = ArrayPool<byte>.Shared.Rent(4096);

            SendStatic("220 localhost SMTP Ready\r\n");
        }

        public bool Tick()
        {
            try
            {
                int read = _socket.Receive(_buffer, _received, _buffer.Length - _received, SocketFlags.None);
                if (read <= 0) return false;

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

                FlushSendQueue();

                return true;
            }
            catch
            {
                return false;
            }
        }

        // =========================
        // SMTP STATE
        // =========================
        private bool Process(ReadOnlySpan<byte> line)
        {
            if (Starts(line, "EHLO") || Starts(line, "HELO"))
            {
                SendStatic("250-localhost\r\n250-PIPELINING\r\n250 OK\r\n");
                return true;
            }

            if (Starts(line, "MAIL FROM") || Starts(line, "RCPT TO"))
            {
                SendStatic("250 OK\r\n");
                return true;
            }

            if (Starts(line, "DATA"))
            {
                SendStatic("354 End data\r\n");
                return true;
            }

            if (Starts(line, "QUIT"))
            {
                SendStatic("221 Bye\r\n");
                return false;
            }

            SendStatic("250 OK\r\n");
            return true;
        }

        // =========================
        // SEND (queued, batchable)
        // =========================
        private void SendStatic(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);
            _sendQueue.Writer.TryWrite(bytes);
        }

        private void FlushSendQueue()
        {
            while (_sendQueue.Reader.TryRead(out var msg))
            {
                _socket.Send(msg, SocketFlags.None);
            }
        }

        public void Dispose()
        {
            try { _socket.Close(); } catch { }
            ArrayPool<byte>.Shared.Return(_buffer);
        }

        // =========================
        // FAST COMPARE
        // =========================
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
    }
}

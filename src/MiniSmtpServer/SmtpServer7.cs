using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;

public static class SmtpServer7
{
    private static Socket _listener = null!;
    private static volatile bool _running;

    private static Reactor[] _reactors = null!;
    private static int _rrIndex;

    public static void Start(int reactorCount = 4)
    {
        Console.WriteLine($"SMTP Server7 starting ({reactorCount} reactors)");

        _listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        _listener.Bind(new IPEndPoint(IPAddress.IPv6Any, 25));
        _listener.Listen(1024);

        _running = true;

        _reactors = new Reactor[reactorCount];
        for (int i = 0; i < reactorCount; i++)
            _reactors[i] = new Reactor();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _running = false;
            try { _listener.Close(); } catch { }
        };

        StartAcceptLoop();

        Console.WriteLine("SMTP Server7 ready");
    }

    // =========================
    // ACCEPT LOOP (ROUND ROBIN)
    // =========================
    private static async void StartAcceptLoop()
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

            var reactor = _reactors[System.Threading.Interlocked.Increment(ref _rrIndex) % _reactors.Length];
            reactor.Assign(client);
        }
    }

    // =========================
    // REACTOR
    // =========================
    private sealed class Reactor
    {
        private readonly SocketAsyncEventArgs _recv;
        private readonly SocketAsyncEventArgs _send;

        private Socket? _socket;

        private byte[] _buffer;
        private int _received;

        public Reactor()
        {
            _buffer = ArrayPool<byte>.Shared.Rent(4096);

            _recv = new SocketAsyncEventArgs();
            _recv.SetBuffer(_buffer, 0, _buffer.Length);
            _recv.Completed += IO_Completed;

            _send = new SocketAsyncEventArgs();
            _send.Completed += IO_Completed;
        }

        public void Assign(Socket socket)
        {
            _socket = socket;

            Send("220 localhost SMTP Ready\r\n");

            StartReceive();
        }

        private void StartReceive()
        {
            if (_socket == null) return;

            _recv.SetBuffer(_buffer, _received, _buffer.Length - _received);

            if (!_socket.ReceiveAsync(_recv))
                ProcessReceive(_recv);
        }

        private void IO_Completed(object? sender, SocketAsyncEventArgs e)
        {
            if (e.LastOperation == SocketAsyncOperation.Receive)
                ProcessReceive(e);
            else if (e.LastOperation == SocketAsyncOperation.Send)
                StartReceive();
        }

        private void ProcessReceive(SocketAsyncEventArgs e)
        {
            if (e.BytesTransferred <= 0)
            {
                Close();
                return;
            }

            _received += e.BytesTransferred;

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
                {
                    Close();
                    return;
                }
            }

            if (start > 0)
            {
                Buffer.BlockCopy(_buffer, start, _buffer, 0, _received - start);
                _received -= start;
            }

            StartReceive();
        }

        // =========================
        // SMTP STATE LOGIC
        // =========================
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

        // =========================
        // SEND (minimal allocations)
        // =========================
        private void Send(string text)
        {
            var data = Encoding.ASCII.GetBytes(text);
            _send.SetBuffer(data, 0, data.Length);

            _socket!.SendAsync(_send);
        }

        private void Close()
        {
            try { _socket?.Close(); } catch { }

            ArrayPool<byte>.Shared.Return(_buffer);
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
    }
}

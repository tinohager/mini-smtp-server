using System.Net;
using System.Net.Sockets;
using System.Text;

public static class SmtpServer4
{
    private static Socket _listener;
    private static volatile bool _running;

    public static void Start()
    {
        Console.WriteLine("SMTP Server4 starting (SAEA reactor)");

        _listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        _listener.Bind(new IPEndPoint(IPAddress.IPv6Any, 25));
        _listener.Listen(1024);

        _running = true;

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            Stop();
        };

        StartAccept(null);

        Console.WriteLine("SMTP Server4 ready");
    }

    public static void Stop()
    {
        _running = false;

        try { _listener.Close(); } catch { }

        Console.WriteLine("SMTP Server4 stopped");
    }

    // =========================
    // ACCEPT (SAEA)
    // =========================
    private static void StartAccept(SocketAsyncEventArgs? args)
    {
        if (!_running) return;

        args ??= new SocketAsyncEventArgs();
        args.Completed += Accept_Completed;

        bool willRaise = _listener.AcceptAsync(args);
        if (!willRaise)
            ProcessAccept(args);
    }

    private static void Accept_Completed(object? sender, SocketAsyncEventArgs e)
        => ProcessAccept(e);

    private static void ProcessAccept(SocketAsyncEventArgs e)
    {
        if (!_running) return;

        var socket = e.AcceptSocket!;
        e.AcceptSocket = null;

        var conn = new Connection(socket);

        conn.Start();

        StartAccept(e);
    }

    // =========================
    // CONNECTION
    // =========================
    private sealed class Connection
    {
        private readonly Socket _socket;

        private readonly byte[] _buffer = new byte[4096];
        private int _received;

        private readonly SocketAsyncEventArgs _recvEvent;
        private readonly SocketAsyncEventArgs _sendEvent;

        public Connection(Socket socket)
        {
            _socket = socket;

            _recvEvent = new SocketAsyncEventArgs();
            _recvEvent.SetBuffer(_buffer, 0, _buffer.Length);
            _recvEvent.Completed += IO_Completed;

            _sendEvent = new SocketAsyncEventArgs();
            _sendEvent.Completed += IO_Completed;
        }

        public void Start()
        {
            Send("220 localhost SMTP Ready\r\n");
            StartReceive();
        }

        // =========================
        // RECEIVE
        // =========================
        private void StartReceive()
        {
            _recvEvent.SetBuffer(_buffer, _received, _buffer.Length - _received);

            if (!_socket.ReceiveAsync(_recvEvent))
                ProcessReceive(_recvEvent);
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
        // SMTP LOGIC
        // =========================
        private bool Process(ReadOnlySpan<byte> line)
        {
            if (Starts(line, "EHLO") || Starts(line, "HELO"))
            {
                Send("250-localhost\r\n250-PIPELINING\r\n250 OK\r\n");
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
        // SEND (zero-copy-ish)
        // =========================
        private void Send(string text)
        {
            var bytes = Encoding.ASCII.GetBytes(text);

            _sendEvent.SetBuffer(bytes, 0, bytes.Length);

            if (!_socket.SendAsync(_sendEvent))
            {
                // immediate send complete
            }
        }

        private void Close()
        {
            try { _socket.Shutdown(SocketShutdown.Both); } catch { }
            try { _socket.Close(); } catch { }
        }

        // =========================
        // FAST COMPARE
        // =========================
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

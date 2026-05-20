using System.Net;
using System.Net.Sockets;
using System.Text;

public static class SmtpServer9
{
    private static Socket _listener = null!;
    private static volatile bool _running;

    public static void Start()
    {
        Console.WriteLine("SMTP Server9 (FAST + STABLE)");

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

        // lifetime anchor
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
    // CONNECTION HANDLER
    // =========================
    private static void Handle(Socket socket)
    {
        using (socket)
        {
            using var stream = new NetworkStream(socket, ownsSocket: false);
            using var reader = new StreamReader(stream, Encoding.ASCII);
            using var writer = new StreamWriter(stream, Encoding.ASCII)
            {
                NewLine = "\r\n",
                AutoFlush = true
            };

            try
            {
                // 🔥 MAILKIT SAFE GREETING (must be FIRST + SYNC)
                writer.WriteLine("220 localhost ESMTP Service Ready");

                while (true)
                {
                    var line = reader.ReadLine();
                    if (line == null)
                        break;

                    if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase) ||
                        line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
                    {
                        writer.WriteLine("250-localhost");
                        writer.WriteLine("250-PIPELINING");
                        writer.WriteLine("250 OK");
                    }
                    else if (line.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase) ||
                             line.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase))
                    {
                        writer.WriteLine("250 OK");
                    }
                    else if (line.Equals("DATA", StringComparison.OrdinalIgnoreCase))
                    {
                        writer.WriteLine("354 End data with <CRLF>.<CRLF>");

                        while (true)
                        {
                            var data = reader.ReadLine();
                            if (data == null || data == ".")
                                break;
                        }

                        writer.WriteLine("250 OK Message accepted");
                    }
                    else if (line.Equals("QUIT", StringComparison.OrdinalIgnoreCase))
                    {
                        writer.WriteLine("221 Bye");
                        break;
                    }
                    else
                    {
                        writer.WriteLine("250 OK");
                    }
                }
            }
            catch
            {
                // ignore broken clients
            }
        }
    }
}

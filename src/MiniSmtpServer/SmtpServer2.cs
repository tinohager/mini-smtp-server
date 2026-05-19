using System.Net;
using System.Net.Sockets;
using System.Text;

public static class SmtpServer2
{
    public static void Start()
    {
        Console.WriteLine("Start Mini SMTP Server 2 [Port:25]");

        var listener = new TcpListener(IPAddress.IPv6Any, 25);
        listener.Server.DualMode = true;
        listener.Start();

        Console.WriteLine("Mini SMTP Server is ready");

        var quit = new ManualResetEvent(false);

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            quit.Set();
        };

        _ = AcceptLoop(listener);

        quit.WaitOne();

        Console.WriteLine("Mini SMTP Server stopped");
    }


    static async Task AcceptLoop(TcpListener listener)
    {
        while (true)
        {
            var client = await listener.AcceptTcpClientAsync();
            _ = HandleClientAsync(client);
        }
    }

    static async Task HandleClientAsync(TcpClient client)
    {
        try
        {
            using var stream = client.GetStream();

            var buffer = new byte[8192];
            int received = 0;

            await WriteLine(stream, "220 localhost SMTP Ready");

            while (true)
            {
                int read = await stream.ReadAsync(buffer.AsMemory(received));
                if (read <= 0) break;

                received += read;

                int lineStart = 0;

                for (int i = 0; i < received; i++)
                {
                    if (buffer[i] != (byte)'\n')
                        continue;

                    var line = new ReadOnlySpan<byte>(buffer, lineStart, i - lineStart);
                    lineStart = i + 1;

                    if (line.Length == 0)
                        continue;

                    if (line[^1] == (byte)'\r')
                        line = line[..^1];

                    if (!ProcessCommand(line, stream))
                        return;
                }

                // shift remaining bytes (partial line)
                if (lineStart > 0)
                {
                    Buffer.BlockCopy(buffer, lineStart, buffer, 0, received - lineStart);
                    received -= lineStart;
                }
            }
        }
        finally
        {
            client.Dispose();
        }
    }

    static bool ProcessCommand(ReadOnlySpan<byte> line, NetworkStream stream)
    {
        if (StartsWith(line, "EHLO") || StartsWith(line, "HELO"))
        {
            _ = WriteLine(stream, "250-localhost");
            _ = WriteLine(stream, "250-PIPELINING");
            _ = WriteLine(stream, "250 OK");
            return true;
        }

        if (StartsWith(line, "MAIL FROM") || StartsWith(line, "RCPT TO"))
        {
            _ = WriteLine(stream, "250 OK");
            return true;
        }

        if (StartsWith(line, "NOOP"))
        {
            _ = WriteLine(stream, "250 OK");
            return true;
        }

        if (StartsWith(line, "DATA"))
        {
            _ = WriteLine(stream, "354 End data with <CR><LF>.<CR><LF>");
            return true;
        }

        if (StartsWith(line, "QUIT"))
        {
            _ = WriteLine(stream, "221 Bye");
            return false;
        }

        _ = WriteLine(stream, "250 OK");
        return true;
    }

    static bool StartsWith(ReadOnlySpan<byte> span, string value)
    {
        var v = value.AsSpan();

        if (span.Length < v.Length)
            return false;

        for (int i = 0; i < v.Length; i++)
        {
            if (char.ToUpperInvariant((char)span[i]) != char.ToUpperInvariant(v[i]))
                return false;
        }

        return true;
    }

    static async Task WriteLine(NetworkStream stream, string text)
    {
        var data = Encoding.ASCII.GetBytes(text + "\r\n");
        await stream.WriteAsync(data);
    }
}

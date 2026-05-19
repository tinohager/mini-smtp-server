using System.Net;
using System.Net.Sockets;
using System.Text;

Console.WriteLine("Start Mini SMTP Server [Port:25]");

var listener = new TcpListener(IPAddress.IPv6Any, 25);
listener.Server.DualMode = true;
listener.Start();
listener.BeginAcceptTcpClient(OnAcceptConnection, listener);

Console.WriteLine("Mini SMTP Server is ready");

var quit = new ManualResetEvent(false);

Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    quit.Set();
};

quit.WaitOne();

Console.WriteLine("Mini SMTP Server stopped");

static void OnAcceptConnection(IAsyncResult asyn)
{
    if (asyn.AsyncState is not TcpListener listener)
    {
        return;
    }

    TcpClient client = listener.EndAcceptTcpClient(asyn);

    _ = Task.Run(async () =>
    {
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        using var writer = new StreamWriter(stream, Encoding.ASCII, leaveOpen: true)
        {
            NewLine = "\r\n",
            AutoFlush = true
        };

        var data = new StringBuilder();

        var sw = System.Diagnostics.Stopwatch.StartNew();

        await writer.WriteLineAsync("220 localhost SMTP Ready");

        while (client.Connected)
        {
            var line = await reader.ReadLineAsync();
            if (line == null) break;

            if (line.StartsWith("EHLO", StringComparison.OrdinalIgnoreCase) || line.StartsWith("HELO", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("250-localhost");
                await writer.WriteLineAsync("250-PIPELINING");
                await writer.WriteLineAsync("250 OK");
            }
            else if (line.StartsWith("NOOP", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("250 OK");
            }
            else if (line.StartsWith("QUIT", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("221 Bye");
                break;
            }
            else if (line.StartsWith("MAIL FROM", StringComparison.OrdinalIgnoreCase) || line.StartsWith("RCPT TO", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("250 OK");
            }
            else if (line.Equals("DATA", StringComparison.OrdinalIgnoreCase))
            {
                await writer.WriteLineAsync("354 End data with <CR><LF>.<CR><LF>");

                while (true)
                {
                    var dataLine = await reader.ReadLineAsync();
                    if (dataLine == null || dataLine == ".")
                        break;

                    data.AppendLine(dataLine);
                }

                await writer.WriteLineAsync("250 OK Message accepted");
            }
            else
            {
                await writer.WriteLineAsync("250 OK");
            }

            sw.Restart();
        }

        client.Close();
        client.Dispose();
    });

    listener.BeginAcceptTcpClient(OnAcceptConnection, listener);
}

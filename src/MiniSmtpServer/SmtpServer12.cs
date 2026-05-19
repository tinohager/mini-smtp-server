using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;

public static class SmtpServer12
{
    private static Socket _listener = null!;
    private static volatile bool _running;

    // Statische Antworten fix im Speicher hinterlegt
    private static readonly byte[] RespReady = "220 localhost ESMTP Service Ready\r\n"u8.ToArray();
    private static readonly byte[] RespEhlo = "250-localhost\r\n250 PIPELINING\r\n250 OK\r\n"u8.ToArray();
    private static readonly byte[] RespOk = "250 OK\r\n"u8.ToArray();
    private static readonly byte[] RespDataStart = "354 End data with <CRLF>.<CRLF>\r\n"u8.ToArray();
    private static readonly byte[] RespDataOk = "250 OK Message accepted\r\n"u8.ToArray();
    private static readonly byte[] RespBye = "221 Bye\r\n"u8.ToArray();

    public static void Start()
    {
        Console.WriteLine("SMTP Server12 (Ultimate - System.IO.Pipelines)");

        _listener = new Socket(SocketType.Stream, ProtocolType.Tcp);
        _listener.Bind(new IPEndPoint(IPAddress.IPv6Any, 25));
        _listener.Listen(1024);
        _running = true;

        _ = AcceptLoop();

        Console.WriteLine("SMTP ready");

        Thread.Sleep(Timeout.Infinite);
    }

    private static async Task AcceptLoop()
    {
        while (_running)
        {
            try
            {
                var client = await _listener.AcceptAsync().ConfigureAwait(false);
                _ = HandleClientPipelineAsync(client);
            }
            catch
            {
                break;
            }
        }
    }

    private static async Task HandleClientPipelineAsync(Socket socket)
    {
        var pipe = MemoryPool<byte>.Shared;
        using var stream = new NetworkStream(socket, ownsSocket: true);
        var reader = PipeReader.Create(stream, new StreamPipeReaderOptions(pipe));
        var writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(pipe));

        try
        {
            // Initiales Greeting in den Buffer schreiben und sofort ins Netzwerk flashen
            writer.Write(RespReady);
            await writer.FlushAsync().ConfigureAwait(false);

            bool dataMode = false;

            while (true)
            {
                ReadResult result = await reader.ReadAsync().ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                bool needsFlush = false;

                // Verarbeite alle im Buffer liegenden Zeilen
                while (TryReadLine(ref buffer, ref dataMode, writer, out bool shouldQuit, ref needsFlush))
                {
                    if (shouldQuit)
                    {
                        // Beim Beenden restliche Daten (z.B. QUIT-Antwort) flashen und raus
                        if (needsFlush) await writer.FlushAsync().ConfigureAwait(false);
                        return;
                    }
                }

                // Wenn während der Verarbeitung Antworten generiert wurden, jetzt gesammelt flashen!
                // Das nennt sich "Smart Batching" und spart extrem viele Netzwerk-Syscalls.
                if (needsFlush)
                {
                    await writer.FlushAsync().ConfigureAwait(false);
                }

                reader.AdvanceTo(buffer.Start, buffer.End);

                if (result.IsCompleted || result.IsCanceled) break;
            }
        }
        catch
        {
        }
        finally
        {
            await reader.CompleteAsync().ConfigureAwait(false);
            await writer.CompleteAsync().ConfigureAwait(false);
        }
    }

    private static bool TryReadLine(ref ReadOnlySequence<byte> buffer, ref bool dataMode, PipeWriter writer, out bool shouldQuit, ref bool needsFlush)
    {
        shouldQuit = false;

        // 1. DATA-Modus: Ende-Tag suchen
        if (dataMode)
        {
            var seqReader = new SequenceReader<byte>(buffer);
            bool foundSequence = false;

            while (!seqReader.End)
            {
                if (seqReader.TryAdvanceTo((byte)'\r'))
                {
                    if (seqReader.UnreadSpan.StartsWith("\n.\r\n"u8))
                    {
                        seqReader.Advance(4);
                        foundSequence = true;
                        break;
                    }
                }
                else
                {
                    break;
                }
            }

            if (foundSequence)
            {
                dataMode = false;
                writer.Write(RespDataOk);
                needsFlush = true; // Signalisiert, dass Daten gesendet werden muessen
                buffer = buffer.Slice(seqReader.Position);
                return true;
            }

            if (buffer.Length > 4)
            {
                buffer = buffer.Slice(buffer.GetPosition(buffer.Length - 4));
            }
            return false;
        }

        // 2. Befehls-Modus (Zeilenweise parsen)
        var commandReader = new SequenceReader<byte>(buffer);
        if (commandReader.TryReadTo(out ReadOnlySequence<byte> lineSequence, (byte)'\n'))
        {
            ReadOnlySpan<byte> line = lineSequence.IsSingleSegment ? lineSequence.First.Span : lineSequence.ToArray();

            if (line.Length > 0 && line[^1] == (byte)'\r')
            {
                line = line[..^1];
            }

            if (line.Length > 0)
            {
                ProcessCommand(line, ref dataMode, writer, out shouldQuit);
                needsFlush = true; // Signalisiert, dass eine Antwort im Buffer liegt
            }

            buffer = buffer.Slice(commandReader.Position);
            return true;
        }

        return false;
    }

    private static void ProcessCommand(ReadOnlySpan<byte> line, ref bool dataMode, PipeWriter writer, out bool shouldQuit)
    {
        shouldQuit = false;

        if (line.StartsWith("EHLO"u8) || line.StartsWith("HELO"u8))
        {
            writer.Write(RespEhlo);
            return;
        }

        if (line.StartsWith("MAIL FROM"u8) || line.StartsWith("RCPT TO"u8))
        {
            writer.Write(RespOk);
            return;
        }

        if (line.StartsWith("DATA"u8))
        {
            dataMode = true;
            writer.Write(RespDataStart);
            return;
        }

        if (line.StartsWith("QUIT"u8))
        {
            shouldQuit = true;
            writer.Write(RespBye);
            return;
        }

        writer.Write(RespOk);
    }
}

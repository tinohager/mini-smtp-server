using System.Buffers;
using System.IO.Pipelines;
using System.Net;
using System.Net.Sockets;

public static class SmtpServer12
{
    private static Socket _listener = null!;
    private static volatile bool _running;

    // Static pre-allocated responses kept in memory (Avoids any string/byte allocations in the hot path)
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
        // Automatically utilizes the highly efficient shared memory pool of .NET
        var pipe = MemoryPool<byte>.Shared;
        using var stream = new NetworkStream(socket, ownsSocket: true);
        var reader = PipeReader.Create(stream, new StreamPipeReaderOptions(pipe));
        var writer = PipeWriter.Create(stream, new StreamPipeWriterOptions(pipe));

        try
        {
            // Write initial greeting to the buffer and flush immediately to the network
            writer.Write(RespReady);
            await writer.FlushAsync().ConfigureAwait(false);

            bool dataMode = false;

            while (true)
            {
                ReadResult result = await reader.ReadAsync().ConfigureAwait(false);
                ReadOnlySequence<byte> buffer = result.Buffer;

                bool needsFlush = false;

                // Process all lines currently available in the buffer
                while (TryReadLine(ref buffer, ref dataMode, writer, out bool shouldQuit, ref needsFlush))
                {
                    if (shouldQuit)
                    {
                        // On exit, flush remaining data (e.g., QUIT response) and terminate
                        if (needsFlush) await writer.FlushAsync().ConfigureAwait(false);
                        return;
                    }
                }

                // If responses were generated during processing, flush them collectively now!
                // This is called "Smart Batching" and saves a massive amount of network syscalls.
                if (needsFlush)
                {
                    await writer.FlushAsync().ConfigureAwait(false);
                }

                // Notify the reader how far we have successfully processed (No buffer recopying needed!)
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

        // 1. DATA Mode: Search for the end tag
        if (dataMode)
        {
            var seqReader = new SequenceReader<byte>(buffer);
            bool foundSequence = false;

            while (!seqReader.End)
            {
                // Jump directly to the next '\r' (Hardware accelerated)
                if (seqReader.TryAdvanceTo((byte)'\r'))
                {
                    // Check directly within the current memory segment if the end tag matches
                    if (seqReader.UnreadSpan.StartsWith("\n.\r\n"u8))
                    {
                        seqReader.Advance(4); // Skip the rest of the tag (\n.\r\n)
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
                needsFlush = true; // Indicates that data needs to be sent
                buffer = buffer.Slice(seqReader.Position);
                return true;
            }

            // If the end tag is not found, retain only the last 4 bytes in the buffer, 
            // just in case the \r\n.\r\n gets sliced across a network packet boundary.
            if (buffer.Length > 4)
            {
                buffer = buffer.Slice(buffer.GetPosition(buffer.Length - 4));
            }
            return false;
        }

        // 2. Command Mode (Parse line by line without string allocations)
        var commandReader = new SequenceReader<byte>(buffer);
        if (commandReader.TryReadTo(out ReadOnlySequence<byte> lineSequence, (byte)'\n'))
        {
            // Extract the direct span (Single segment in 99% of cases, making this extremely cheap)
            ReadOnlySpan<byte> line = lineSequence.IsSingleSegment ? lineSequence.First.Span : lineSequence.ToArray();

            if (line.Length > 0 && line[^1] == (byte)'\r')
            {
                line = line[..^1];
            }

            if (line.Length > 0)
            {
                ProcessCommand(line, ref dataMode, writer, out shouldQuit);
                needsFlush = true; // Indicates that a response is pending in the buffer
            }

            buffer = buffer.Slice(commandReader.Position);
            return true;
        }

        return false;
    }

    private static void ProcessCommand(ReadOnlySpan<byte> line, ref bool dataMode, PipeWriter writer, out bool shouldQuit)
    {
        shouldQuit = false;

        // .NET heavily optimizes StartsWith with u8 literals using SIMD/vectorization
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

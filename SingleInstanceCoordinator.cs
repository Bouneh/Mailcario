using System.Threading;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Buffers.Binary;

namespace Mailcario;

/// <summary>Owns a per-Windows-session mutex and signals the running instance on a second launch.</summary>
public sealed class SingleInstanceCoordinator : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _activationEvent;
    private readonly RegisteredWaitHandle? _registration;
    private readonly bool _ownsMutex;
    private readonly CancellationTokenSource _pipeCancellation = new();

    public bool IsPrimary => _ownsMutex;

    public SingleInstanceCoordinator(string name, Action onActivate) :
        this(name, _ => onActivate(), null) { }

    public SingleInstanceCoordinator(string name, Action<string?> onActivate, string? activationPayload)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Any(c => !char.IsAsciiLetterOrDigit(c)))
            throw new ArgumentException("Invalid instance name.", nameof(name));
        _activationEvent = new EventWaitHandle(false, EventResetMode.AutoReset,
            @"Local\Mailcario.Activate." + name);
        _mutex = new Mutex(true, @"Local\Mailcario.Instance." + name, out bool createdNew);
        if (createdNew) _ownsMutex = true;
        else
        {
            try { _ownsMutex = _mutex.WaitOne(0); }
            catch (AbandonedMutexException) { _ownsMutex = true; }
        }
        if (_ownsMutex)
        {
            _registration = ThreadPool.RegisterWaitForSingleObject(_activationEvent,
                (_, timedOut) => { if (!timedOut) onActivate(null); }, null, Timeout.Infinite, false);
            _ = ListenForPayloadsAsync(name, onActivate, _pipeCancellation.Token);
        }
        else if (activationPayload is null || !SendPayload(name, activationPayload)) _activationEvent.Set();
    }

    private async Task ListenForPayloadsAsync(string name, Action<string?> onActivate, CancellationToken token)
    {
        string pipeName = "Mailcario.Activate." + name + "." +
            System.Diagnostics.Process.GetCurrentProcess().SessionId;
        while (!token.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                await server.WaitForConnectionAsync(token);
                using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                readTimeout.CancelAfter(TimeSpan.FromSeconds(3));
                byte[] header = new byte[4];
                await server.ReadExactlyAsync(header, readTimeout.Token);
                int length = BinaryPrimitives.ReadInt32LittleEndian(header);
                if (length is < 1 or > 8192) continue;
                byte[] data = new byte[length];
                await server.ReadExactlyAsync(data, readTimeout.Token);
                onActivate(Encoding.UTF8.GetString(data));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { break; }
            catch (OperationCanceledException) { }
            catch (EndOfStreamException) { }
            catch (IOException) { }
            catch (ObjectDisposedException) { break; }
        }
    }

    private static bool SendPayload(string name, string payload)
    {
        byte[] data = Encoding.UTF8.GetBytes(payload);
        if (data.Length is < 1 or > 8192) return false;
        try
        {
            string pipeName = "Mailcario.Activate." + name + "." +
                System.Diagnostics.Process.GetCurrentProcess().SessionId;
            using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
            client.Connect(1500);
            using var writer = new BinaryWriter(client, Encoding.UTF8, leaveOpen: true);
            writer.Write(data.Length);
            writer.Write(data);
            writer.Flush();
            return true;
        }
        catch (Exception ex) when (ex is IOException or TimeoutException or UnauthorizedAccessException)
        { return false; }
    }

    public void Dispose()
    {
        _pipeCancellation.Cancel();
        _registration?.Unregister(null);
        _activationEvent.Dispose();
        if (_ownsMutex) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}

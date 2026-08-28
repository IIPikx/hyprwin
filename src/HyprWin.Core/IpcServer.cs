using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace HyprWin.Core;

/// <summary>
/// Named Pipe IPC Server providing hyprctl-compatible CLI and external automation.
/// Pipe: \\.\pipe\hyprwin-ipc
/// </summary>
public sealed class IpcServer : IDisposable
{
    public const string PipeName = "hyprwin-ipc";
    private readonly Func<string, Task<string>> _commandHandler;
    private CancellationTokenSource? _cts;
    private Task? _serverTask;
    private bool _disposed;

    public IpcServer(Func<string, Task<string>> commandHandler)
    {
        _commandHandler = commandHandler;
    }

    public void Start()
    {
        if (_serverTask != null) return;

        _cts = new CancellationTokenSource();
        _serverTask = Task.Run(() => ServerLoopAsync(_cts.Token));
        Logger.Instance.Info($"IPC Server started on pipe \\\\.\\pipe\\{PipeName}");
    }

    private async Task ServerLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var pipeServer = new NamedPipeServerStream(
                    PipeName,
                    PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);

                await pipeServer.WaitForConnectionAsync(ct);

                using var reader = new StreamReader(pipeServer, Encoding.UTF8);
                using var writer = new StreamWriter(pipeServer, Encoding.UTF8) { AutoFlush = true };

                string? line = await reader.ReadLineAsync(ct);
                if (!string.IsNullOrWhiteSpace(line))
                {
                    string response = await _commandHandler(line.Trim());
                    await writer.WriteLineAsync(response);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Logger.Instance.Debug($"IPC Server connection error: {ex.Message}");
                await Task.Delay(100, ct);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cts?.Cancel();
        _cts?.Dispose();
        Logger.Instance.Info("IPC Server stopped");
    }
}

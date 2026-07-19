using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace LiveLyricOverlayApp.Engine
{
    public class PlaybackEvent
    {
        public string EventType { get; set; } = string.Empty; // "play", "pause", "stop", "seek"
        public double Time { get; set; } // Current time in seconds
        public string FilePath { get; set; } = string.Empty;
    }

    public class IpcClient
    {
        public event EventHandler<PlaybackEvent>? OnPlaybackEvent;
        private CancellationTokenSource _cts = new CancellationTokenSource();

        public void Start()
        {
            Task.Run(RunLoop);
        }

        public void Stop()
        {
            _cts.Cancel();
        }

        private async Task RunLoop()
        {
            while (!_cts.IsCancellationRequested)
            {
                NamedPipeClientStream? client = null;
                try
                {
                    client = new NamedPipeClientStream(".", "LiveLyricOverlayPipe", PipeDirection.In, PipeOptions.Asynchronous);
                    await client.ConnectAsync(_cts.Token);
                    using var reader = new StreamReader(client, Encoding.UTF8);

                    while (client.IsConnected && !_cts.IsCancellationRequested)
                    {
                        // ReadLineAsync has no CancellationToken overload on .NET 8.
                        // Use Task.WhenAny to make it cancellable.
                        var readTask = reader.ReadLineAsync();
                        var cancelTask = Task.Delay(Timeout.Infinite, _cts.Token);
                        var completed = await Task.WhenAny(readTask, cancelTask);

                        if (completed == cancelTask)
                        {
                            // Cancellation requested — dispose the pipe to unblock ReadLineAsync
                            client.Dispose();
                            client = null;
                            break;
                        }

                        var line = await readTask;
                        if (line == null) break; // pipe closed / EOF

                        try
                        {
                            var msg = JsonSerializer.Deserialize<PlaybackEvent>(line, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                            if (msg != null)
                            {
                                OnPlaybackEvent?.Invoke(this, msg);
                            }
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine("IPC Parse Error: " + ex.Message);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Connection failed or pipe broken, retry in a bit
                }
                finally
                {
                    client?.Dispose();
                }

                // Retry delay (cancellation-aware)
                if (!_cts.IsCancellationRequested)
                {
                    try { await Task.Delay(1000, _cts.Token); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
    }
}


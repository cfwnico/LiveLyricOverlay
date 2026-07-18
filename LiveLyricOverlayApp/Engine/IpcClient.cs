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
                try
                {
                    using var client = new NamedPipeClientStream(".", "LiveLyricOverlayPipe", PipeDirection.In, PipeOptions.Asynchronous);
                    await client.ConnectAsync(_cts.Token);
                    using var reader = new StreamReader(client, Encoding.UTF8);

                    while (client.IsConnected && !_cts.IsCancellationRequested)
                    {
                        var line = await reader.ReadLineAsync();
                        if (line != null)
                        {
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
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception)
                {
                    // Connection failed or pipe broken, retry in a bit
                    await Task.Delay(1000, _cts.Token);
                }
            }
        }
    }
}

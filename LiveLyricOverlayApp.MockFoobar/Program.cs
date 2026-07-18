using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LiveLyricOverlayApp.MockFoobar
{
    class Program
    {
        static async Task Main(string[] args)
        {
            Console.WriteLine("Mock Foobar2000 Server for LiveLyricOverlay");
            Console.WriteLine("Waiting for client connection...");

            using var server = new NamedPipeServerStream("LiveLyricOverlayPipe", PipeDirection.Out, 1, PipeTransmissionMode.Message, PipeOptions.Asynchronous);
            await server.WaitForConnectionAsync();
            Console.WriteLine("Client connected!");

            using var writer = new StreamWriter(server, Encoding.UTF8) { AutoFlush = true };

            var filePath = @"e:\LiveLyricOverlay\AiScReam - 愛♡スクリ～ム！.lrc";

            // Tell client a new track is loaded
            var msg = new { EventType = "new_track", FilePath = filePath, Time = 0.0 };
            await writer.WriteLineAsync(JsonSerializer.Serialize(msg));
            Console.WriteLine("Sent new_track event.");

            await Task.Delay(500); // Wait half a second

            // Tell client playback has started
            var msg2 = new { EventType = "play", FilePath = filePath, Time = 0.0 };
            await writer.WriteLineAsync(JsonSerializer.Serialize(msg2));
            Console.WriteLine("Sent play event.");

            // Simulate sending a sync event every 5 seconds
            double currentTime = 0;
            for (int i = 0; i < 20; i++)
            {
                await Task.Delay(5000);
                currentTime += 5.0;
                var msg3 = new { EventType = "seek", FilePath = filePath, Time = currentTime };
                await writer.WriteLineAsync(JsonSerializer.Serialize(msg3));
                Console.WriteLine($"Sent sync (seek) event: {currentTime}s");
            }

            Console.WriteLine("Finished mock playback.");
        }
    }
}

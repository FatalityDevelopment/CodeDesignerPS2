using CodeDesigner.Mips64LE.Common;

namespace CodeDesigner.Mips64LE
{
    class Program
    {
        static async Task Main(string[] args)
        {
            // Entry point for the MIPS64 Emotion Engine socket server
            Console.WriteLine("MIPS64 Emotion Engine - Socket Server");
            Console.WriteLine();
            
            // Start socket server for VSCode extension communication
            Console.WriteLine("Starting socket server on port 8765...");
            var server = new SocketServer(8765);
            
            server.MessageReceived += (sender, message) =>
            {
                Console.WriteLine($"[Server] Received: {message.Substring(0, Math.Min(100, message.Length))}...");
            };

            server.ClientConnected += (sender, endpoint) =>
            {
                Console.WriteLine($"[Server] Client connected: {endpoint}");
            };

            server.ClientDisconnected += (sender, e) =>
            {
                Console.WriteLine("[Server] Client disconnected");
            };

            var serverTask = server.StartAsync();
            
            // Use CancellationToken to handle shutdown
            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (sender, e) =>
            {
                e.Cancel = true;
                cts.Cancel();
                Console.WriteLine("\nShutting down server...");
                server.Stop();
            };

            Console.WriteLine("Server is running. Press Ctrl+C to stop.");
            
            try
            {
                await Task.Delay(-1, cts.Token); // Wait indefinitely until cancelled
            }
            catch (TaskCanceledException)
            {
                // Expected when Ctrl+C is pressed
            }
            
            await serverTask;
        }
    }
}

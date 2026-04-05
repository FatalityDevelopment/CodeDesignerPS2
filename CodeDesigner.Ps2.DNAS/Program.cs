using System.Net;
using System.Text;

var listener = new HttpListener();
listener.Prefixes.Add("http://localhost:8080/");
listener.Start();

while (true)
{
    var ctx = await listener.GetContextAsync();
    _ = HandleRequest(ctx);
}

async Task HandleRequest(HttpListenerContext ctx)
{
    var req = ctx.Request;
    var resp = ctx.Response;

    // Read raw POST body
    using var ms = new MemoryStream();
    await req.InputStream.CopyToAsync(ms);
    var body = ms.ToArray();

    // Extract game ID at offset 0x2C, length 8
    string gameId = body.Length > 0x34
        ? Encoding.ASCII.GetString(body, 0x2C, 8).TrimEnd('\0')
        : "unknown";
    Console.WriteLine($"Game ID: {gameId}");

    // Look up response file: Data/us-gw/<gameId>.raw or fall back to error.raw
    string path = Path.Combine("Data", "us-gw", $"{gameId}.raw");
    if (!File.Exists(path))
        path = Path.Combine("Data", "us-gw", "error.raw");

    if (File.Exists(path))
    {
        var bytes = await File.ReadAllBytesAsync(path);
        resp.ContentType = "application/octet-stream";
        resp.ContentLength64 = bytes.Length;
        await resp.OutputStream.WriteAsync(bytes);
        Console.WriteLine($"Sent {path} ({bytes.Length} bytes)");
    }
    else
    {
        resp.StatusCode = 404;
    }
    resp.Close();
}
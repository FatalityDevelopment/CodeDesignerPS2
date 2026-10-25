// Offline harness for the Zipper webviews (workbench, model editor). Serves the built webview bundle from
// dist/webview and answers the webview's bridge requests by calling the bridge's own Zipper handlers through
// reflection, so the page sees byte-identical payloads to the VS Code extension without VS Code or the bridge
// process. Usage (from vscode/codedesigner, after `npx webpack --mode development`):
//
//   dotnet run --project scripts/zipper-harness -- [port] [distDir]
//
// then open http://localhost:<port>/?path=G:/RUN/M51.ZDB (workbench) or add &page=model-editor.
using System.Net;
using System.Reflection;
using System.Text;

_ = typeof(CodeDesigner.Ps2.FileFormats.Zipper.ZDB.ZdbArchive).Assembly;
Assembly bridge = Assembly.Load("CodeDesigner.Ps2.Bridge");
Type serviceType = bridge.GetTypes().First(t => t.Name == "ZipperFileFormatsService");
object service = Activator.CreateInstance(serviceType, true)!;

string Call(string method, params object?[] args)
{
    MethodInfo m = serviceType.GetMethod(method, BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
        ?? throw new MissingMethodException(serviceType.Name, method);
    return (string)m.Invoke(m.IsStatic ? null : service, args)!;
}

// POST /bridge {Command: ...}: the bridge's own handlers for the Zipper services (asset sessions, weapons, file
// formats), dispatched through its CommandRegistry exactly as the socket server does.
Type registryType = bridge.GetTypes().First(t => t.Name == "CommandRegistry");
object registry = Activator.CreateInstance(registryType, true)!;
foreach (string name in new[] { "ZipperFileFormatsService", "ZipperWeaponsService", "ZipperAssetService" })
{
    Type? t = bridge.GetTypes().FirstOrDefault(x => x.Name == name);
    if (t == null) { Console.WriteLine($"(bridge build has no {name})"); continue; }
    t.GetMethod("Register")!.Invoke(Activator.CreateInstance(t, true), new[] { registry });
}
Type messageType = bridge.GetType("CodeDesigner.Ps2.Bridge.CommandMessage")!;
string Dispatch(string json)
{
    object message = System.Text.Json.JsonSerializer.Deserialize(json, messageType)!;
    string command = (string)messageType.GetProperty("Command")!.GetValue(message)!;
    object?[] tryArgs = { command, null };
    if (!(bool)registryType.GetMethod("TryGet")!.Invoke(registry, tryArgs)!)
        return $"{{\"Success\":false,\"Error\":\"Unknown command {command}\"}}";
    return (string)((Delegate)tryArgs[1]!).DynamicInvoke(message)!;
}

int port = args.Length > 0 ? int.Parse(args[0]) : 18765;
string dist = args.Length > 1 ? args[1] : Path.Combine(Directory.GetCurrentDirectory(), "dist", "webview");
string html = Path.Combine(AppContext.BaseDirectory, "index.html");

var listener = new HttpListener();
listener.Prefixes.Add($"http://localhost:{port}/");
listener.Start();
Console.WriteLine($"Zipper harness on http://localhost:{port}/ serving {dist}");

while (true)
{
    HttpListenerContext ctx = listener.GetContext();
    _ = Task.Run(() =>
    {
        try
        {
            string path = ctx.Request.Url!.AbsolutePath;
            var q = ctx.Request.QueryString;
            byte[] body;
            string type = "application/json";
            if (path == "/")
            {
                body = File.ReadAllBytes(html);
                type = "text/html";
            }
            else if (path.StartsWith("/dist/"))
            {
                string file = Path.GetFullPath(Path.Combine(dist, path[6..]));
                if (!file.StartsWith(Path.GetFullPath(dist), StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException(path);
                body = File.ReadAllBytes(file);
                type = "text/javascript";
            }
            else if (path == "/api")
            {
                string target = q["path"] ?? "";
                string result = q["cmd"] switch
                {
                    "inspect" => Call("HandleInspect", target),
                    "readNode" => Call("HandleReadNode", target, (int?)int.Parse(q["idx"] ?? "-1")),
                    "readAll" => Call("HandleReadAllNodes", target),
                    "mps" => Call("HandleInspectMps", target),
                    _ => "{\"Success\":false,\"Error\":\"unknown cmd\"}",
                };
                body = Encoding.UTF8.GetBytes(result);
            }
            else if (path == "/bridge" && ctx.Request.HttpMethod == "POST")
            {
                using var reader = new StreamReader(ctx.Request.InputStream);
                body = Encoding.UTF8.GetBytes(Dispatch(reader.ReadToEnd()));
            }
            else
            {
                ctx.Response.StatusCode = 404;
                body = Array.Empty<byte>();
            }
            ctx.Response.ContentType = type;
            ctx.Response.OutputStream.Write(body);
            ctx.Response.Close();
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            try { ctx.Response.StatusCode = 500; ctx.Response.Close(); } catch { }
        }
    });
}

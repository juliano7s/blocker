using System.Net;

namespace Blocker.Relay;

public static class Program
{
    private static RelayServer? _server;

    public static async Task Main(string[] args)
    {
        var options = RelayOptions.FromEnvironment();
        _server = new RelayServer(options);
        Logger.Info($"Blocker.Relay starting on {options.ListenUrl}");

        var listener = new HttpListener();
        listener.Prefixes.Add(options.ListenUrl);
        listener.Start();
        Logger.Info("Listening.");

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

        try
        {
            while (!cts.IsCancellationRequested)
            {
                HttpListenerContext ctx;
                try { ctx = await listener.GetContextAsync().WaitAsync(cts.Token); }
                catch (OperationCanceledException) { break; }
                _ = Task.Run(() => HandleRequest(ctx, cts.Token));
            }
        }
        finally
        {
            listener.Stop();
            Logger.Info("Shut down.");
        }
    }

    private static async Task HandleRequest(HttpListenerContext ctx, CancellationToken ct)
    {
        try
        {
            if (ctx.Request.Url?.AbsolutePath == "/healthz")
            {
                ctx.Response.StatusCode = 200;
                await ctx.Response.OutputStream.WriteAsync("ok"u8.ToArray(), ct);
                ctx.Response.Close();
                return;
            }
            if (ctx.Request.Url?.AbsolutePath == "/blocker/ws-relay" && ctx.Request.IsWebSocketRequest)
            {
                await _server!.HandleWebSocket(ctx, ct);
                return;
            }
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
        }
        catch (Exception ex)
        {
            Logger.Warn($"Unhandled request error: {ex.Message}");
            try { ctx.Response.Abort(); } catch { }
        }
    }
}

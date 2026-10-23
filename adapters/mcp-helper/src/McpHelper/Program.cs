using System.Net;
using System.Security.Cryptography;
using System.Text;
using McpHelper;
using McpHelper.Core;
using Microsoft.Extensions.Logging.Console;
using ModelContextProtocol.AspNetCore;

try
{
    if (args.Length == 1 && args[0] == "--version") { Console.WriteLine("MCP Helper 0.1.0"); return; }
    var http = args.Length > 0 && args[0] == "serve";
    var stdio = args.Length > 0 && args[0] == "stdio";
    if ((!http && !stdio) || args.Length != 3 || args[1] != "--config")
        throw new ArgumentException("Usage: McpHelper (stdio|serve) --config <local-json-path>");
    var config = HelperConfig.Read(args[2]);
    config.Validate(http);
    var engine = new CodeEngine(config.WorkspaceRoot);
    if (stdio)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(engine);
        builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<HelperTools>();
        await builder.Build().RunAsync();
    }
    else
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], ContentRootPath = AppContext.BaseDirectory });
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.WebHost.ConfigureKestrel(o => { o.Listen(IPAddress.Loopback, config.Port); o.Limits.MaxRequestBodySize = 65536; });
        builder.Services.AddSingleton(config);
        builder.Services.AddSingleton(engine);
        builder.Services.AddMcpServer().WithHttpTransport(o => o.SessionMode = HttpServerSessionMode.Stateless).WithTools<HelperTools>();
        var app = builder.Build();
        app.Use(async (context, next) =>
        {
            var host = context.Request.Host;
            if (host.Host != "127.0.0.1" || host.Port != config.Port) { context.Response.StatusCode = 403; return; }
            if (context.Request.Headers.TryGetValue("Origin", out var origin) && origin != $"http://127.0.0.1:{config.Port}")
            { context.Response.StatusCode = 403; return; }
            var supplied = context.Request.Headers.Authorization.ToString();
            var expected = "Bearer " + config.Token;
            if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(supplied), Encoding.UTF8.GetBytes(expected)))
            { context.Response.StatusCode = 401; return; }
            context.Response.Headers.CacheControl = "no-store";
            await next(context);
        });
        app.MapMcp("/mcp");
        await app.RunAsync();
    }
}
catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
{
    Console.Error.WriteLine("Helper startup failed: invalid config, unavailable workspace or unreadable file. " + ex.GetType().Name);
    Environment.ExitCode = 2;
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

Log.Logger = new LoggerConfiguration()
              .MinimumLevel.Information()
              .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
              .Enrich.FromLogContext()
              .WriteTo.Console()
              .WriteTo.File(@"logs/editortool.log", 
                  rollingInterval: RollingInterval.Day,
                  outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
              .CreateLogger();

var builder = WebApplication.CreateBuilder(args);

// Configure built-in logging to use Serilog
builder.Host.UseSerilog();

builder.Services.AddMemoryCache();
builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
    app.UseWebAssemblyDebugging();
}
else
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

// In Development, forcing HTTPS commonly results in "TypeError: Failed to fetch" in Blazor WASM
// when the dev certificate isn't trusted yet. Keep local dev on HTTP unless explicitly needed.
if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

// Add request logging to see what's being called
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000}ms";
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
        diagnosticContext.Set("UserAgent", httpContext.Request.Headers["User-Agent"].ToString());
    };
});

app.UseBlazorFrameworkFiles();

// Compatibility shim: if a cached client requests a hashed Syncfusion script name
// (e.g., /_content/Syncfusion.Blazor/scripts/syncfusion-blazor-<hash>.min.js),
// rewrite it to the canonical script we actually ship.
app.Use(async (context, next) =>
{
    if (HttpMethods.IsGet(context.Request.Method) || HttpMethods.IsHead(context.Request.Method))
    {
        var path = context.Request.Path.Value;
        if (!string.IsNullOrEmpty(path)
            && path.StartsWith("/_content/Syncfusion.Blazor/scripts/syncfusion-blazor-", StringComparison.OrdinalIgnoreCase)
            && path.EndsWith(".min.js", StringComparison.OrdinalIgnoreCase))
        {
            context.Request.Path = "/_content/Syncfusion.Blazor/scripts/syncfusion-blazor.min.js";
        }
    }

    await next();
});

app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();
app.MapControllers();
app.MapFallbackToFile("index.html");

Log.Information("Application starting");
try
{
    app.Run();
}
finally
{
    Log.CloseAndFlush();
}

using eSDSCom.Editor.Client;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Text;
using Syncfusion.Blazor;

var builder = WebAssemblyHostBuilder.CreateDefault(args);

// Local-only override file (do not commit secrets): Client/wwwroot/appsettings.local.json
// If present, it will be fetched at runtime and merged into configuration.
try
{
	using var localConfigHttp = new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) };
	// IMPORTANT: in Blazor WebAssembly, HTTP response streams cannot be read synchronously.
	// Some configuration providers read the provided stream synchronously, which triggers:
	//   net_http_synchronous_reads_not_supported
	// Workaround: fetch the content asynchronously first, then feed a MemoryStream to the configuration provider.
	var localConfigJson = await localConfigHttp.GetStringAsync("appsettings.local.json");
	await using var localConfigStream = new MemoryStream(Encoding.UTF8.GetBytes(localConfigJson));
	builder.Configuration.AddJsonStream(localConfigStream);
}
catch (HttpRequestException)
{
	// File not present (or unreachable). This is expected in CI and for contributors without a license key.
}

var syncfusionLicenseKey = builder.Configuration["Syncfusion:LicenseKey"];
if (!string.IsNullOrWhiteSpace(syncfusionLicenseKey))
{
	Syncfusion.Licensing.SyncfusionLicenseProvider.RegisterLicense(syncfusionLicenseKey);
}
else
{
	// If you see Syncfusion licensing banners/popups, set Syncfusion:LicenseKey in Client/wwwroot/appsettings.json.
	Console.WriteLine("Syncfusion license key missing (Syncfusion:LicenseKey). UI components may show a licensing banner.");
}

builder.RootComponents.Add<App>("#app");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });

builder.Services.AddSyncfusionBlazor();

await builder.Build().RunAsync();

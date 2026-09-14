using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;
using KnowledgeHub.Client;
using KnowledgeHub.Client.Services;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddBootstrapBlazor();

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<SourceApiClient>();
builder.Services.AddScoped<SearchApiClient>();
builder.Services.AddTransient<McpMonitorClient>();

await builder.Build().RunAsync();

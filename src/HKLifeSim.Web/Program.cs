using HKLifeSim.Core.Persistence;
using HKLifeSim.Web;
using HKLifeSim.Web.Services;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

var builder = WebAssemblyHostBuilder.CreateDefault(args);
builder.RootComponents.Add<App>("#app");
builder.RootComponents.Add<HeadOutlet>("head::after");

builder.Services.AddScoped(sp => new HttpClient { BaseAddress = new Uri(builder.HostEnvironment.BaseAddress) });
builder.Services.AddScoped<ISaveStore, LocalStorageSaveStore>();
builder.Services.AddScoped<GameSessionService>();

await builder.Build().RunAsync().ConfigureAwait(false);

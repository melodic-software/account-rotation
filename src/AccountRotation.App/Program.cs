using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

// Loopback only: the page is a local control surface, never a network service.
builder.WebHost.ConfigureKestrel(static kestrel => kestrel.ListenLocalhost(48211));

WebApplication app = builder.Build();

app.MapGet("/healthz", static () => Results.Ok(new { status = "ok" }));

await app.RunAsync();

using AccountRotation.App.Configuration;
using AccountRotation.App.Hosting;
using AccountRotation.Core;
using Microsoft.AspNetCore.Builder;

Result<StartupArguments, string> parsed = StartupArguments.Parse(args);
if (parsed.IsFailure)
{
    await Console.Error.WriteLineAsync(parsed.Error);
    return 2;
}

if (parsed.Value.ShowHelp)
{
    await Console.Out.WriteLineAsync(StartupArguments.Usage);
    return 0;
}

if (parsed.Value.ShowVersion)
{
    await Console.Out.WriteLineAsync(AppComposition.Version);
    return 0;
}

WebApplicationBuilder builder = WebApplication.CreateBuilder(args);
Result<Unit, string> composed = await AppComposition.ComposeAsync(builder, parsed.Value, CancellationToken.None);
if (composed.IsFailure)
{
    await Console.Error.WriteLineAsync(composed.Error);
    return 1;
}

WebApplication app = builder.Build();
AppComposition.MapRoutes(app);
await app.RunAsync();
return 0;

using Ashopper.Infrastructure;
using Ashopper.Infrastructure.Collecting;
using Ashopper.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

// Run-once console app. On Azure it's a Container Apps Job: "migrate" once per deploy, "collect" on a cron.
//
//   collect [--source fake] [--source ceneo] [--backfill-days 180]
//   migrate
var command = args.FirstOrDefault(a => !a.StartsWith("--")) ?? "collect";
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
{
    Args = args.Where(a => a != command).ToArray(),
    ContentRootPath = AppContext.BaseDirectory, // find appsettings*.json regardless of the working directory
});
builder.Services.AddAshopperData(builder.Configuration);
builder.Services.AddAshopperCollector(builder.Configuration);
using var host = builder.Build();

using var cts = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; cts.Cancel(); };

switch (command)
{
    case "migrate":
        await DatabaseMigrator.MigrateAndSeedAsync(host.Services, cts.Token);
        Console.WriteLine("Database migrated and reference data seeded.");
        return 0;

    case "collect":
        var options = new CollectorOptions
        {
            Sources = ReadAll(args, "--source"),
            BackfillDays = int.TryParse(ReadAll(args, "--backfill-days").LastOrDefault(), out var days) ? days : 0,
        };
        await using (var scope = host.Services.CreateAsyncScope())
        {
            var run = await scope.ServiceProvider.GetRequiredService<CollectorService>().RunAsync(options, cts.Token);
            return run.Succeeded ? 0 : 1;
        }

    default:
        Console.Error.WriteLine($"Unknown command '{command}'. Use 'collect' or 'migrate'.");
        return 2;
}

static List<string> ReadAll(string[] args, string name) =>
    args.Select((a, i) => (a, i)).Where(x => x.a == name && x.i + 1 < args.Length).Select(x => args[x.i + 1]).ToList();

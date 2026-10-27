using Microsoft.Extensions.DependencyInjection;

namespace Ashopper.Infrastructure.Collecting;

/// <summary>
/// Hands collection work items to whoever runs them. In-process today; a queue-backed implementation
/// (Azure Storage Queue + parallel job replicas) can replace it without touching the collector logic.
/// </summary>
public interface IWorkDispatcher
{
    Task<WorkItemResult> DispatchAsync(CollectorWorkItem item, CancellationToken ct);
}

public sealed class InProcessWorkDispatcher(IServiceScopeFactory scopes) : IWorkDispatcher
{
    public async Task<WorkItemResult> DispatchAsync(CollectorWorkItem item, CancellationToken ct)
    {
        // A fresh scope per item: its own DbContext, so change tracking doesn't grow across the whole run.
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<CatalogIngestor>().IngestAsync(item, ct);
    }
}

namespace Ashopper.Infrastructure.Collecting;

/// <summary>One collector execution. The latest successful FinishedAt versions all price data (ETags).</summary>
public sealed class CollectorRun
{
    public Guid Id { get; init; } = Guid.CreateVersion7();
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset? FinishedAt { get; set; }
    public required string Sources { get; init; }
    public int WorkItems { get; set; }
    public int FailedWorkItems { get; set; }
    public int Observations { get; set; }
    public bool Succeeded { get; set; }
}

/// <summary>The unit of collection work: one source × market × category. Small enough to queue and run in parallel.</summary>
public sealed record CollectorWorkItem(string SourceKey, string MarketCode, string CategorySlug, int BackfillDays);

public sealed record WorkItemResult(int Products, int Offers, int Observations);

public sealed class CollectorOptions
{
    /// <summary>Source keys to run; empty = all registered sources.</summary>
    public List<string> Sources { get; set; } = [];
    /// <summary>For new offers from sources that support it, also load this many days of past prices.</summary>
    public int BackfillDays { get; set; }
}

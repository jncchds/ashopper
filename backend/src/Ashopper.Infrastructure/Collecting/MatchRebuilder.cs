using Ashopper.Domain.Catalog;
using Ashopper.Domain.Lists;
using Ashopper.Domain.Matching;
using Ashopper.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Ashopper.Infrastructure.Collecting;

/// <summary>
/// Keeps <see cref="RequirementMatch"/> in sync: after a requirement changes, and after the catalog changes.
/// Matching runs in C# over a category's products, which is fine for thousands of products per category;
/// past that, translate constraints to SQL (OPENJSON) or a search index.
/// </summary>
public sealed class MatchRebuilder(AppDbContext db)
{
    public async Task<int> RebuildAsync(Requirement requirement, CancellationToken ct)
    {
        var products = await LoadCandidatesAsync(requirement.CategoryId, ct);
        return await ReplaceAsync([requirement], products, ct);
    }

    public async Task<int> RebuildAllAsync(CancellationToken ct)
    {
        var total = 0;
        var byCategory = (await db.Requirements.AsNoTracking().ToListAsync(ct)).GroupBy(r => r.CategoryId);
        foreach (var group in byCategory)
        {
            var products = await LoadCandidatesAsync(group.Key, ct);
            total += await ReplaceAsync(group.ToList(), products, ct);
        }
        return total;
    }

    /// <summary>Products in the category, without tracking. Pinned products live in the same category.</summary>
    public Task<List<Product>> LoadCandidatesAsync(Guid categoryId, CancellationToken ct) =>
        db.Products.AsNoTracking().Where(p => p.CategoryId == categoryId).ToListAsync(ct);

    private async Task<int> ReplaceAsync(IReadOnlyList<Requirement> requirements, List<Product> products, CancellationToken ct)
    {
        var ids = requirements.Select(r => r.Id).ToList();
        await db.RequirementMatches.Where(m => ids.Contains(m.RequirementId)).ExecuteDeleteAsync(ct);

        var matches = requirements
            .SelectMany(r => RequirementMatcher.FindMatches(r, products)
                .Select(p => new RequirementMatch { RequirementId = r.Id, ProductId = p.Id }))
            .ToList();
        db.RequirementMatches.AddRange(matches);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
        return matches.Count;
    }
}

using Ashopper.Domain.Catalog;
using Ashopper.Domain.Lists;

namespace Ashopper.Domain.Matching;

public static class RequirementMatcher
{
    /// <summary>Products that satisfy the requirement: the pinned one, or all in its category matching every constraint.</summary>
    public static IEnumerable<Product> FindMatches(Requirement requirement, IEnumerable<Product> candidates)
    {
        if (requirement.PinnedProductId is { } pinned)
            return candidates.Where(p => p.Id == pinned);

        return candidates.Where(p =>
            p.CategoryId == requirement.CategoryId && SpecMatcher.Matches(p.Attributes, requirement.Constraints));
    }
}

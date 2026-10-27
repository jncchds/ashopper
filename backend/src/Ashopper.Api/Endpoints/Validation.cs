using Ashopper.Domain.Catalog;
using Ashopper.Domain.Matching;

namespace Ashopper.Api.Endpoints;

internal static class Validation
{
    public static IResult Problem(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    public static IResult? Constraints(IReadOnlyList<SpecConstraint> constraints, Category category)
    {
        if (constraints.Count > 30)
            return Problem("constraints", "At most 30 constraints.");
        var errors = constraints
            .Select((c, i) => (Index: i, Error: c.Validate(category.Attributes)))
            .Where(x => x.Error is not null)
            .ToDictionary(x => $"constraints[{x.Index}]", x => new[] { x.Error! });
        return errors.Count > 0 ? Results.ValidationProblem(errors) : null;
    }

    public static IResult? Text(string field, string? value, int max = 200) =>
        string.IsNullOrWhiteSpace(value) ? Problem(field, "Required.")
        : value.Length > max ? Problem(field, $"At most {max} characters.")
        : null;
}

using System.Globalization;
using Ashopper.Domain.Catalog;

namespace Ashopper.Domain.Matching;

public enum ConstraintOp
{
    /// <summary>Equal to Values[0].</summary>
    Eq,
    /// <summary>Equal to none of Values. A missing attribute counts as "not equal".</summary>
    Neq,
    /// <summary>Numeric ≥ Values[0].</summary>
    Gte,
    /// <summary>Numeric ≤ Values[0].</summary>
    Lte,
    /// <summary>Numeric, Values[0] ≤ x ≤ Values[1].</summary>
    Between,
    /// <summary>Equal to any of Values.</summary>
    In,
    /// <summary>Text contains Values[0], case-insensitive.</summary>
    Contains,
}

/// <summary>One condition on a product attribute, e.g. capacity_gb Gte ["32"].</summary>
public sealed record SpecConstraint(string Key, ConstraintOp Op, IReadOnlyList<string> Values)
{
    /// <summary>Returns an error message when the constraint can't be evaluated against these definitions.</summary>
    public string? Validate(IReadOnlyList<AttributeDefinition> definitions)
    {
        var def = definitions.FirstOrDefault(d => d.Key == Key);
        if (def is null)
            return $"Unknown attribute '{Key}'.";

        var expected = Op switch
        {
            ConstraintOp.Between => 2,
            ConstraintOp.Neq or ConstraintOp.In => -1, // one or more
            _ => 1,
        };
        if (expected == -1 ? Values.Count == 0 : Values.Count != expected)
            return $"'{Key}' {Op} needs {(expected == -1 ? "at least one value" : $"{expected} value(s)")}.";

        var numericOp = Op is ConstraintOp.Gte or ConstraintOp.Lte or ConstraintOp.Between;
        if (numericOp && def.Type != AttributeType.Number)
            return $"'{Key}' is not numeric, {Op} can't be used.";
        if (def.Type == AttributeType.Number && Op != ConstraintOp.Contains
            && Values.Any(v => !SpecMatcher.TryParseNumber(v, out _)))
            return $"'{Key}' needs numeric values.";
        if (Op == ConstraintOp.Contains && def.Type != AttributeType.Text)
            return $"Contains works only on text attributes, '{Key}' is {def.Type}.";

        return null;
    }
}

/// <summary>Evaluates constraints against product attributes. Pure and allocation-light.</summary>
public static class SpecMatcher
{
    public static bool Matches(IReadOnlyDictionary<string, string> attributes, IEnumerable<SpecConstraint> constraints)
    {
        foreach (var c in constraints)
        {
            if (!Matches(attributes, c))
                return false;
        }
        return true;
    }

    public static bool Matches(IReadOnlyDictionary<string, string> attributes, SpecConstraint c)
    {
        if (!attributes.TryGetValue(c.Key, out var actual))
            return c.Op == ConstraintOp.Neq;
        if (c.Values.Count == 0)
            return false;

        return c.Op switch
        {
            ConstraintOp.Eq => AreEqual(actual, c.Values[0]),
            ConstraintOp.Neq => c.Values.All(v => !AreEqual(actual, v)),
            ConstraintOp.In => c.Values.Any(v => AreEqual(actual, v)),
            ConstraintOp.Gte => Compare(actual, c.Values[0]) >= 0,
            ConstraintOp.Lte => Compare(actual, c.Values[0]) <= 0,
            ConstraintOp.Between => c.Values.Count >= 2
                && Compare(actual, c.Values[0]) >= 0
                && Compare(actual, c.Values[1]) <= 0,
            ConstraintOp.Contains => actual.Contains(c.Values[0].Trim(), StringComparison.OrdinalIgnoreCase),
            _ => false,
        };
    }

    public static bool TryParseNumber(string s, out decimal value) =>
        decimal.TryParse(s.Trim(), NumberStyles.Number, CultureInfo.InvariantCulture, out value);

    private static bool AreEqual(string a, string b) =>
        TryParseNumber(a, out var x) && TryParseNumber(b, out var y)
            ? x == y
            : string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>Numeric comparison; null when either side isn't a number (never matches).</summary>
    private static int? Compare(string actual, string expected) =>
        TryParseNumber(actual, out var x) && TryParseNumber(expected, out var y) ? x.CompareTo(y) : null;
}

using Ashopper.Domain.Catalog;
using Ashopper.Domain.Matching;

namespace Ashopper.Tests.Unit;

public class SpecMatcherTests
{
    private static readonly Dictionary<string, string> Ram = new()
    {
        ["memory_type"] = "DDR5", ["capacity_gb"] = "32", ["speed_mts"] = "6000", ["cas_latency"] = "30", ["rgb"] = "false", ["name"] = "Fury Beast",
    };

    private static SpecConstraint C(string key, ConstraintOp op, params string[] values) => new(key, op, values);

    [Theory]
    [InlineData("capacity_gb", ConstraintOp.Eq, new[] { "32" }, true)]
    [InlineData("capacity_gb", ConstraintOp.Eq, new[] { "32.0" }, true)] // numeric, not string, equality
    [InlineData("memory_type", ConstraintOp.Eq, new[] { "ddr5" }, true)] // case-insensitive
    [InlineData("memory_type", ConstraintOp.Eq, new[] { "DDR4" }, false)]
    [InlineData("speed_mts", ConstraintOp.Gte, new[] { "6000" }, true)]
    [InlineData("speed_mts", ConstraintOp.Gte, new[] { "6400" }, false)]
    [InlineData("cas_latency", ConstraintOp.Lte, new[] { "30" }, true)]
    [InlineData("cas_latency", ConstraintOp.Lte, new[] { "28" }, false)]
    [InlineData("speed_mts", ConstraintOp.Between, new[] { "5600", "6400" }, true)]
    [InlineData("speed_mts", ConstraintOp.Between, new[] { "6200", "6400" }, false)]
    [InlineData("memory_type", ConstraintOp.In, new[] { "DDR4", "DDR5" }, true)]
    [InlineData("memory_type", ConstraintOp.Neq, new[] { "DDR4" }, true)]
    [InlineData("name", ConstraintOp.Contains, new[] { "beast" }, true)]
    [InlineData("memory_type", ConstraintOp.Gte, new[] { "DDR4" }, false)] // non-numeric never matches range ops
    [InlineData("missing_key", ConstraintOp.Eq, new[] { "x" }, false)]
    [InlineData("missing_key", ConstraintOp.Neq, new[] { "x" }, true)]
    public void Matches_single_constraint(string key, ConstraintOp op, string[] values, bool expected) =>
        Assert.Equal(expected, SpecMatcher.Matches(Ram, C(key, op, values)));

    [Fact]
    public void All_constraints_must_match()
    {
        Assert.True(SpecMatcher.Matches(Ram, [C("memory_type", ConstraintOp.Eq, "DDR5"), C("cas_latency", ConstraintOp.Lte, "30")]));
        Assert.False(SpecMatcher.Matches(Ram, [C("memory_type", ConstraintOp.Eq, "DDR5"), C("cas_latency", ConstraintOp.Lte, "28")]));
        Assert.True(SpecMatcher.Matches(Ram, []));
    }

    private static readonly AttributeDefinition[] Defs =
    [
        new("memory_type", "Type", AttributeType.Enum, AllowedValues: ["DDR4", "DDR5"]),
        new("capacity_gb", "Capacity", AttributeType.Number, "GB"),
        new("name", "Name", AttributeType.Text),
    ];

    [Fact]
    public void Validate_accepts_well_formed_constraints()
    {
        Assert.Null(C("capacity_gb", ConstraintOp.Between, "16", "64").Validate(Defs));
        Assert.Null(C("memory_type", ConstraintOp.In, "DDR4", "DDR5").Validate(Defs));
        Assert.Null(C("name", ConstraintOp.Contains, "fury").Validate(Defs));
    }

    [Theory]
    [InlineData("nope", ConstraintOp.Eq, new[] { "1" })]            // unknown key
    [InlineData("capacity_gb", ConstraintOp.Between, new[] { "1" })] // wrong arity
    [InlineData("capacity_gb", ConstraintOp.Gte, new[] { "lots" })]  // not a number
    [InlineData("memory_type", ConstraintOp.Gte, new[] { "4" })]     // range op on enum
    [InlineData("memory_type", ConstraintOp.Contains, new[] { "D" })] // contains on non-text
    [InlineData("memory_type", ConstraintOp.In, new string[0])]       // no values
    public void Validate_rejects_bad_constraints(string key, ConstraintOp op, string[] values) =>
        Assert.NotNull(C(key, op, values).Validate(Defs));
}

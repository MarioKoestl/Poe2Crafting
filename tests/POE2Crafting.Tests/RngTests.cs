using POE2Crafting.Core.Engine;
using Xunit;

namespace POE2Crafting.Tests;

public class RngTests
{
    [Fact]
    public void Seeded_rng_is_deterministic()
    {
        var rng1 = new Rng(42);
        var rng2 = new Rng(42);
        for (int i = 0; i < 100; i++)
            Assert.Equal(rng1.NextDouble(), rng2.NextDouble());
    }

    [Fact]
    public void PickWeighted_respects_weights()
    {
        var rng = new Rng(123);
        var weights = new double[] { 100, 0, 0 };
        for (int i = 0; i < 50; i++)
            Assert.Equal(0, rng.PickWeighted(weights));
    }

    [Fact]
    public void RollRange_integer_range_returns_integer()
    {
        var rng = new Rng(1);
        for (int i = 0; i < 50; i++)
        {
            var v = rng.RollRange(10, 20);
            Assert.Equal(Math.Floor(v), v);
            Assert.InRange(v, 10, 20);
        }
    }

    [Fact]
    public void RollRange_decimal_range_returns_decimal()
    {
        var rng = new Rng(1);
        var v = rng.RollRange(1.5, 3.5);
        Assert.InRange(v, 1.5, 3.5);
    }
}

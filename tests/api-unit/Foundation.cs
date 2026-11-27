using FragSplit;
using Xunit;

public class Foundation
{
    [Fact]
    public void SupportedSizesMatchDecantMenu() =>
        Assert.Equal(new[] { 3, 5, 10, 15, 30 }, Seed.Sizes);

    [Fact]
    public void ThreeDiscoveryCategories() => Assert.Equal(3, Seed.Categories.Distinct().Count());

    [Fact]
    public void SizesDrawFromSameLiquid() =>
        Assert.Equal(20, Checkout.RequiredMl([new(1, 5, 2), new(1, 10, 1)]));
}

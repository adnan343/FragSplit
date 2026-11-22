using FragSplit;
using Xunit;

public class Foundation
{
    [Fact]
    public void SupportedSizesMatchDecantMenu() =>
        Assert.Equal(new[] { 3, 5, 10, 15, 30 }, Seed.Sizes);

    [Fact]
    public void ThreeDiscoveryCategories() => Assert.Equal(3, Seed.Categories.Distinct().Count());
}

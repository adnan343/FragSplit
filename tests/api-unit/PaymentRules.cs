using FragSplit;
using Xunit;

public class PaymentRules
{
    static readonly GatewayResult Valid = new(
        "VALID",
        "FS-test",
        "abc",
        300m,
        "BDT",
        "BDT",
        300m,
        "0",
        "",
        ""
    );

    [Fact]
    public void BothProviderSuccessStatesCanBeVerified()
    {
        Assert.True(Settlement.Matches("FS-test", "abc", 300, Valid));
        Assert.True(Settlement.Matches("FS-test", "abc", 300, Valid with { Status = "VALIDATED" }));
    }

    [Fact]
    public void IdentityCurrencyAmountAndStatusMustMatch()
    {
        foreach (
            var bad in new[]
            {
                Valid with
                {
                    ApiConnect = "FAILED",
                },
                Valid with
                {
                    Amount = 250,
                },
                Valid with
                {
                    Reference = "other",
                },
                Valid with
                {
                    ValidationId = "other",
                },
                Valid with
                {
                    Currency = "USD",
                },
                Valid with
                {
                    OriginalAmount = 299,
                },
                Valid with
                {
                    Status = "FAILED",
                },
            }
        )
            Assert.False(Settlement.Matches("FS-test", "abc", 300, bad));
    }

    [Fact]
    public void SizesDrawFromSameLiquid() =>
        Assert.Equal(20, Checkout.RequiredMl([new(1, 5, 2), new(1, 10, 1)]));
}

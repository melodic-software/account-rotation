using AccountRotation.Core.Identity;

namespace AccountRotation.Core.Tests.Identity;

public sealed class AccountEmailTests
{
    [Fact]
    public void ParseLowercasesAndTrims()
    {
        AccountEmail email = AccountEmail.Parse("  Dev.Name@Example.COM ").Value;

        email.Value.ShouldBe("dev.name@example.com");
    }

    [Theory]
    [InlineData("dev.example.com", "exactly one @")]
    [InlineData("dev@@example.com", "exactly one @")]
    [InlineData("dev name@example.com", "whitespace")]
    [InlineData("dev/name@example.com", "path separator")]
    [InlineData("dev\\name@example.com", "path separator")]
    [InlineData("devname@example.com", "control character")]
    [InlineData("a@", "3 to 254")]
    public void ParseRefusesMalformedValues(string value, string expectedReasonFragment)
    {
        Result<AccountEmail, string> result = AccountEmail.Parse(value);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain(expectedReasonFragment);
    }

    [Fact]
    public void ParseRefusesAValueLongerThan254Characters()
    {
        string local = new('a', 250);

        AccountEmail.Parse(local + "@x.io").IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void EqualityIsOrdinalOnTheNormalizedValue()
    {
        AccountEmail first = AccountEmail.Parse("Dev@Example.com").Value;
        AccountEmail second = AccountEmail.Parse("dev@example.com").Value;

        first.ShouldBe(second);
    }
}

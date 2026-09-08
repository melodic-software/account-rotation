using ClaudeCodeAccountRotation.Core.Identity;

namespace ClaudeCodeAccountRotation.Core.Tests.Identity;

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

    [Theory]
    [InlineData("a&whoami&b@x.com")]
    [InlineData("a|b@x.com")]
    [InlineData("a>b@x.com")]
    [InlineData("a<b@x.com")]
    [InlineData("a^b@x.com")]
    [InlineData("a%PATH%b@x.com")]
    [InlineData("a\"b@x.com")]
    [InlineData("a:b@x.com")]
    [InlineData("a*b@x.com")]
    [InlineData("a?b@x.com")]
    [InlineData("a'b@x.com")]
    [InlineData("a!b@x.com")]
    public void ParseRefusesEveryCharacterOutsideTheAllowlist(string value)
    {
        // Each of these is legal RFC 5322 atext and each is a command-interpreter
        // metacharacter, a forbidden file-name character, or both. The address
        // reaches both of those, so the boundary admits neither.
        Result<AccountEmail, string> result = AccountEmail.Parse(value);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("letters, digits");
    }

    [Theory]
    [InlineData("user@example.com.")]
    [InlineData(".user@example.com")]
    public void ParseRefusesAnAddressWithADotAtEitherEnd(string value)
    {
        // The profile folder name trims a trailing dot, so this address and the one
        // without it would share a folder, and with it a refresh token.
        Result<AccountEmail, string> result = AccountEmail.Parse(value);

        result.IsFailure.ShouldBeTrue();
        result.Error.ShouldContain("dot");
    }

    [Theory]
    [InlineData("dev.name+tag@example.com")]
    [InlineData("dev_name-2@sub.example.co.uk")]
    public void ParseAcceptsAnOrdinaryAddress(string value) =>
        AccountEmail.Parse(value).IsSuccess.ShouldBeTrue();

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

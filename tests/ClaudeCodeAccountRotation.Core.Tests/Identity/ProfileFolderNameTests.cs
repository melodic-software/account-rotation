using ClaudeCodeAccountRotation.Core.Identity;

namespace ClaudeCodeAccountRotation.Core.Tests.Identity;

public sealed class ProfileFolderNameTests
{
    [Fact]
    public void ReplacesEveryForbiddenFileNameCharacterWithUnderscore()
    {
        AccountEmail email = new("a<b>c:d\"e/f\\g|h?i*j@example.com");

        ProfileFolderName.FromEmail(email).ShouldBe("a_b_c_d_e_f_g_h_i_j@example.com");
    }

    [Fact]
    public void ReplacesControlCharactersWithUnderscore()
    {
        AccountEmail email = new("ab\tc\nde@example.com");

        ProfileFolderName.FromEmail(email).ShouldBe("a_b_c_d_e@example.com");
    }

    [Fact]
    public void TrimsTrailingDotsAndSpaces()
    {
        AccountEmail email = new("dev@example.com. . ");

        ProfileFolderName.FromEmail(email).ShouldBe("dev@example.com");
    }

    [Fact]
    public void LowercasesTheWholeName()
    {
        AccountEmail email = new("Dev.Name@Example.COM");

        ProfileFolderName.FromEmail(email).ShouldBe("dev.name@example.com");
    }

    [Theory]
    [InlineData("")]
    [InlineData("...")]
    [InlineData("   ")]
    public void FallsBackToUnknownWhenNothingRemains(string value)
    {
        AccountEmail email = new(value);

        ProfileFolderName.FromEmail(email).ShouldBe("unknown");
    }

    [Fact]
    public void LeavesAnOrdinaryLowercaseEmailUnchanged()
    {
        AccountEmail email = new("dev.name+tag@example.com");

        ProfileFolderName.FromEmail(email).ShouldBe("dev.name+tag@example.com");
    }
}

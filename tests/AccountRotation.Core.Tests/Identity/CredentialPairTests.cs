using System.Text.Json.Nodes;
using AccountRotation.Core.Identity;

namespace AccountRotation.Core.Tests.Identity;

public sealed class CredentialPairTests
{
    private static JsonObject FileShape(long expiresAt = 1_800_000_000_000, long? refreshTokenExpiresAt = 1_802_000_000_000) =>
        new()
        {
            ["claudeAiOauth"] = new JsonObject
            {
                ["accessToken"] = "access-abc",
                ["refreshToken"] = "abc",
                ["expiresAt"] = expiresAt,
                ["refreshTokenExpiresAt"] = refreshTokenExpiresAt,
                ["scopes"] = new JsonArray("user:inference", "user:profile"),
                ["subscriptionType"] = "max",
            },
        };

    [Fact]
    public void FromJsonReadsTheKnownFieldsAndKeepsTheRawObject()
    {
        JsonObject raw = FileShape();

        CredentialPair pair = CredentialPair.FromJson(raw).Value;

        pair.AccessToken.ShouldBe("access-abc");
        pair.RefreshToken.ShouldBe("abc");
        pair.AccessTokenExpiresAt.ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(1_800_000_000_000));
        pair.LoginExpiresAt.ShouldBe(DateTimeOffset.FromUnixTimeMilliseconds(1_802_000_000_000));
        pair.Scopes.ShouldBe(["user:inference", "user:profile"]);
        pair.Raw.ShouldBeSameAs(raw);
    }

    [Fact]
    public void FingerprintIsTheSha256OfTheRefreshToken()
    {
        CredentialPair pair = CredentialPair.FromJson(FileShape()).Value;

        pair.Fingerprint.Sha256Hex.ShouldBe("ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad");
    }

    [Fact]
    public void LoginExpiryIsOptional()
    {
        CredentialPair pair = CredentialPair.FromJson(FileShape(refreshTokenExpiresAt: null)).Value;

        pair.LoginExpiresAt.ShouldBeNull();
    }

    [Fact]
    public void FromJsonRefusesAFileWithoutTheOauthObject()
    {
        CredentialPair.FromJson(new JsonObject()).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void FromJsonRefusesAPairWithoutARefreshToken()
    {
        JsonObject raw = FileShape();
        raw["claudeAiOauth"]!["refreshToken"] = "";

        CredentialPair.FromJson(raw).IsFailure.ShouldBeTrue();
    }

    [Fact]
    public void NoRenderingOfThePairContainsAToken()
    {
        CredentialPair pair = CredentialPair.FromJson(FileShape()).Value;

        string rendered = pair.ToString();

        rendered.ShouldNotContain("access-abc");
        rendered.ShouldNotContain("abc");
        rendered.ShouldContain(pair.Fingerprint.Sha256Hex[..12]);
    }
}

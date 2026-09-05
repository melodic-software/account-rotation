using AccountRotation.App;
using AccountRotation.Core.Switching;

namespace AccountRotation.App.Tests;

public sealed class ManagedLoginPolicyReaderTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "account-rotation-tests", Guid.NewGuid().ToString("N"));
    private readonly string _managedSettingsPath;

    public ManagedLoginPolicyReaderTests()
    {
        Directory.CreateDirectory(_directory);
        _managedSettingsPath = Path.Combine(_directory, "managed-settings.json");
    }

    private ManagedLoginPolicyReader Reader(string? machinePolicy = null, string? userPolicy = null) =>
        new(_managedSettingsPath, () => machinePolicy, () => userPolicy);

    [Fact]
    public async Task NoSourceYieldsNoPolicy()
    {
        ManagedLoginPolicy policy = await Reader().ReadAsync(TestContext.Current.CancellationToken);

        policy.ForceLoginOrgUuid.ShouldBeNull();
        policy.BlocksSwitching.ShouldBeFalse();
    }

    [Fact]
    public async Task TheManagedSettingsFileSetsTheOrganization()
    {
        await File.WriteAllTextAsync(_managedSettingsPath, """{"forceLoginOrgUUID":"org-file","forceLoginMethod":"claudeai"}""", TestContext.Current.CancellationToken);

        ManagedLoginPolicy policy = await Reader().ReadAsync(TestContext.Current.CancellationToken);

        policy.ForceLoginOrgUuid.ShouldBe("org-file");
        policy.Source.ShouldContain(_managedSettingsPath);
        policy.BlocksSwitching.ShouldBeTrue();
    }

    [Fact]
    public async Task TheMachineRegistryOutranksTheFile()
    {
        await File.WriteAllTextAsync(_managedSettingsPath, """{"forceLoginOrgUUID":"org-file"}""", TestContext.Current.CancellationToken);

        ManagedLoginPolicy policy = await Reader(machinePolicy: """{"forceLoginOrgUUID":"org-registry"}""").ReadAsync(TestContext.Current.CancellationToken);

        policy.ForceLoginOrgUuid.ShouldBe("org-registry");
        policy.Source.ShouldContain("HKLM");
    }

    [Fact]
    public async Task AHigherRankedSourceWithoutTheKeyHidesALowerOne()
    {
        await File.WriteAllTextAsync(_managedSettingsPath, """{"forceLoginOrgUUID":"org-file"}""", TestContext.Current.CancellationToken);

        ManagedLoginPolicy policy = await Reader(machinePolicy: """{"permissions":{}}""").ReadAsync(TestContext.Current.CancellationToken);

        policy.ForceLoginOrgUuid.ShouldBeNull();
        policy.Source.ShouldContain("HKLM");
    }

    [Fact]
    public async Task TheUserRegistryCountsOnlyWhenNothingAboveItExists()
    {
        ManagedLoginPolicy alone = await Reader(userPolicy: """{"forceLoginOrgUUID":"org-user"}""").ReadAsync(TestContext.Current.CancellationToken);
        alone.ForceLoginOrgUuid.ShouldBe("org-user");

        await File.WriteAllTextAsync(_managedSettingsPath, "{}", TestContext.Current.CancellationToken);
        ManagedLoginPolicy shadowed = await Reader(userPolicy: """{"forceLoginOrgUUID":"org-user"}""").ReadAsync(TestContext.Current.CancellationToken);
        shadowed.ForceLoginOrgUuid.ShouldBeNull();
    }

    [Fact]
    public async Task AnUnreadableSourceIsReportedNotTrusted()
    {
        await File.WriteAllTextAsync(_managedSettingsPath, "not json", TestContext.Current.CancellationToken);

        ManagedLoginPolicy policy = await Reader().ReadAsync(TestContext.Current.CancellationToken);

        policy.ForceLoginOrgUuid.ShouldBeNull();
        policy.Source.ShouldContain("unreadable");
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

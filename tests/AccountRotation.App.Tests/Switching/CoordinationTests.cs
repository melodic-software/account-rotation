using AccountRotation.App.Switching;
using AccountRotation.Core;
using AccountRotation.Core.Identity;

namespace AccountRotation.App.Tests.Switching;

public sealed class CoordinationTests : IDisposable
{
    private readonly string _appData = Path.Combine(Path.GetTempPath(), "account-rotation-tests", Guid.NewGuid().ToString("N"));

    public CoordinationTests() => Directory.CreateDirectory(_appData);

    [Fact]
    public async Task TheMutationGateAdmitsOneHolderAtATime()
    {
        using CredentialMutationGate gate = new();
        using (await gate.AcquireAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken))
        {
            await Should.ThrowAsync<TimeoutException>(() => gate.AcquireAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken));
        }

        using IDisposable second = await gate.AcquireAsync(TimeSpan.FromMilliseconds(100), TestContext.Current.CancellationToken);
    }

    [Fact]
    public void TheInstanceLockRefusesASecondInstanceAndNamesTheRunningUrl()
    {
        Result<InstanceLock, string> first = InstanceLock.TryAcquire(_appData, "http://127.0.0.1:48211");
        first.IsSuccess.ShouldBeTrue();

        using (first.Value)
        {
            Result<InstanceLock, string> second = InstanceLock.TryAcquire(_appData, "http://127.0.0.1:48212");
            second.IsFailure.ShouldBeTrue();
            second.Error.ShouldContain("http://127.0.0.1:48211");
        }

        Result<InstanceLock, string> afterRelease = InstanceLock.TryAcquire(_appData, "http://127.0.0.1:48213");
        afterRelease.IsSuccess.ShouldBeTrue();
        afterRelease.Value.Dispose();
    }

    [Fact]
    public async Task TheJournalRoundTripsAnOpenEntryAndClears()
    {
        SwitchJournal journal = new(_appData);
        (await journal.ReadOpenAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
        SwitchJournalEntry entry = new(
            Outgoing: AccountEmail.Parse("a@example.com").Value,
            OutgoingFingerprint: new RefreshTokenFingerprint("aa"),
            OutgoingFolderPath: Path.Combine(_appData, "a"),
            Incoming: AccountEmail.Parse("b@example.com").Value,
            IncomingFingerprint: new RefreshTokenFingerprint("bb"),
            IncomingFolderPath: Path.Combine(_appData, "b"),
            StepReached: SwitchStep.Parked,
            StartedAt: new DateTimeOffset(2026, 9, 5, 12, 0, 0, TimeSpan.Zero));

        await journal.WriteAsync(entry, TestContext.Current.CancellationToken);
        SwitchJournalEntry? read = await journal.ReadOpenAsync(TestContext.Current.CancellationToken);

        read.ShouldBe(entry);

        await journal.ClearAsync(TestContext.Current.CancellationToken);
        (await journal.ReadOpenAsync(TestContext.Current.CancellationToken)).ShouldBeNull();
    }

    [Fact]
    public async Task APermitReleasedAfterTheGateIsDisposedDoesNotThrow()
    {
        // At shutdown the container disposes the gate while a request aborted past the
        // shutdown timeout may still be releasing its permit from a finally block.
        CredentialMutationGate gate = new();
        IDisposable permit = await gate.AcquireAsync(TimeSpan.FromSeconds(1), TestContext.Current.CancellationToken);
        gate.Dispose();

        Should.NotThrow(permit.Dispose);
    }

    public void Dispose()
    {
        if (Directory.Exists(_appData))
        {
            Directory.Delete(_appData, recursive: true);
        }
    }
}

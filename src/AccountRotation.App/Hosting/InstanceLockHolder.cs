using AccountRotation.App.Switching;
using Microsoft.Extensions.Hosting;

namespace AccountRotation.App.Hosting;

/// <summary>
/// Holds the single-instance lock for the host's lifetime and releases it at
/// shutdown. Resolving it here is what makes the container create, and later
/// dispose, the registered lock; nothing else asks for it.
/// </summary>
internal sealed class InstanceLockHolder(InstanceLock instanceLock) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

    public Task StopAsync(CancellationToken cancellationToken)
    {
        instanceLock.Dispose();
        return Task.CompletedTask;
    }
}

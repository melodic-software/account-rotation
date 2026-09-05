namespace AccountRotation.App.Switching;

/// <summary>
/// The one in-process gate every credential-touching operation (switch,
/// refresh write-back, login completion, remove) acquires with a timeout and
/// releases in <c>finally</c>. A second concurrent mutation is refused, not
/// queued behind a moved file.
/// </summary>
internal sealed class CredentialMutationGate : IDisposable
{
    private readonly SemaphoreSlim _permit = new(1, 1);

    public async Task<IDisposable> AcquireAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        if (!await _permit.WaitAsync(timeout, cancellationToken))
        {
            throw new TimeoutException("another credential mutation is in progress; try again in a moment");
        }

        return new Permit(_permit);
    }

    public void Dispose() => _permit.Dispose();

    private sealed class Permit(SemaphoreSlim permit) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (!_released)
            {
                _released = true;
                permit.Release();
            }
        }
    }
}

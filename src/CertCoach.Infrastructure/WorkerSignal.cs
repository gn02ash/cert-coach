namespace CertCoach.Infrastructure;

/// <summary>Wakes a background worker as soon as there is work, instead of on its next poll.</summary>
internal abstract class WorkerSignal : IDisposable
{
    private readonly SemaphoreSlim _signal = new(0, 1);

    public void Notify()
    {
        // Several requests in a row need only one wake-up; the worker drains its whole queue.
        if (_signal.CurrentCount == 0)
        {
            try
            {
                _signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // Another request released it first.
            }
        }
    }

    public Task WaitAsync(TimeSpan timeout, CancellationToken ct) => _signal.WaitAsync(timeout, ct);

    public void Dispose() => _signal.Dispose();
}

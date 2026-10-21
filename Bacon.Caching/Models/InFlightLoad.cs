namespace Bacon.Caching.Models;

internal sealed class InFlightLoad<TItem>(LoadScope scope)
{
    public LoadScope Scope { get; } = scope;

    public TaskCompletionSource<TItem?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
}
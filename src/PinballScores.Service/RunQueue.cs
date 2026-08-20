using System.Threading.Channels;

namespace PinballScores.Service;

public enum RunTrigger
{
    Startup,
    Scheduled,
    FileChanged,
}

/// <summary>
/// Serialises run requests from the timer and the file watcher into a single
/// consumer, so a scheduled run and a file change can never overlap and corrupt
/// each other's write-back. A run already queued absorbs further requests rather
/// than stacking them up.
/// </summary>
public sealed class RunQueue
{
    private readonly Channel<RunTrigger> _channel =
        Channel.CreateBounded<RunTrigger>(new BoundedChannelOptions(1)
        {
            FullMode = BoundedChannelFullMode.DropWrite,
            SingleReader = true,
        });

    /// <summary>Requests a run. Returns false when one is already pending.</summary>
    public bool Request(RunTrigger trigger) => _channel.Writer.TryWrite(trigger);

    public IAsyncEnumerable<RunTrigger> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);

    public void Complete() => _channel.Writer.TryComplete();
}

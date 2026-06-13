using System.Threading.Channels;

namespace Linkbelli.Application.Enrichment;

/// <summary>In-process queue of link ids awaiting metadata enrichment.</summary>
public interface ILinkEnrichmentQueue
{
    void Enqueue(Guid linkId);
    IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken);
}

public sealed class ChannelLinkEnrichmentQueue : ILinkEnrichmentQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true });

    public void Enqueue(Guid linkId) => _channel.Writer.TryWrite(linkId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken cancellationToken) =>
        _channel.Reader.ReadAllAsync(cancellationToken);
}

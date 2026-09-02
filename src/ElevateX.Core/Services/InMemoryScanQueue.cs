using System.Threading.Channels;

namespace ElevateX.Core.Services;

public class InMemoryScanQueue : IScanQueue
{
    private readonly Channel<Guid> _channel;

    public InMemoryScanQueue(int capacity = 500)
    {
        var options = new BoundedChannelOptions(capacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleWriter = false,
            SingleReader = true
        };
        _channel = Channel.CreateBounded<Guid>(options);
    }

    public ValueTask EnqueueAsync(Guid fileAnalysisId, CancellationToken cancellationToken = default)
    {
        return _channel.Writer.WriteAsync(fileAnalysisId, cancellationToken);
    }

    public ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken = default)
    {
        return _channel.Reader.ReadAsync(cancellationToken);
    }
}

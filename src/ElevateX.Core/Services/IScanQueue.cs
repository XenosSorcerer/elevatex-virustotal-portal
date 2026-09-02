namespace ElevateX.Core.Services;

public interface IScanQueue
{
    ValueTask EnqueueAsync(Guid fileAnalysisId, CancellationToken cancellationToken = default);
    ValueTask<Guid> DequeueAsync(CancellationToken cancellationToken = default);
}

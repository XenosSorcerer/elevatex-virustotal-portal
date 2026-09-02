using ElevateX.Core.Entities;

namespace ElevateX.Core.Services;

public interface IScanPipelineService
{
    Task ProcessScanAsync(Guid fileAnalysisId, CancellationToken cancellationToken = default);
}

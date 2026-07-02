using Opera2Oris.Entities;

namespace Opera2Oris.Domain;

public interface IBofExportWorker
{
    Task<BofExportBatch> ReadAsync(BofExportImportOptions options, CancellationToken cancellationToken = default);
}

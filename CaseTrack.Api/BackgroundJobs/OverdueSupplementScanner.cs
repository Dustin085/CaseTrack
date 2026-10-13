using CaseTrack.Application.Abstractions;
using CaseTrack.Application.Cases;
using CaseTrack.Application.Exceptions;
using Microsoft.Extensions.Options;

namespace CaseTrack.Api.BackgroundJobs;

public sealed class OverdueSupplementScanner : BackgroundService
{
    private readonly IServiceScopeFactory _serviceScopeFactory;
    private readonly IOptions<OverdueScanOptions> _options;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<OverdueSupplementScanner> _logger;

    public OverdueSupplementScanner(
        IServiceScopeFactory serviceScopeFactory,
        IOptions<OverdueScanOptions> options,
        TimeProvider timeProvider,
        ILogger<OverdueSupplementScanner> logger)
    {
        _serviceScopeFactory = serviceScopeFactory;
        _options = options;
        _timeProvider = timeProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Value.Enabled)
        {
            _logger.LogInformation("OverdueSupplementScanner is not enabled.");
            return;
        }

        using var periodicTimer = new PeriodicTimer(_options.Value.Interval, _timeProvider);

        while (await periodicTimer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await ScanOnceAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "OverdueSupplementScanner throws unexpected exception.");
            }
        }
    }

    public async Task<int> ScanOnceAsync(CancellationToken cancellationToken)
    {
        var rejectedCount = 0;
        var now = _timeProvider.GetUtcNow();
        IReadOnlyList<Guid> ids;
        await using (var readIdsScope = _serviceScopeFactory.CreateAsyncScope())
        {
            ids = await readIdsScope.ServiceProvider.GetRequiredService<ICaseRepository>()
                 .GetOverdueSupplementCaseIdsAsync(now, _options.Value.BatchSize, cancellationToken);
        }
        foreach (var id in ids)
        {
            try
            {
                await using var handlerScope = _serviceScopeFactory.CreateAsyncScope();
                var isRejected = await handlerScope.ServiceProvider.GetRequiredService<RejectOverdueCaseHandler>()
                    .HandleAsync(id, cancellationToken);
                if (isRejected)
                {
                    rejectedCount++;
                }
            }
            catch (ConcurrencyConflictException)
            {
                // 承辦人或民眾剛好同時修改了這個案件，下次掃描會重新判斷
                _logger.LogInformation("Overdue case {CaseId} was modified by someone else, skipped.", id);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "Error occurred when try to reject overdue case: {CaseId}", id);
            }
        }
        if (ids.Count > 0)
        {
            _logger.LogInformation("Overdue cases scan completed: found {Found} case(s), rejected {Rejected} case(s).",
                ids.Count,
                rejectedCount);
        }
        return rejectedCount;
    }
}

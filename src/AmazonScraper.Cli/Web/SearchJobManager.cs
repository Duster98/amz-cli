using System.Collections.Concurrent;
using AmazonScraper.Cli.History;
using AmazonScraper.Cli.Monitoring;
using AmazonScraper.Cli.Services;
using AmazonScraper.Cli.Web.Monitoring;
using AmazonScraper.Core.Http;
using AmazonScraper.Core.Marketplaces;
using AmazonScraper.Core.Monitoring;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AmazonScraper.Cli.Web;

public sealed class SearchJobSnapshot
{
    public string Id { get; init; } = string.Empty;

    public string State { get; init; } = "queued";

    public ProductSearchProgress? Progress { get; init; }

    public ProductSearchResult? Result { get; init; }

    public string? ErrorCode { get; init; }

    public string? ErrorMessage { get; init; }

    public DateTimeOffset CreatedAt { get; init; }

    public DateTimeOffset? StartedAt { get; init; }

    public DateTimeOffset? CompletedAt { get; init; }
}

public sealed class SearchJobManager : IDisposable
{
    private readonly ConcurrentDictionary<string, SearchJob> _jobs = [];
    private readonly SemaphoreSlim _searchGate = new(1, 1);
    private readonly ILogger _logger;
    private readonly IHostApplicationLifetime _lifetime;
    private readonly HistoryStore? _historyStore;
    private readonly HistoryCacheStore? _historyCache;
    private bool _disposed;

    public SearchJobManager(
        ILogger logger,
        IHostApplicationLifetime lifetime,
        HistoryStore? historyStore = null,
        HistoryCacheStore? historyCache = null)
    {
        _logger = logger;
        _lifetime = lifetime;
        _historyStore = historyStore;
        _historyCache = historyCache;
    }

    public bool TryCreate(
        ProductSearchRequest request,
        HistorySearchInput? history,
        out string jobId,
        out string? historyId,
        out string? error)
    {
        jobId = string.Empty;
        historyId = null;
        error = null;
        Cleanup();
        if (!_searchGate.Wait(0))
        {
            error = "search_busy";
            return false;
        }

        var id = Guid.NewGuid().ToString("N");
        var reuseFailed = false;
        if (history is not null
            && !string.IsNullOrWhiteSpace(history.EntryId)
            && _historyStore is not null)
        {
            try
            {
                var restarted = _historyStore.RestartSearch(
                    history.EntryId,
                    history.Filters);
                if (restarted is not null)
                {
                    historyId = restarted.Id;
                    _historyCache?.Delete(restarted.Id);
                }
            }
            catch (HistoryException ex)
            {
                reuseFailed = true;
                _logger.LogWarning(
                    ex,
                    "Could not restart the search history entry.");
            }
        }
        if (!reuseFailed
            && historyId is null
            && history is not null
            && _historyStore is not null)
        {
            try
            {
                historyId = _historyStore.StartSearch(history).Id;
            }
            catch (HistoryException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not create the search history entry.");
            }
        }
        var job = new SearchJob(
            id,
            request,
            _lifetime.ApplicationStopping,
            historyId);
        _jobs[id] = job;
        jobId = id;
        _ = Task.Run(() => RunAsync(job), CancellationToken.None);
        return true;
    }

    public bool TryGet(string id, out SearchJobSnapshot snapshot)
    {
        Cleanup();
        if (_jobs.TryGetValue(id, out var job))
        {
            snapshot = job.Snapshot();
            return true;
        }

        snapshot = null!;
        return false;
    }

    public bool Cancel(string id)
    {
        if (!_jobs.TryGetValue(id, out var job))
        {
            return false;
        }

        job.RequestCancel();
        return true;
    }

    public bool HasActiveJobs => _jobs.Values.Any(job =>
        job.State is SearchJobState.Queued or SearchJobState.Running);

    public void CancelAll()
    {
        foreach (var job in _jobs.Values)
        {
            job.RequestCancel();
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        foreach (var job in _jobs.Values)
        {
            job.RequestCancel();
        }
        // Do not dispose the semaphore here: a running job may still release
        // it while the host is shutting down. The process is exiting anyway.
    }

    private async Task RunAsync(SearchJob job)
    {
        job.MarkRunning();
        try
        {
            var search = new ProductSearchService();
            var progress = new Progress<ProductSearchProgress>(item =>
                job.SetProgress(item));
            var result = await search
                .SearchAsync(job.Request, progress, job.Token)
                .ConfigureAwait(false);
            CompleteHistory(job, result);
            job.Succeed(result);
        }
        catch (OperationCanceledException) when (job.Token.IsCancellationRequested)
        {
            CompleteHistory(job, null, HistoryStatus.Cancelled, null);
            job.Cancel();
        }
        catch (Exception ex)
        {
            var mapped = WebErrors.Map(ex);
            CompleteHistory(job, null, HistoryStatus.Failed, mapped.Message);
            job.Fail(mapped.Code, mapped.Message);
            _logger.LogWarning(
                ex,
                "Web search {JobId} failed ({Code})",
                job.Id,
                mapped.Code);
        }
        finally
        {
            job.Complete();
            _searchGate.Release();
        }
    }

    private void CompleteHistory(
        SearchJob job,
        ProductSearchResult? result,
        string status = HistoryStatus.Succeeded,
        string? error = null)
    {
        if (job.HistoryId is null
            || _historyStore is null)
        {
            return;
        }

        if (result is not null && _historyCache is not null)
        {
            try
            {
                _historyCache.Save(
                    job.HistoryId,
                    WebHostFactory.SearchResultNode(result));
            }
            catch (HistoryException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not cache search history results {HistoryId}.",
                    job.HistoryId);
            }
        }

        try
        {
            _historyStore.CompleteSearch(
                job.HistoryId,
                status,
                result?.Results.Count,
                error);
        }
        catch (HistoryException ex)
        {
            _logger.LogWarning(
                ex,
                "Could not complete search history entry {HistoryId}.",
                job.HistoryId);
        }

        if (_historyCache is not null)
        {
            try
            {
                // If the entry was removed or cleared while the search was
                // running, do not leave an orphan cache file behind.
                if (!_historyStore.Contains(job.HistoryId))
                {
                    _historyCache.Delete(job.HistoryId);
                }
            }
            catch (HistoryException ex)
            {
                _logger.LogWarning(
                    ex,
                    "Could not clean up search history cache {HistoryId}.",
                    job.HistoryId);
            }
        }
    }

    private void Cleanup()
    {
        var threshold = DateTimeOffset.UtcNow.AddMinutes(-15);
        foreach (var pair in _jobs)
        {
            if (pair.Value.IsFinished
                && pair.Value.CompletedAt is { } completed
                && completed < threshold)
            {
                _jobs.TryRemove(pair.Key, out _);
            }
        }
    }

    private enum SearchJobState
    {
        Queued,
        Running,
        Succeeded,
        Failed,
        Cancelled,
    }

    private sealed class SearchJob
    {
        private readonly object _sync = new();
        private readonly CancellationTokenSource _cts;
        private readonly CancellationTokenRegistration _stoppingRegistration;
        private ProductSearchProgress? _progress;
        private ProductSearchResult? _result;
        private string? _errorCode;
        private string? _errorMessage;
        private SearchJobState _state = SearchJobState.Queued;
        private DateTimeOffset? _startedAt;
        private DateTimeOffset? _completedAt;

        public SearchJob(
            string id,
            ProductSearchRequest request,
            CancellationToken stopping,
            string? historyId)
        {
            Id = id;
            Request = request;
            HistoryId = historyId;
            CreatedAt = DateTimeOffset.UtcNow;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(stopping);
            _stoppingRegistration = stopping.Register(RequestCancel);
        }

        public string Id { get; }

        public ProductSearchRequest Request { get; }

        public string? HistoryId { get; }

        public DateTimeOffset CreatedAt { get; }

        public CancellationToken Token => _cts.Token;

        public DateTimeOffset? CompletedAt
        {
            get
            {
                lock (_sync)
                {
                    return _completedAt;
                }
            }
        }

        public bool IsFinished
        {
            get
            {
                lock (_sync)
                {
                    return _state is SearchJobState.Succeeded
                        or SearchJobState.Failed
                        or SearchJobState.Cancelled;
                }
            }
        }

        public SearchJobState State
        {
            get
            {
                lock (_sync)
                {
                    return _state;
                }
            }
        }

        public void MarkRunning()
        {
            lock (_sync)
            {
                if (_state == SearchJobState.Queued)
                {
                    _state = SearchJobState.Running;
                    _startedAt = DateTimeOffset.UtcNow;
                }
            }
        }

        public void SetProgress(ProductSearchProgress progress)
        {
            lock (_sync)
            {
                _progress = progress;
            }
        }

        public void Succeed(ProductSearchResult result)
        {
            lock (_sync)
            {
                _result = result;
                _state = SearchJobState.Succeeded;
                _completedAt = DateTimeOffset.UtcNow;
            }
        }

        public void Fail(string code, string message)
        {
            lock (_sync)
            {
                _errorCode = code;
                _errorMessage = message;
                _state = SearchJobState.Failed;
                _completedAt = DateTimeOffset.UtcNow;
            }
        }

        public void RequestCancel()
        {
            lock (_sync)
            {
                if (_state is SearchJobState.Succeeded
                    or SearchJobState.Failed
                    or SearchJobState.Cancelled)
                {
                    return;
                }
                _state = SearchJobState.Cancelled;
                _completedAt ??= DateTimeOffset.UtcNow;
            }
            try
            {
                _cts.Cancel();
            }
            catch (ObjectDisposedException)
            {
                // The job already completed.
            }
        }

        public void Cancel()
        {
            lock (_sync)
            {
                _state = SearchJobState.Cancelled;
                _completedAt ??= DateTimeOffset.UtcNow;
            }
        }

        public void Complete()
        {
            lock (_sync)
            {
                _completedAt ??= DateTimeOffset.UtcNow;
            }
            _stoppingRegistration.Dispose();
            _cts.Dispose();
        }

        public SearchJobSnapshot Snapshot()
        {
            lock (_sync)
            {
                return new SearchJobSnapshot
                {
                    Id = Id,
                    State = _state switch
                    {
                        SearchJobState.Queued => "queued",
                        SearchJobState.Running => "running",
                        SearchJobState.Succeeded => "succeeded",
                        SearchJobState.Failed => "failed",
                        _ => "cancelled",
                    },
                    Progress = _progress,
                    Result = _result,
                    ErrorCode = _errorCode,
                    ErrorMessage = _errorMessage,
                    CreatedAt = CreatedAt,
                    StartedAt = _startedAt,
                    CompletedAt = _completedAt,
                };
            }
        }
    }
}

public static class WebErrors
{
    public static (int Status, string Code, string Message) Map(Exception exception) =>
        exception switch
        {
            MarketplaceNotAvailableException ex =>
                (400, "marketplace_not_available", ex.Message),
            MarketplaceNotFoundException ex =>
                (400, "marketplace_not_found", ex.Message),
            MarketplaceException ex =>
                (400, "marketplace_error", ex.Message),
            AmazonBlockedException ex =>
                (503, "amazon_blocked", ex.Message),
            AmazonNotFoundException ex =>
                (404, "amazon_not_found", ex.Message),
            MonitorNotFoundException ex =>
                (404, "monitor_not_found", ex.Message),
            MonitorException ex =>
                (400, "monitor_error", ex.Message),
            TaskCanceledException =>
                (504, "network_timeout", "Amazon did not respond in time."),
            OperationCanceledException =>
                (499, "cancelled", "Operation cancelled."),
            HttpRequestException ex =>
                (502, "network_error", ex.Message),
            ArgumentException ex =>
                (400, "invalid_request", ex.Message),
            InvalidOperationException ex =>
                (502, "parse_error", ex.Message),
            _ => (500, "internal_error", "Internal web interface error."),
        };
}

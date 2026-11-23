namespace AmazonScraper.Core.Monitoring;

/// <summary>
/// Pure adaptive scheduler. It does not perform HTTP requests; the runner
/// selects a unit, executes it, and reports the real request cost. Keeping the
/// selection logic here makes budget, gap and restart behavior testable without
/// network access.
/// </summary>
public sealed class MonitorScheduler
{
    private readonly MonitorSchedulePlan _plan;
    private readonly MonitorScheduleState _state;
    private readonly int _requestDelaySeconds;
    private readonly string _scheduleHash;
    private readonly Action<string>? _warning;
    private readonly Random _random;
    private readonly double _refillRate;
    private readonly double _capacity;

    public MonitorScheduler(
        MonitorSchedulePlan plan,
        MonitorScheduleState state,
        int requestDelaySeconds,
        string scheduleHash,
        Action<string>? warning = null,
        Random? random = null)
    {
        _plan = plan;
        _state = state;
        _requestDelaySeconds = Math.Max(1, requestDelaySeconds);
        _scheduleHash = scheduleHash;
        _warning = warning;
        _random = random ?? Random.Shared;
        _refillRate = plan.WindowSeconds > 0
            ? plan.MaxRequests / (double)plan.WindowSeconds
            : 0;
        _capacity = Math.Max(1, plan.MaxUnitCost);
    }

    public MonitorSchedulePlan Plan => _plan;

    public MonitorScheduleState State => _state;

    public DateTimeOffset WindowStart =>
        _state.WindowStart ?? DateTimeOffset.UtcNow;

    public DateTimeOffset WindowEnd =>
        _state.WindowEnd ?? DateTimeOffset.UtcNow;

    public long WindowIndex => _state.WindowIndex;

    public void Initialize(DateTimeOffset now)
    {
        _state.Units ??= [];
        var scheduleChanged = !string.Equals(
            _state.ScheduleHash,
            _scheduleHash,
            StringComparison.Ordinal);
        var windowChanged = _state.AnchorAt is null
            || _state.WindowSeconds != _plan.WindowSeconds
            || _state.WindowStart is null
            || _state.WindowEnd is null;

        if (windowChanged)
        {
            if (_state.AnchorAt is not null)
            {
                _warning?.Invoke(
                    "Adaptive window reinitialized after a configuration change.");
            }

            if (_state.AnchorAt is not null
                && _state.WindowSeconds != _plan.WindowSeconds)
            {
                _state.AnchorAt = now;
            }
            ResetWindow(now, index: 0);
        }
        else
        {
            EnsureUnitStates(now);
            if (scheduleChanged)
            {
                ClampNextDueTimes(now);
            }
        }

        if (scheduleChanged)
        {
            _state.ScheduleHash = _scheduleHash;
            _state.Tokens = Math.Min(_state.Tokens, _capacity);
        }

        EnsureWindow(now);
    }

    public MonitorUnitPlan? SelectNext(DateTimeOffset now)
    {
        EnsureWindow(now);
        var candidates = new List<(MonitorUnitPlan Unit, MonitorUnitState State)>();
        foreach (var unit in _plan.Units)
        {
            var unitState = GetUnitState(unit.Id);
            if (unitState.LastSkippedWindow == _state.WindowIndex)
            {
                continue;
            }
            if (unitState.NextDueAt is null || unitState.NextDueAt > now)
            {
                continue;
            }
            if (CanRun(unit, unitState, now))
            {
                candidates.Add((unit, unitState));
            }
        }

        if (candidates.Count == 0)
        {
            return null;
        }

        return candidates
            .OrderBy(candidate => candidate.State.LastRunAt
                ?? DateTimeOffset.MinValue)
            .ThenBy(_ => _random.Next())
            .First()
            .Unit;
    }

    public TimeSpan NextDelay(DateTimeOffset now)
    {
        EnsureWindow(now);
        var windowRemaining = WindowEnd - now;
        if (windowRemaining <= TimeSpan.Zero)
        {
            return TimeSpan.FromSeconds(1);
        }

        var due = new List<(MonitorUnitPlan Unit, MonitorUnitState State)>();
        DateTimeOffset? nextDue = null;
        foreach (var unit in _plan.Units)
        {
            var unitState = GetUnitState(unit.Id);
            if (unitState.LastSkippedWindow == _state.WindowIndex)
            {
                continue;
            }
            if (unitState.NextDueAt is null)
            {
                continue;
            }
            if (unitState.NextDueAt <= now)
            {
                due.Add((unit, unitState));
            }
            else if (nextDue is null || unitState.NextDueAt < nextDue)
            {
                nextDue = unitState.NextDueAt;
            }
        }

        TimeSpan delay;
        if (due.Count == 0)
        {
            delay = nextDue is { } dueAt
                ? dueAt - now
                : windowRemaining;
        }
        else if (due.Any(item => CanRun(item.Unit, item.State, now)))
        {
            return TimeSpan.Zero;
        }
        else
        {
            var affordable = due
                .Where(item => item.State.NextDueAt <= now
                    && _state.SpentRequests + item.Unit.Cost <= _plan.MaxRequests)
                .ToArray();
            if (affordable.Length == 0)
            {
                delay = windowRemaining;
            }
            else
            {
                var cheapest = affordable.Min(item => item.Unit.Cost);
                var tokenWait = _refillRate > 0
                    ? Math.Max(0, (cheapest - _state.Tokens) / _refillRate)
                    : double.MaxValue;
                var fits = affordable.Any(item =>
                    FitsBeforeWindowEnd(item.Unit, now));
                delay = fits
                    ? TimeSpan.FromSeconds(Math.Min(tokenWait, windowRemaining.TotalSeconds))
                    : windowRemaining;
            }
        }

        if (delay < TimeSpan.FromSeconds(1))
        {
            return TimeSpan.FromSeconds(1);
        }

        return delay < windowRemaining ? delay : windowRemaining;
    }

    public void RecordRun(
        MonitorUnitPlan unit,
        DateTimeOffset startedAt,
        DateTimeOffset endedAt,
        int actualCost,
        bool chargeRequests = true)
    {
        var unitState = GetUnitState(unit.Id);
        var cost = Math.Max(0, actualCost);
        if (chargeRequests)
        {
            ChargeRequests(cost);
        }
        unitState.LastRunAt = endedAt;
        unitState.NextDueAt = endedAt.AddSeconds(RandomGap(unit));
        unitState.Runs++;
        unitState.LastCost = cost;
        unitState.Skipped = 0;
        unitState.LastSkippedWindow = null;
    }

    /// <summary>
    /// Charges requests as soon as they are attempted. The adaptive runner uses
    /// this to account for a cycle interrupted by Ctrl+C, where RecordRun would
    /// never be reached.
    /// </summary>
    public void ChargeRequests(int count)
    {
        if (count <= 0)
        {
            return;
        }

        _state.SpentRequests = Math.Min(
            _plan.MaxRequests,
            _state.SpentRequests + count);
        _state.Tokens = Math.Max(0, _state.Tokens - count);
    }

    public void Postpone(
        MonitorUnitPlan unit,
        DateTimeOffset now,
        TimeSpan delay,
        bool skipForWindow = false)
    {
        var unitState = GetUnitState(unit.Id);
        unitState.NextDueAt = now + (delay > TimeSpan.Zero
            ? delay
            : TimeSpan.FromSeconds(1));
        unitState.Skipped++;
        if (skipForWindow)
        {
            unitState.LastSkippedWindow = _state.WindowIndex;
        }
    }

    public void SkipForWindow(MonitorUnitPlan unit, DateTimeOffset now)
    {
        Postpone(
            unit,
            now,
            WindowEnd - now + TimeSpan.FromSeconds(1),
            skipForWindow: true);
    }

    public int GetRuns(string unitId) => GetUnitState(unitId).Runs;

    public TimeSpan EstimateDuration(MonitorUnitPlan unit) =>
        TimeSpan.FromSeconds(
            Math.Max(1, unit.Cost) * _requestDelaySeconds);

    private bool CanRun(
        MonitorUnitPlan unit,
        MonitorUnitState unitState,
        DateTimeOffset now) =>
        _state.Tokens + 0.000001 >= unit.Cost
        && _state.SpentRequests + unit.Cost <= _plan.MaxRequests
        && FitsBeforeWindowEnd(unit, now);

    private bool FitsBeforeWindowEnd(
        MonitorUnitPlan unit,
        DateTimeOffset now) =>
        now + EstimateDuration(unit) <= WindowEnd;

    private void EnsureWindow(DateTimeOffset now)
    {
        if (_state.AnchorAt is null
            || _state.WindowStart is null
            || _state.WindowEnd is null)
        {
            ResetWindow(now, index: 0);
            return;
        }

        var elapsed = now - _state.AnchorAt.Value;
        var index = elapsed <= TimeSpan.Zero
            ? 0
            : (long)Math.Floor(
                elapsed.TotalSeconds / _plan.WindowSeconds);
        if (index < 0)
        {
            index = 0;
        }

        if (index < _state.WindowIndex)
        {
            // A backwards clock jump must not rewind the budget.
            index = _state.WindowIndex;
        }

        if (index > _state.WindowIndex)
        {
            _state.WindowIndex = index;
            _state.WindowStart = _state.AnchorAt.Value.AddSeconds(
                index * _plan.WindowSeconds);
            _state.WindowEnd = _state.WindowStart.Value.AddSeconds(
                _plan.WindowSeconds);
            _state.SpentRequests = 0;
            _state.Tokens = _capacity;
            _state.LastRefillAt = now;
            _warning?.Invoke(
                $"New adaptive window {index}: budget {_plan.MaxRequests} requests.");
        }

        RefillTokens(now);
    }

    private void RefillTokens(DateTimeOffset now)
    {
        var last = _state.LastRefillAt ?? now;
        if (now < last)
        {
            _state.LastRefillAt = now;
            return;
        }

        var elapsed = (now - last).TotalSeconds;
        if (elapsed > 0 && _refillRate > 0)
        {
            _state.Tokens = Math.Min(
                _capacity,
                _state.Tokens + elapsed * _refillRate);
        }
        _state.LastRefillAt = now;
    }

    private void ResetWindow(DateTimeOffset now, long index)
    {
        _state.AnchorAt ??= now;
        _state.WindowSeconds = _plan.WindowSeconds;
        _state.WindowIndex = index;
        _state.WindowStart = _state.AnchorAt.Value.AddSeconds(
            index * _plan.WindowSeconds);
        _state.WindowEnd = _state.WindowStart.Value.AddSeconds(
            _plan.WindowSeconds);
        _state.SpentRequests = 0;
        _state.Tokens = _capacity;
        _state.LastRefillAt = now;
        _state.ScheduleHash = _scheduleHash;
        EnsureUnitStates(now);
    }

    private void EnsureUnitStates(DateTimeOffset now)
    {
        var known = _plan.Units
            .Select(unit => unit.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var stale in _state.Units.Keys
                     .Where(key => !known.Contains(key))
                     .ToArray())
        {
            _state.Units.Remove(stale);
        }

        foreach (var unit in _plan.Units)
        {
            if (!_state.Units.TryGetValue(unit.Id, out var unitState))
            {
                _state.Units[unit.Id] = new MonitorUnitState
                {
                    NextDueAt = now,
                };
                continue;
            }

            unitState.NextDueAt ??= now;
        }
    }

    private void ClampNextDueTimes(DateTimeOffset now)
    {
        foreach (var unit in _plan.Units)
        {
            var unitState = GetUnitState(unit.Id);
            if (unitState.LastRunAt is not { } lastRun)
            {
                unitState.NextDueAt ??= now;
                continue;
            }

            var earliest = lastRun.AddSeconds(unit.MinGapSeconds);
            var latest = lastRun.AddSeconds(unit.MaxGapSeconds);
            var next = unitState.NextDueAt ?? earliest;
            if (next < earliest)
            {
                next = earliest;
            }
            if (next > latest)
            {
                next = latest;
            }
            unitState.NextDueAt = next;
        }
    }

    private MonitorUnitState GetUnitState(string unitId)
    {
        if (!_state.Units.TryGetValue(unitId, out var state))
        {
            state = new MonitorUnitState
            {
                NextDueAt = DateTimeOffset.UtcNow,
            };
            _state.Units[unitId] = state;
        }

        return state;
    }

    private int RandomGap(MonitorUnitPlan unit)
    {
        var minimum = Math.Max(0, unit.MinGapSeconds);
        if (unit.Kind == MonitorUnitKind.Item)
        {
            minimum = Math.Max(minimum, _requestDelaySeconds);
        }
        var maximum = Math.Max(minimum, unit.MaxGapSeconds);
        if (maximum == minimum)
        {
            return minimum;
        }

        return _random.Next(minimum, maximum + 1);
    }
}

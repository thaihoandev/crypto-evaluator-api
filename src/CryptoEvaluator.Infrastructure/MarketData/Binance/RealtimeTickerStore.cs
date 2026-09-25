using System.Collections.Concurrent;

namespace CryptoEvaluator.Infrastructure.MarketData.Binance;

/// <summary>
/// Process-local fallback for ticker snapshots. Redis remains the shared cache,
/// while this store makes a just-received WebSocket event available immediately.
/// </summary>
public sealed class RealtimeTickerStore
{
    /// <summary>
    /// How long a ticker snapshot is kept in the in-memory store.
    /// Set to 70 s to outlast the 60-second Redis write throttle window \u2014
    /// if the WebSocket briefly disconnects and reconnects within this period,
    /// same-process callers still get a valid snapshot without falling back to HTTP.
    /// </summary>
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromMinutes(5);

    private readonly ConcurrentDictionary<string, Entry> _entries =
        new(StringComparer.OrdinalIgnoreCase);

    public void Set(RealtimeTicker ticker) =>
        _entries[ticker.Symbol] = new Entry(ticker, DateTime.UtcNow.Add(EntryLifetime));

    public bool TryGet(string symbol, out RealtimeTicker? ticker)
    {
        ticker = null;
        if (!_entries.TryGetValue(symbol, out var entry)) return false;

        if (entry.ExpiresAt > DateTime.UtcNow)
        {
            ticker = entry.Ticker;
            return true;
        }

        _entries.TryRemove(symbol, out _);
        return false;
    }

    private sealed record Entry(RealtimeTicker Ticker, DateTime ExpiresAt);
}

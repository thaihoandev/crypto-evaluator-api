using System.Collections.Concurrent;

namespace CryptoEvaluator.Infrastructure.MarketData.Binance;

/// <summary>
/// Process-local fallback for ticker snapshots. Redis remains the shared cache,
/// while this store makes a just-received WebSocket event available immediately.
/// </summary>
public sealed class RealtimeTickerStore
{
    private static readonly TimeSpan EntryLifetime = TimeSpan.FromSeconds(10);
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

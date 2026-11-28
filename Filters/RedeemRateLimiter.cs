namespace suara_belajar.Filters
{
    using System.Collections.Concurrent;

    public static class RedeemRateLimiter
    {
        private static readonly ConcurrentDictionary<string, (int Count, DateTime ResetAt)> _store
            = new();

        private const int MAX_ATTEMPT = 5;
        private static readonly TimeSpan WINDOW = TimeSpan.FromMinutes(5);

        public static bool IsLimited(string key)
        {
            var now = DateTime.UtcNow;

            var entry = _store.GetOrAdd(key, _ => (0, now.Add(WINDOW)));

            // Reset window
            if (now > entry.ResetAt)
            {
                _store[key] = (1, now.Add(WINDOW));
                return false;
            }

            // Over limit
            if (entry.Count >= MAX_ATTEMPT)
                return true;

            // Increment
            _store[key] = (entry.Count + 1, entry.ResetAt);
            return false;
        }
    }

}

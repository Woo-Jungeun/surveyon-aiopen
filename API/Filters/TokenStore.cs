using System.Collections.Concurrent;

namespace SurveyonApi.Filters
{
    public record TokenInfo(string UserId, DateTime ExpiresAtUtc);
    public static class TokenStore
    {
        // key: loginkey (token)
        private static readonly ConcurrentDictionary<string, TokenInfo> _tokens = new();

        public static void Add(string token, string userId, TimeSpan ttl)
            => _tokens[token] = new TokenInfo(userId, DateTime.UtcNow.Add(ttl));

        public static bool TryGetValid(string token, out TokenInfo info)
        {
            info = default!;
            if (!_tokens.TryGetValue(token, out var v)) return false;

            if (DateTime.UtcNow >= v.ExpiresAtUtc)
            {
                _tokens.TryRemove(token, out _);
                return false;
            }

            info = v;
            return true;
        }

        public static void Remove(string token) => _tokens.TryRemove(token, out _);
    }
}

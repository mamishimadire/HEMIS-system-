using Microsoft.Extensions.Caching.Memory;

namespace HemisAudit.Services
{
    public interface IPendingValidationCacheService
    {
        void StorePending<TRequest, TSummary>(int ruleNumber, int clientId, string reviewerEmail, TRequest request, TSummary summary, string? reviewerName = null, TimeSpan? lifetime = null)
            where TRequest : class
            where TSummary : class;

        PendingValidationSnapshot<TRequest, TSummary>? GetPending<TRequest, TSummary>(int ruleNumber, int clientId, string reviewerEmail)
            where TRequest : class
            where TSummary : class;

        bool MatchesPending<TRequest>(int ruleNumber, int clientId, string reviewerEmail, TRequest request)
            where TRequest : class;

        bool HasPending(int ruleNumber, int clientId, string reviewerEmail);

        void ClearPending(int ruleNumber, int clientId, string reviewerEmail);
    }

    public sealed class PendingValidationSnapshot<TRequest, TSummary>
        where TRequest : class
        where TSummary : class
    {
        public TRequest Request { get; init; } = default!;
        public TSummary Summary { get; init; } = default!;
        public string? ReviewerName { get; init; }
        public DateTime ValidatedAtUtc { get; init; }
    }

    public class PendingValidationCacheService : IPendingValidationCacheService
    {
        private static readonly TimeSpan DefaultLifetime = TimeSpan.FromHours(4);
        private readonly IMemoryCache _memoryCache;

        public PendingValidationCacheService(IMemoryCache memoryCache)
        {
            _memoryCache = memoryCache;
        }

        public void StorePending<TRequest, TSummary>(int ruleNumber, int clientId, string reviewerEmail, TRequest request, TSummary summary, string? reviewerName = null, TimeSpan? lifetime = null)
            where TRequest : class
            where TSummary : class
        {
            if (ruleNumber <= 0 || clientId <= 0 || string.IsNullOrWhiteSpace(reviewerEmail))
                return;

            _memoryCache.Set(
                BuildKey(ruleNumber, clientId, reviewerEmail),
                new SerializedPendingValidationSnapshot
                {
                    RequestJson = Newtonsoft.Json.JsonConvert.SerializeObject(request),
                    SummaryJson = Newtonsoft.Json.JsonConvert.SerializeObject(summary),
                    ReviewerName = reviewerName,
                    ValidatedAtUtc = DateTime.UtcNow
                },
                new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = lifetime ?? DefaultLifetime,
                    SlidingExpiration = TimeSpan.FromMinutes(30)
                });
        }

        public PendingValidationSnapshot<TRequest, TSummary>? GetPending<TRequest, TSummary>(int ruleNumber, int clientId, string reviewerEmail)
            where TRequest : class
            where TSummary : class
        {
            if (ruleNumber <= 0 || clientId <= 0 || string.IsNullOrWhiteSpace(reviewerEmail))
                return null;

            if (!_memoryCache.TryGetValue(BuildKey(ruleNumber, clientId, reviewerEmail), out SerializedPendingValidationSnapshot? pending) || pending == null)
                return null;

            var request = Newtonsoft.Json.JsonConvert.DeserializeObject<TRequest>(pending.RequestJson);
            var summary = Newtonsoft.Json.JsonConvert.DeserializeObject<TSummary>(pending.SummaryJson);
            if (request == null || summary == null)
                return null;

            return new PendingValidationSnapshot<TRequest, TSummary>
            {
                Request = request,
                Summary = summary,
                ReviewerName = pending.ReviewerName,
                ValidatedAtUtc = pending.ValidatedAtUtc
            };
        }

        public bool MatchesPending<TRequest>(int ruleNumber, int clientId, string reviewerEmail, TRequest request)
            where TRequest : class
        {
            if (!_memoryCache.TryGetValue(BuildKey(ruleNumber, clientId, reviewerEmail), out SerializedPendingValidationSnapshot? pending) || pending == null)
                return false;

            return string.Equals(
                pending.RequestJson,
                Newtonsoft.Json.JsonConvert.SerializeObject(request),
                StringComparison.Ordinal);
        }

        public bool HasPending(int ruleNumber, int clientId, string reviewerEmail)
        {
            if (ruleNumber <= 0 || clientId <= 0 || string.IsNullOrWhiteSpace(reviewerEmail))
                return false;

            return _memoryCache.TryGetValue(BuildKey(ruleNumber, clientId, reviewerEmail), out _);
        }

        public void ClearPending(int ruleNumber, int clientId, string reviewerEmail)
        {
            if (ruleNumber <= 0 || clientId <= 0 || string.IsNullOrWhiteSpace(reviewerEmail))
                return;

            _memoryCache.Remove(BuildKey(ruleNumber, clientId, reviewerEmail));
        }

        private static string BuildKey(int ruleNumber, int clientId, string reviewerEmail)
            => $"PendingValidation:{ruleNumber}:{clientId}:{reviewerEmail.Trim().ToLowerInvariant()}";

        private sealed class SerializedPendingValidationSnapshot
        {
            public string RequestJson { get; init; } = "";
            public string SummaryJson { get; init; } = "";
            public string? ReviewerName { get; init; }
            public DateTime ValidatedAtUtc { get; init; }
        }
    }
}

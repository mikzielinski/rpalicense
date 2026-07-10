using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Ops.License.Api;

public sealed class HandshakeService
{
    private readonly ServerSettings _settings;
    private readonly ConcurrentDictionary<string, ChallengeRecord> _challenges = new();

    public HandshakeService(ServerSettings settings)
    {
        _settings = settings;
    }

    public ChallengeRecord CreateRuntimeChallenge(string machine)
    {
        return CreateChallenge("runtime", machine);
    }

    public ChallengeRecord CreateOperatorChallenge(string operatorId)
    {
        return CreateChallenge("operator", operatorId);
    }

    public bool TryConsumeRuntimeChallenge(string challengeId, out ChallengeRecord record)
    {
        return TryConsume(challengeId, "runtime", out record);
    }

    public bool TryConsumeOperatorChallenge(string challengeId, out ChallengeRecord record)
    {
        return TryConsume(challengeId, "operator", out record);
    }

    private ChallengeRecord CreateChallenge(string kind, string subject)
    {
        PurgeExpired();
        var record = new ChallengeRecord
        {
            ChallengeId = Guid.NewGuid().ToString("N"),
            ServerNonce = Convert.ToBase64String(RandomNumberGenerator.GetBytes(24)),
            ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(_settings.ChallengeTtlSeconds),
            Kind = kind,
            Subject = subject.Trim()
        };
        _challenges[record.ChallengeId] = record;
        return record;
    }

    private bool TryConsume(string challengeId, string kind, out ChallengeRecord record)
    {
        PurgeExpired();
        if (!_challenges.TryRemove(challengeId, out record!))
        {
            return false;
        }

        if (!string.Equals(record.Kind, kind, StringComparison.Ordinal) ||
            record.ExpiresAt <= DateTimeOffset.UtcNow)
        {
            record = null!;
            return false;
        }

        return true;
    }

    private void PurgeExpired()
    {
        var now = DateTimeOffset.UtcNow;
        foreach (var key in _challenges.Keys)
        {
            if (_challenges.TryGetValue(key, out var value) && value.ExpiresAt <= now)
            {
                _challenges.TryRemove(key, out _);
            }
        }
    }
}

public sealed class ChallengeRecord
{
    public string ChallengeId { get; set; } = string.Empty;
    public string ServerNonce { get; set; } = string.Empty;
    public DateTimeOffset ExpiresAt { get; set; }
    public string Kind { get; set; } = string.Empty;
    public string Subject { get; set; } = string.Empty;
}

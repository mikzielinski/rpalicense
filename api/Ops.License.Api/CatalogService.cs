using UiPath.System.RoboticSecurity;

namespace Ops.License.Api;

public sealed class CatalogService
{
    private readonly ILicenseStore _store;
    private readonly ServerSettings _serverSettings;
    private readonly object _gate = new();
    private string? _cachedJwt;
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public CatalogService(ILicenseStore store, ServerSettings serverSettings)
    {
        _store = store;
        _serverSettings = serverSettings;
        ApplyBootstrapperSettings();
    }

    internal async Task<CatalogDocument> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        ApplyBootstrapperSettings();
        var jwt = await GetSeedJwtAsync(cancellationToken).ConfigureAwait(false);
        return LicenseCatalog.ParseCatalog(jwt);
    }

    public async Task<string> GetSeedJwtAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            if (_cachedJwt is not null && DateTimeOffset.UtcNow - _cachedAt < CacheTtl)
            {
                return _cachedJwt;
            }
        }

        var jwt = await _store.GetSeedJwtAsync(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(jwt))
        {
            throw new FileNotFoundException("Catalog seed is not configured in the database.");
        }

        lock (_gate)
        {
            _cachedJwt = jwt.Trim();
            _cachedAt = DateTimeOffset.UtcNow;
            return _cachedJwt;
        }
    }

    public void InvalidateCache()
    {
        lock (_gate)
        {
            _cachedJwt = null;
        }
    }

    public async Task<PublishResult> PublishSeedJwtAsync(string jwt, string message, CancellationToken cancellationToken = default)
    {
        var result = await _store.PublishSeedJwtAsync(jwt, message, cancellationToken).ConfigureAwait(false);
        InvalidateCache();
        return result;
    }

    private void ApplyBootstrapperSettings()
    {
        BootstrapperSettings.Pepper = _serverSettings.Pepper;
        BootstrapperSettings.EnvelopePepper = _serverSettings.EnvelopePepper;
        BootstrapperSettings.EnvelopeSigningKey = _serverSettings.EnvelopeSigningKey;
        BootstrapperSettings.EnvelopeIssuer = _serverSettings.EnvelopeIssuer;
        BootstrapperSettings.EnvelopeAudience = _serverSettings.EnvelopeAudience;
        BootstrapperSettings.PublicSealKeyPem = string.IsNullOrWhiteSpace(_serverSettings.PublicSealKeyPem)
            ? BootstrapperSettings.PublicSealKeyPem
            : _serverSettings.PublicSealKeyPem;
        BootstrapperSettings.SourceUsesJwtEnvelope = true;
    }
}

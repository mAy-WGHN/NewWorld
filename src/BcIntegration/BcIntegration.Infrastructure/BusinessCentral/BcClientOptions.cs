namespace BcIntegration.Infrastructure.BusinessCentral;

/// <summary>
/// Access to Business Central (section <c>Bc</c>). Credentials never live here: the token comes from a
/// <see cref="Azure.Core.TokenCredential"/> (managed identity / environment / user secrets).
/// </summary>
public sealed class BcClientOptions
{
    public const string ConfigurationSection = "Bc";

    /// <summary>API root, e.g. <c>https://api.businesscentral.dynamics.com/v2.0/{tenant}/{environment}/api/</c>.</summary>
    public Uri? BaseUrl { get; set; }

    /// <summary>BC company ID, inserted for <c>{companyId}</c> in the paths.</summary>
    public string CompanyId { get; set; } = "";

    /// <summary>Token scope for BC.</summary>
    public string Scope { get; set; } = "https://api.businesscentral.dynamics.com/.default";

    /// <summary>Credential kind for <c>TokenCredentialFactory</c> (<c>ManagedIdentity</c> or default chain).</summary>
    public string? Credential { get; set; }

    /// <summary>Standard API v2.0 employees. TODO(BC-Modell): store, position code, employment type and hours need a custom API page.</summary>
    public string EmployeesPath { get; set; } = "v2.0/companies({companyId})/employees";

    /// <summary>LSC stores. TODO(BC-Modell): no standard API; custom API page path required.</summary>
    public string StoresPath { get; set; } = "";

    /// <summary>Employee absences with external ID. TODO(BC-Modell): custom API page path required.</summary>
    public string AbsencesPath { get; set; } = "";

    /// <summary>BC cause-of-absence codes per absence type (codes seen in the BC export: KRANK, URLAUB).</summary>
    public Dictionary<string, string> AbsenceTypeCodes { get; set; } = new(StringComparer.Ordinal)
    {
        ["Sickness"] = "KRANK",
        ["Vacation"] = "URLAUB",
    };

    /// <summary>TODO(BC-Modell): BC approval states per absence status; unmapped statuses are sent by name.</summary>
    public Dictionary<string, string> AbsenceStatusCodes { get; set; } = new(StringComparer.Ordinal);

    public BcRateLimitOptions RateLimit { get; set; } = new();

    public BcResilienceOptions Resilience { get; set; } = new();
}

public sealed class BcResilienceOptions
{
    /// <summary>Retries of safe requests (GET) and of throttled writes (429).</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base delay of the exponential backoff; <c>Retry-After</c> of BC wins.</summary>
    public TimeSpan BaseDelay { get; set; } = TimeSpan.FromSeconds(1);

    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(30);

    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromMinutes(3);

    /// <summary>How long the circuit stays open after repeated failures.</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromSeconds(30);
}

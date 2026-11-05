namespace Access.Core.StepUp;

/// <summary>
/// Second factor with Microsoft Entra (section <c>Access:StepUp</c>): the person signs in with personnel number and PIN as before and, for
/// rights that demand it (see <c>AppPolicies.PrivilegeWithSecondFactor</c>), confirms with their Microsoft account (OpenID Connect,
/// authorization code with PKCE, <c>prompt=login</c>). Access requires <c>amr</c> to contain <c>mfa</c> itself and does not rely on the tenant.
/// <para>
/// <see cref="ClientSecret"/> comes from user secrets / Aspire / Key Vault only, never from <c>appsettings*.json</c>. The tenant (conditional
/// access or security defaults) must make Entra demand MFA; otherwise every step-up is refused (fail closed). Off by default.
/// </para>
/// </summary>
public sealed class StepUpOptions
{
    public const string ConfigurationSection = "Access:StepUp";

    /// <summary>Whether the step-up is available at all. Without it <c>step-up/start</c> answers 503 and nothing else is validated.</summary>
    public bool Enabled { get; set; }

    /// <summary>Directory (tenant) id of the company tenant: the only tenant whose tokens count (<c>tid</c>).</summary>
    public string? TenantId { get; set; }

    /// <summary>Application (client) id of the app registration of Access; the expected audience of the ID token.</summary>
    public string? ClientId { get; set; }

    /// <summary>Client secret of the app registration (secret!).</summary>
    public string? ClientSecret { get; set; }

    /// <summary>Redirect address registered at Entra: the address of <c>GET /api/auth/step-up/callback</c> of Access as the browser reaches it.</summary>
    public string? RedirectUri { get; set; }

    /// <summary>Where the browser is sent after the callback (the portal, absolute or relative to Access); <c>?result=ok</c> or <c>?result=failed</c> is appended.</summary>
    public string? ReturnUrl { get; set; }

    /// <summary>How long a confirmation counts.</summary>
    public TimeSpan Validity { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Local and test environments only: base address of the OpenID provider instead of <c>https://login.microsoftonline.com/{TenantId}/v2.0</c>
    /// (an Entra stand-in). Leave empty in production.
    /// </summary>
    public string? Authority { get; set; }

    /// <summary>The address of the OpenID provider; the issuer of the ID tokens equals it.</summary>
    public string EffectiveAuthority => string.IsNullOrWhiteSpace(Authority)
        ? $"https://login.microsoftonline.com/{TenantId}/v2.0"
        : Authority.TrimEnd('/');

    /// <summary>Settings that are missing or malformed, by name (never their values). Empty when the step-up is off or complete.</summary>
    public IReadOnlyList<string> Problems()
    {
        if (!Enabled)
        {
            return [];
        }

        var problems = new List<string>();
        Require(TenantId, nameof(TenantId));
        Require(ClientId, nameof(ClientId));
        Require(ClientSecret, nameof(ClientSecret));
        Require(RedirectUri, nameof(RedirectUri), absoluteUri: true);
        Require(ReturnUrl, nameof(ReturnUrl));
        if (!string.IsNullOrWhiteSpace(Authority) && !Uri.TryCreate(Authority, UriKind.Absolute, out _))
        {
            problems.Add(nameof(Authority));
        }

        if (Validity <= TimeSpan.Zero)
        {
            problems.Add(nameof(Validity));
        }

        return problems;

        void Require(string? value, string name, bool absoluteUri = false)
        {
            if (string.IsNullOrWhiteSpace(value) || (absoluteUri && !Uri.TryCreate(value, UriKind.Absolute, out _)))
            {
                problems.Add(name);
            }
        }
    }
}

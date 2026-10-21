using Portal.Common.Api;
using Microsoft.AspNetCore.Authorization;

namespace Portal.Authentication;

/// <summary>
/// Vordefinierte Fehlermeldungen (403), wenn jemand ohne ausreichende Berechtigung Inhalte einsehen, erstellen, bearbeiten oder löschen
/// will — sowohl bei fehlendem Recht als auch bei einem Datensatz außerhalb des eigenen Scopes. Das Frontend kann sie direkt anzeigen.
/// </summary>
public static class AuthMessages
{
    public const string Read = "Sie haben nicht die erforderliche Berechtigung, diese Inhalte einzusehen.";
    public const string Create = "Sie haben nicht die erforderliche Berechtigung, diese Inhalte zu erstellen.";
    public const string Update = "Sie haben nicht die erforderliche Berechtigung, diese Inhalte zu bearbeiten.";
    public const string Delete = "Sie haben nicht die erforderliche Berechtigung, diese Inhalte zu löschen.";
    public const string Other = "Sie haben nicht die erforderliche Berechtigung für diese Aktion.";

    public static string For(CrudAction action) => action switch
    {
        CrudAction.Read => Read,
        CrudAction.Create => Create,
        CrudAction.Update => Update,
        CrudAction.Delete => Delete,
        _ => Other
    };

    /// <summary>Aus dem Namen der Aktion (letzter Teil des Rechts, z. B. "update" in "report:update").</summary>
    public static string ForAction(string? action) => action switch
    {
        "read" => Read,
        "create" => Create,
        "update" => Update,
        "delete" => Delete,
        _ => Other
    };

    /// <summary>Die Meldung zu dem Recht, das die Route verlangt (aus ihren Autorisierungs-Metadaten).</summary>
    public static string ForEndpoint(Endpoint? endpoint)
    {
        var policy = endpoint?.Metadata.GetOrderedMetadata<IAuthorizeData>()
            .Select(a => a.Policy)
            .LastOrDefault(p => p is not null && p.StartsWith(AuthPolicies.PrivilegePrefix, StringComparison.Ordinal));
        return ForAction(policy?[(policy.LastIndexOf(':') + 1)..]);
    }

    public static ApiException Forbidden(CrudAction action) => new(StatusCodes.Status403Forbidden, For(action));
}

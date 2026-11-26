using System.Security.Cryptography;
using System.Text;
using Notifications.Contracts;
using Portal.Core;
using Shared.Authorization;
using Shared.Infrastructure.Messaging;

namespace Portal.Infrastructure.CashRegisters;

/// <summary>
/// The notifications of the Kassenregister, sent as <c>SendNotification</c> through the outbox in the same transaction as the change. They carry
/// <b>no data</b>: InApp and a generic push, the template only says that registers are waiting for their seal, the link leads to the list. The id is
/// derived from what happened, so a redelivery is not shown twice.
/// </summary>
public sealed class CashRegisterNotifier(Outbox<PortalDbContext> outbox, IPermissionCache permissions, TimeProvider time)
{
    public const string UnsealedStore = "portal.cash-register-unsealed.store";
    public const string UnsealedDistrict = "portal.cash-register-unsealed.district";

    /// <summary>The store managers of a store: those who hold the privilege to record registers for exactly this store (not everybody with it everywhere, such as the administration).</summary>
    public IReadOnlyList<Guid> StoreManagersOf(Guid storeId) =>
        [.. permissions.All()
            .Where(s => s.Privileges.TryGetValue(PortalPrivileges.CashRegisterRecord, out var reach) && !reach.Unrestricted && reach.StoreIds.Contains(storeId))
            .Select(s => s.EmployeeId)
            .Order()];

    /// <summary><paramref name="quiet"/>: an info in the portal only, no push (the district leaders are informed this way).</summary>
    public bool Send(string template, string key, IEnumerable<Guid> recipients, Guid correlation, bool quiet = false)
    {
        var to = recipients.Distinct().ToList();
        if (to.Count == 0)
        {
            return false;
        }

        var now = time.GetUtcNow();
        outbox.Send(new SendNotification(
            Guid.CreateVersion7(now), correlation, now, Deterministic($"{template}:{key}"), to, template, new Dictionary<string, string>(),
            quiet ? [NotificationChannel.InApp] : [NotificationChannel.InApp, NotificationChannel.Push], "/cash-registers"));
        return true;
    }

    private static Guid Deterministic(string text) => new(SHA256.HashData(Encoding.UTF8.GetBytes(text))[..16]);
}

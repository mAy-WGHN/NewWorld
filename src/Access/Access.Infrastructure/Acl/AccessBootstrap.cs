using Access.Core.Acl;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Access.Infrastructure.Acl;

/// <summary>At start: built-in roles exist and Access's own privileges are registered (owner <c>access</c>).</summary>
public sealed class AccessBootstrap(IServiceScopeFactory scopes) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AccessDbContext>();
        if (!await db.Database.CanConnectAsync(cancellationToken))
        {
            return; // readiness reports the database; the next start completes the bootstrap
        }

        foreach (var role in new[] { Role.Administrator, Role.PublicUser, Role.FormerEmployee })
        {
            if (await db.Roles.FindAsync([role.Id], cancellationToken) is null)
            {
                db.Roles.Add(new RoleRecord { Id = role.Id, Name = role.Name, Privileges = [.. role.Privileges], BuiltIn = true });
            }
        }

        await db.Catalog.Where(c => c.Owner == AccessPrivileges.Owner).ExecuteDeleteAsync(cancellationToken);
        db.Catalog.AddRange(AccessPrivileges.Catalog.Select(c => new CatalogEntryRecord { Key = c.Key, Owner = c.Owner, Scopable = c.Scopable, Description = c.Description }));
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

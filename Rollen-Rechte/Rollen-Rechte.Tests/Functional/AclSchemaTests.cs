using Rollen_Rechte.Domain.Entities;
using Rollen_Rechte.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Rollen_Rechte.Tests.Functional;

/// <summary>Schema-Tests für acl_role und acl_user_role.</summary>
public class AclSchemaTests(ApiFactory factory) : ApiTestBase(factory)
{
    [Fact]
    public async Task Role_StoresPrivilegesAsJsonList_AndCanBeAssignedToSeveralUsers()
    {
        var first = await CreateAdminAsync();
        var second = await CreateAdminAsync();
        var roleId = await Factory.WithDbAsync(async (db, _) =>
        {
            var role = new AclRole { Name = Unique("Rolle"), Description = "Darf lesen", Privileges = ["user:read", "user:update"] };
            db.AclRoles.Add(role);
            db.AclUserRoles.AddRange(
                new AclUserRole { UserId = first.Id, AclRoleId = role.Id },
                new AclUserRole { UserId = second.Id, AclRoleId = role.Id });
            await db.SaveChangesAsync();
            return role.Id;
        });

        var (role, userIds, rawJson) = await QueryDbAsync(async db => (
            await db.AclRoles.AsNoTracking().SingleAsync(r => r.Id == roleId),
            await db.AclUserRoles.Where(x => x.AclRoleId == roleId).Select(x => x.UserId).ToListAsync(),
            await db.Database.SqlQuery<string>($"SELECT privileges::text AS \"Value\" FROM acl_role WHERE id = {roleId}").SingleAsync()));

        Assert.Equal(["user:read", "user:update"], role.Privileges);
        Assert.Equal("[\"user:read\",\"user:update\"]", rawJson.Replace(" ", ""));
        Assert.Equal(2, userIds.Count);
    }

    [Fact]
    public async Task UserRole_IsUniquePerUserAndRole_AndDisappearsWithTheUser()
    {
        var user = await CreateAdminAsync();
        var roleId = await Factory.WithDbAsync(async (db, _) =>
        {
            var role = new AclRole { Name = Unique("Rolle") };
            db.AclRoles.Add(role);
            db.AclUserRoles.Add(new AclUserRole { UserId = user.Id, AclRoleId = role.Id });
            await db.SaveChangesAsync();
            return role.Id;
        });

        await Assert.ThrowsAsync<DbUpdateException>(() => Factory.WithDbAsync(async (db, _) =>
        {
            db.AclUserRoles.Add(new AclUserRole { UserId = user.Id, AclRoleId = roleId });
            await db.SaveChangesAsync();
        }));

        await Factory.WithDbAsync(async (db, _) =>
            await db.Users.Where(u => u.Id == user.Id).ExecuteDeleteAsync());

        Assert.False(await QueryDbAsync(db => db.AclUserRoles.AnyAsync(x => x.AclRoleId == roleId)));
        Assert.True(await QueryDbAsync(db => db.AclRoles.AnyAsync(r => r.Id == roleId)));
    }
}

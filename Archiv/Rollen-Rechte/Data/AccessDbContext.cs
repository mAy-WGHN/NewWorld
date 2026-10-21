using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Rollen_Rechte.Domain.Entities;

namespace Rollen_Rechte.Data;

/// <summary>
/// Datenbank von "Rollen &amp; Rechte": Benutzer, Anmeldung (Refresh-Tokens), Rollen, Zuweisungen, Scopes, Filialen (Stammdaten für Scopes)
/// und die von den Anwendungen registrierten Rechte. Spaltennamen in snake_case (EFCore.NamingConventions).
/// </summary>
public class AccessDbContext(DbContextOptions<AccessDbContext> options) : DbContext(options)
{
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AclRole> AclRoles => Set<AclRole>();
    public DbSet<AclUserRole> AclUserRoles => Set<AclUserRole>();
    public DbSet<CustomPrivilege> CustomPrivileges => Set<CustomPrivilege>();
    public DbSet<Scope> Scopes => Set<Scope>();
    public DbSet<ScopeMember> ScopeMembers => Set<ScopeMember>();
    public DbSet<AclUserRoleScope> AclUserRoleScopes => Set<AclUserRoleScope>();
    public DbSet<AppPrivilege> AppPrivileges => Set<AppPrivilege>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>()
            .HaveColumnType("timestamp(0) without time zone")
            .HaveConversion<UnspecifiedDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>()
            .HaveColumnType("timestamp(0) without time zone")
            .HaveConversion<NullableUnspecifiedDateTimeConverter>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Location>(e =>
        {
            e.ToTable("locations");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(255);
            e.Property(x => x.StoreCode).HasMaxLength(10);
            e.HasIndex(x => x.StoreCode).IsUnique().HasDatabaseName("uniq_location_store_code");
        });

        modelBuilder.Entity<User>(e =>
        {
            e.ToTable("users");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Username).HasMaxLength(180);
            e.Property(x => x.Password).HasMaxLength(255);
            e.Property(x => x.BcPinEncrypted).HasMaxLength(512);
            e.HasIndex(x => x.Username).IsUnique().HasDatabaseName("uniq_user_username");
            e.HasOne<Location>().WithMany().HasForeignKey(x => x.HomeLocationId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AclRole>(e =>
        {
            e.ToTable("acl_role");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(255);
            e.HasIndex(x => x.Name).IsUnique().HasDatabaseName("uniq_acl_role_name");
            e.Property(x => x.Description).HasColumnType("text");
            e.Property(x => x.Privileges)
                .HasColumnType("json")
                .HasConversion(
                    v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                    v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>(),
                    new ValueComparer<List<string>>(
                        (a, b) => a!.SequenceEqual(b!),
                        v => v.Aggregate(0, (h, s) => HashCode.Combine(h, s)),
                        v => v.ToList()));
        });

        modelBuilder.Entity<CustomPrivilege>(e =>
        {
            e.ToTable("custom_privilege");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Key).HasMaxLength(100);
            e.HasIndex(x => x.Key).IsUnique().HasDatabaseName("uniq_custom_privilege_key");
            e.Property(x => x.Label).HasMaxLength(255);
            e.Property(x => x.Description).HasColumnType("text");
        });

        modelBuilder.Entity<AppPrivilege>(e =>
        {
            e.ToTable("app_privilege");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.AppKey).HasMaxLength(50);
            e.Property(x => x.Privilege).HasMaxLength(100);
            e.HasIndex(x => x.Privilege).IsUnique().HasDatabaseName("uniq_app_privilege_privilege");
            e.HasIndex(x => x.AppKey);
            e.Property(x => x.Label).HasMaxLength(255);
            e.Property(x => x.AreaKey).HasMaxLength(50);
            e.Property(x => x.AreaLabel).HasMaxLength(255);
        });

        modelBuilder.Entity<Scope>(e =>
        {
            e.ToTable("scope");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Name).HasMaxLength(255);
            e.HasIndex(x => x.Name).IsUnique().HasDatabaseName("uniq_scope_name");
            e.Property(x => x.Description).HasColumnType("text");
            e.HasMany(x => x.Members).WithOne(m => m.Scope).HasForeignKey(m => m.ScopeId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ScopeMember>(e =>
        {
            e.ToTable("scope_member");
            e.HasKey(x => new { x.ScopeId, x.Dimension, x.RefId });
            e.Property(x => x.Dimension).HasMaxLength(50);
            e.HasIndex(x => new { x.Dimension, x.RefId });
        });

        modelBuilder.Entity<AclUserRoleScope>(e =>
        {
            e.ToTable("acl_user_role_scope");
            e.HasKey(x => new { x.UserId, x.AclRoleId, x.ScopeId });
            // Ein Scope, der noch zugewiesen ist, lässt sich nicht löschen (die Zuweisung würde sonst unbemerkt ihre Einschränkung verlieren).
            e.HasOne(x => x.Scope).WithMany().HasForeignKey(x => x.ScopeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AclUserRole>(e =>
        {
            e.ToTable("acl_user_role");
            e.HasKey(x => new { x.UserId, x.AclRoleId });
            e.HasMany(x => x.Scopes).WithOne().HasForeignKey(x => new { x.UserId, x.AclRoleId }).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AclRole).WithMany().HasForeignKey(x => x.AclRoleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RefreshToken>(e =>
        {
            e.ToTable("refresh_tokens");
            e.Property(x => x.Id).UseIdentityByDefaultColumn();
            e.Property(x => x.Token).HasColumnName("refresh_token").HasMaxLength(128);
            e.Property(x => x.Username).HasMaxLength(255);
            e.Property(x => x.Family).HasMaxLength(32);
            e.HasIndex(x => x.Token).IsUnique();
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        TouchUpdatedAt();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        TouchUpdatedAt();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void TouchUpdatedAt()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries().Where(e => e.State == EntityState.Modified))
        {
            switch (entry.Entity)
            {
                case User u: u.UpdatedAt = now; break;
                case AclRole r: r.UpdatedAt = now; break;
                case CustomPrivilege c: c.UpdatedAt = now; break;
                case Scope sc: sc.UpdatedAt = now; break;
                case AclUserRole a: a.UpdatedAt = now; break;
                case AppPrivilege p: p.UpdatedAt = now; break;
            }
        }
    }
}

using Access.Infrastructure.Acl;
using Access.Infrastructure.Login;
using Access.Infrastructure.People;
using Access.Infrastructure.Publishing;
using Microsoft.EntityFrameworkCore;
using Shared.Infrastructure.Authorization;
using Shared.Infrastructure.Messaging;

namespace Access.Infrastructure;

/// <summary>Own schema <c>access</c>; no cross-schema joins.</summary>
public sealed class AccessDbContext(DbContextOptions<AccessDbContext> options) : DbContext(options)
{
    public const string Schema = "access";

    public DbSet<PersonRecord> People => Set<PersonRecord>();
    public DbSet<StoreRecord> Stores => Set<StoreRecord>();
    public DbSet<CatalogEntryRecord> Catalog => Set<CatalogEntryRecord>();
    public DbSet<RoleRecord> Roles => Set<RoleRecord>();
    public DbSet<ScopeRecord> Scopes => Set<ScopeRecord>();
    public DbSet<AssignmentRecord> Assignments => Set<AssignmentRecord>();
    public DbSet<ResponsibilityRecord> Responsibilities => Set<ResponsibilityRecord>();
    public DbSet<PublicationRecord> Publications => Set<PublicationRecord>();
    public DbSet<CredentialRecord> Credentials => Set<CredentialRecord>();
    public DbSet<RefreshTokenRecord> RefreshTokens => Set<RefreshTokenRecord>();

    /// <summary>Provider settings shared by runtime and design time (migrations).</summary>
    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) => options
        .UseNpgsql(connectionString, npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema))
        .UseSnakeCaseNamingConvention();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ConfigureMessaging();
        modelBuilder.ConfigurePermissionCache();

        modelBuilder.Entity<PersonRecord>(b =>
        {
            b.ToTable("people");
            b.HasKey(p => p.EmployeeId);
            b.HasIndex(p => p.PersonnelNumber);
            b.HasIndex(p => p.HomeStoreId);
        });
        modelBuilder.Entity<StoreRecord>(b =>
        {
            b.ToTable("stores");
            b.HasKey(s => s.StoreId);
        });
        modelBuilder.Entity<CatalogEntryRecord>(b =>
        {
            b.ToTable("catalog_entries");
            b.HasKey(c => c.Key);
            b.HasIndex(c => c.Owner);
        });
        modelBuilder.Entity<RoleRecord>(b =>
        {
            b.ToTable("roles");
            b.HasKey(r => r.Id);
            b.HasIndex(r => r.Name).IsUnique();
        });
        modelBuilder.Entity<ScopeRecord>(b =>
        {
            b.ToTable("scopes");
            b.HasKey(s => s.Id);
            b.HasIndex(s => s.Name).IsUnique();
        });
        modelBuilder.Entity<AssignmentRecord>(b =>
        {
            b.ToTable("assignments");
            b.HasKey(a => a.Id);
            b.HasIndex(a => a.EmployeeId);
            b.HasIndex(a => a.RoleId);
        });
        modelBuilder.Entity<ResponsibilityRecord>(b =>
        {
            b.ToTable("responsibilities");
            b.HasKey(r => r.Id);
            b.HasIndex(r => new { r.TargetKind, r.TargetId });
            b.HasIndex(r => r.ResponsibleEmployeeId);
        });
        modelBuilder.Entity<CredentialRecord>(b =>
        {
            b.ToTable("credentials");
            b.HasKey(c => c.EmployeeId);
        });
        modelBuilder.Entity<RefreshTokenRecord>(b =>
        {
            b.ToTable("refresh_tokens");
            b.HasKey(t => t.Id);
            b.HasIndex(t => t.TokenHash).IsUnique();
            b.HasIndex(t => t.EmployeeId);
            b.HasIndex(t => t.ExpiresAt);
        });
        modelBuilder.Entity<PublicationRecord>(b =>
        {
            b.ToTable("publications");
            b.HasKey(p => new { p.EmployeeId, p.Kind });
        });
    }
}

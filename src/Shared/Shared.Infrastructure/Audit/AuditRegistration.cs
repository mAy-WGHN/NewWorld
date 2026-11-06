using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Shared.Infrastructure.Audit;

public static class AuditRegistration
{
    /// <summary>Maps the audit table into the service's own schema (call from <c>OnModelCreating</c>).</summary>
    public static ModelBuilder ConfigureAudit(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditEntryRecord>(b =>
        {
            b.ToTable("audit_entries");
            b.HasKey(e => e.Id);
            b.Property(e => e.Id).ValueGeneratedNever();
            b.Property(e => e.ActorKind).HasConversion<string>().HasMaxLength(20);
            b.Property(e => e.Action).HasConversion<string>().HasMaxLength(20);
            b.Property(e => e.ActorPersonnelNumber).HasMaxLength(50);
            b.Property(e => e.ActorName).HasMaxLength(255);
            b.Property(e => e.EntityType).HasMaxLength(200);
            b.Property(e => e.EntityId).HasMaxLength(400);
            b.Property(e => e.ChangesJson).HasColumnType("jsonb");
            b.HasIndex(e => e.OccurredAt);
            b.HasIndex(e => e.ActorPersonnelNumber);
        });
        return modelBuilder;
    }

    /// <summary>
    /// Registers the interceptor and the person lookup of a service. Add the interceptor to the context with <see cref="UseAudit"/>.
    /// </summary>
    public static IServiceCollection AddAudit<TLookup>(this IServiceCollection services, Action<AuditOptions>? configure = null)
        where TLookup : class, IAuditPersonLookup
    {
        var options = new AuditOptions();
        configure?.Invoke(options);
        services.AddHttpContextAccessor();
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton(options);
        services.AddScoped<IAuditPersonLookup, TLookup>();
        services.AddScoped<AuditSaveChangesInterceptor>();
        return services;
    }

    public static Microsoft.EntityFrameworkCore.DbContextOptionsBuilder UseAudit(
        this Microsoft.EntityFrameworkCore.DbContextOptionsBuilder options, IServiceProvider services) =>
        options.AddInterceptors(services.GetRequiredService<AuditSaveChangesInterceptor>());

    /// <summary>SQL for a migration: the database itself refuses to change or delete audit lines (in addition to the interceptor).</summary>
    public static string AppendOnlySql(string schema) => $"""
        CREATE OR REPLACE FUNCTION "{schema}".audit_entries_append_only() RETURNS trigger AS $$
        BEGIN
            RAISE EXCEPTION 'The audit log is append only.';
        END;
        $$ LANGUAGE plpgsql;
        CREATE TRIGGER audit_entries_append_only BEFORE UPDATE OR DELETE ON "{schema}".audit_entries
            FOR EACH ROW EXECUTE FUNCTION "{schema}".audit_entries_append_only();
        """;

    public static string DropAppendOnlySql(string schema) => $"""
        DROP TRIGGER IF EXISTS audit_entries_append_only ON "{schema}".audit_entries;
        DROP FUNCTION IF EXISTS "{schema}".audit_entries_append_only();
        """;
}

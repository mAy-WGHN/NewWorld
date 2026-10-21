using HrTool_API.Authentication;
using HrTool_API.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace HrTool_API.Data;

/// <summary>
/// Die eigene Datenbank des HR-Tools (Spalten in snake_case). Benutzer, Rollen, Rechte, Scopes und Filialen gehören Rollen-Rechte und stehen hier nicht:
/// Das HR-Tool speichert nur deren IDs (<c>Employee.UserId</c>, <c>Workplace.LocationId</c>), ohne Fremdschlüssel.
/// </summary>
public class HrToolDbContext(DbContextOptions<HrToolDbContext> options, ScopeContext? scopeContext = null) : DbContext(options)
{
    public DbSet<Workplace> Workplaces => Set<Workplace>();
    public DbSet<PlanningRole> PlanningRoles => Set<PlanningRole>();
    public DbSet<EmploymentContract> EmploymentContracts => Set<EmploymentContract>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<CashierProfile> CashierProfiles => Set<CashierProfile>();
    public DbSet<ShiftPattern> ShiftPatterns => Set<ShiftPattern>();
    public DbSet<AbsenceType> AbsenceTypes => Set<AbsenceType>();
    public DbSet<Absence> Absences => Set<Absence>();
    public DbSet<Schedule> Schedules => Set<Schedule>();
    public DbSet<Shift> Shifts => Set<Shift>();

    // Scope der laufenden Anfrage (von der Rechteprüfung gesetzt) für die Abfragefilter. Ohne Anfrage-Kontext (Import, Tests) ist nichts gesetzt = uneingeschränkt.
    // Bewusst Bool/Array: EF parametrisiert sie je Abfrage.
    public bool ScopeAll => scopeContext?.Current.All ?? true;
    public Guid[] ScopeLocations => scopeContext?.Current.Locations.ToArray() ?? [];

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>()
            .HaveColumnType("timestamp(0) without time zone")
            .HaveConversion<UnspecifiedDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>()
            .HaveColumnType("timestamp(0) without time zone")
            .HaveConversion<NullableUnspecifiedDateTimeConverter>();

        // Enums liegen als Kleinbuchstaben-String in der DB.
        configurationBuilder.Properties<ScheduleStatus>().HaveMaxLength(20).HaveConversion<LowerCaseEnumConverter<ScheduleStatus>>();
        configurationBuilder.Properties<ShiftPatternKind>().HaveMaxLength(20).HaveConversion<LowerCaseEnumConverter<ShiftPatternKind>>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Workplace>(e =>
        {
            e.ToTable("workplaces");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Code).HasMaxLength(10);
            e.Property(x => x.Name).HasMaxLength(100);
            e.Property(x => x.StoreCode).HasMaxLength(10);
            e.Property(x => x.CostCenterCode).HasMaxLength(10);
            e.Property(x => x.RegionCode).HasMaxLength(20);
            e.HasIndex(x => x.Code).IsUnique();
            e.HasIndex(x => x.LocationId).IsUnique().HasFilter("location_id IS NOT NULL"); // je Filiale ein Arbeitsort
        });

        modelBuilder.Entity<PlanningRole>(e =>
        {
            e.ToTable("planning_roles");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Code).HasMaxLength(10);
            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<EmploymentContract>(e =>
        {
            e.ToTable("employment_contracts");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.WeeklyHours).HasPrecision(5, 2);
            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<Employee>(e =>
        {
            e.ToTable("employees");
            // Scope: nur Mitarbeiter eines Arbeitsorts, dessen Filiale im Scope der Anfrage liegt. Ohne Arbeitsort oder ohne Filiale sieht sie nur, wer uneingeschränkt darf.
            e.HasQueryFilter(x => ScopeAll || (x.Workplace != null && x.Workplace.LocationId != null && ScopeLocations.Contains(x.Workplace.LocationId.Value)));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.EmployeeNo).HasMaxLength(10);
            e.Property(x => x.FirstName).HasMaxLength(100);
            e.Property(x => x.LastName).HasMaxLength(100);
            e.Property(x => x.Function).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(255);
            e.Property(x => x.Remark).HasColumnType("text");
            e.Property(x => x.RegionCode).HasMaxLength(20);
            e.HasIndex(x => x.EmployeeNo).IsUnique();
            e.HasIndex(x => x.UserId).IsUnique().HasFilter("user_id IS NOT NULL");
            e.HasIndex(x => x.WorkplaceId);

            e.HasOne(x => x.Contract).WithMany().HasForeignKey(x => x.ContractId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Workplace).WithMany().HasForeignKey(x => x.WorkplaceId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.PlanningRole).WithMany().HasForeignKey(x => x.PlanningRoleId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.CashierProfile).WithOne(x => x.Employee).HasForeignKey<CashierProfile>(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CashierProfile>(e =>
        {
            e.ToTable("cashier_profiles");
            e.HasKey(x => x.EmployeeId);
            // Folgt dem Scope des Mitarbeiters.
            e.HasQueryFilter(x => ScopeAll || (x.Employee!.Workplace != null && x.Employee.Workplace.LocationId != null && ScopeLocations.Contains(x.Employee.Workplace.LocationId.Value)));
            e.Property(x => x.ReceiptName).HasMaxLength(50);
            e.Property(x => x.WorkRelation).HasMaxLength(30);
            e.Property(x => x.StoreCode).HasMaxLength(10);
            e.Property(x => x.PermissionGroup).HasMaxLength(30);
            e.Property(x => x.InterfaceProfile).HasMaxLength(50);
            e.Property(x => x.MenuProfile).HasMaxLength(50);
            e.Property(x => x.PosStyleProfile).HasMaxLength(50);
        });

        modelBuilder.Entity<ShiftPattern>(e =>
        {
            e.ToTable("shift_patterns");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Code).HasMaxLength(30);
            e.Property(x => x.Description).HasMaxLength(100);
            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<AbsenceType>(e =>
        {
            e.ToTable("absence_types");
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Code).HasMaxLength(40);
            e.Property(x => x.Description).HasMaxLength(100);
            e.Property(x => x.ScheduleSymbol).HasMaxLength(10);
            e.Property(x => x.SetWorkCode).HasMaxLength(20);
            e.HasIndex(x => x.Code).IsUnique();
        });

        modelBuilder.Entity<Absence>(e =>
        {
            e.ToTable("absences");
            e.HasQueryFilter(x => ScopeAll || (x.Employee!.Workplace != null && x.Employee.Workplace.LocationId != null && ScopeLocations.Contains(x.Employee.Workplace.LocationId.Value)));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Days).HasPrecision(6, 2);
            e.Property(x => x.VacationDays).HasPrecision(6, 2);
            e.Property(x => x.Remark).HasColumnType("text");
            e.Property(x => x.ApprovalStatus).HasMaxLength(50);
            e.Property(x => x.RejectionReason).HasColumnType("text");
            e.Property(x => x.RecordedBy).HasMaxLength(50);
            e.HasIndex(x => new { x.EmployeeId, x.DateFrom });

            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.AbsenceType).WithMany().HasForeignKey(x => x.AbsenceTypeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Schedule>(e =>
        {
            e.ToTable("schedules");
            // Scope: nur Pläne der Arbeitsorte, deren Filiale im Scope der Anfrage liegt.
            e.HasQueryFilter(x => ScopeAll || (x.Workplace!.LocationId != null && ScopeLocations.Contains(x.Workplace.LocationId.Value)));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.Number).HasMaxLength(20);
            e.Property(x => x.BudgetHours).HasPrecision(8, 2);
            e.HasIndex(x => x.Number).IsUnique();
            e.HasIndex(x => new { x.WorkplaceId, x.Year, x.Month }).IsUnique();

            e.HasOne(x => x.Workplace).WithMany().HasForeignKey(x => x.WorkplaceId).OnDelete(DeleteBehavior.Restrict);

            e.HasMany(x => x.Shifts).WithOne(x => x.Schedule).HasForeignKey(x => x.ScheduleId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Shift>(e =>
        {
            e.ToTable("shifts");
            e.HasQueryFilter(x => ScopeAll || (x.Schedule!.Workplace!.LocationId != null && ScopeLocations.Contains(x.Schedule.Workplace.LocationId.Value)));
            e.Property(x => x.Id).ValueGeneratedNever();
            e.Property(x => x.ShiftCode).HasMaxLength(20);
            e.Property(x => x.Remark).HasMaxLength(255);
            e.HasIndex(x => new { x.ScheduleId, x.EmployeeId, x.Date });
            e.HasIndex(x => new { x.EmployeeId, x.Date });

            e.HasOne(x => x.Employee).WithMany().HasForeignKey(x => x.EmployeeId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PlanningRole).WithMany().HasForeignKey(x => x.PlanningRoleId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.AbsenceType).WithMany().HasForeignKey(x => x.AbsenceTypeId).OnDelete(DeleteBehavior.SetNull);
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
        foreach (var entry in ChangeTracker.Entries<IHasUpdatedAt>().Where(e => e.State == EntityState.Modified))
            entry.Entity.UpdatedAt = now;
    }
}

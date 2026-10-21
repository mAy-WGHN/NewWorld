using Microsoft.EntityFrameworkCore;

namespace Shared.Approvals;

public static class ApprovalModelBuilderExtensions
{
    /// <summary>Maps approvals into the including API's own schema (call from <c>OnModelCreating</c>).</summary>
    public static ModelBuilder ConfigureApprovals(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ApprovalRequest>(b =>
        {
            b.ToTable("approval_requests");
            b.HasKey(r => r.Id);
            b.Property(r => r.SubjectType).HasMaxLength(100);
            b.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
            b.HasIndex(r => new { r.SubjectType, r.SubjectId });
            b.HasMany(r => r.Steps).WithOne().HasForeignKey("ApprovalRequestId").OnDelete(DeleteBehavior.Cascade);
            b.Navigation(r => r.Steps).UsePropertyAccessMode(PropertyAccessMode.Field);
            b.Metadata.FindNavigation(nameof(ApprovalRequest.Steps))!.SetField("_steps");
        });

        modelBuilder.Entity<ApprovalStep>(b =>
        {
            b.ToTable("approval_steps");
            b.HasKey(s => s.Id);
            b.Property(s => s.Decision).HasConversion<string>().HasMaxLength(20);
            b.HasIndex("ApprovalRequestId", nameof(ApprovalStep.Order)).IsUnique();
        });

        return modelBuilder;
    }
}

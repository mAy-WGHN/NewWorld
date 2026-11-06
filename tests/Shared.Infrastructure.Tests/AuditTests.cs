using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Time.Testing;
using Shared.Authorization;
using Shared.Infrastructure.Audit;
using Shared.Testing;

namespace Shared.Infrastructure.Tests;

public sealed class Widget
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string PinHash { get; set; } = string.Empty;
    public int Count { get; set; }
}

public sealed class Copy
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
}

public sealed class AuditDbContext(DbContextOptions<AuditDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Widget>();
        modelBuilder.Entity<Copy>();
        modelBuilder.ConfigureAudit();
    }
}

public sealed class AuditTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly Guid Person = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private sealed class Lookup : IAuditPersonLookup
    {
        public Task<AuditPerson?> FindAsync(DbContext db, Guid employeeId, CancellationToken cancellationToken) =>
            Task.FromResult<AuditPerson?>(employeeId == Person ? new AuditPerson("10042", "Erika Mustermann") : null);
    }

    private sealed class Accessor(HttpContext? context) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = context;
    }

    private static DefaultHttpContext AsPerson(Guid? id) => new DefaultHttpContext
    {
        User = new ClaimsPrincipal(new ClaimsIdentity(id is null ? [] : [new Claim(AppClaimTypes.EmployeeId, id.Value.ToString())], "test")),
    };

    private async Task<(DbContextOptions<AuditDbContext> Options, FakeTimeProvider Time)> CreateAsync(HttpContext? context, Action<AuditOptions>? configure = null)
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2028, 2, 29, 23, 30, 0, TimeSpan.Zero));
        var auditOptions = new AuditOptions();
        configure?.Invoke(auditOptions);
        var interceptor = new AuditSaveChangesInterceptor(new Accessor(context), time, auditOptions, new Lookup());
        var options = new DbContextOptionsBuilder<AuditDbContext>().UseNpgsql(await postgres.CreateDatabaseAsync()).AddInterceptors(interceptor).Options;
        await using var db = new AuditDbContext(options);
        await db.Database.EnsureCreatedAsync();
        return (options, time);
    }

    [Fact]
    public async Task Create_update_and_delete_are_logged_with_time_name_and_personnel_number()
    {
        var (options, time) = await CreateAsync(AsPerson(Person));
        var widget = new Widget { Name = "first" };

        await using (var db = new AuditDbContext(options))
        {
            db.Add(widget);
            await db.SaveChangesAsync();
        }

        time.Advance(TimeSpan.FromMinutes(45));
        await using (var db = new AuditDbContext(options))
        {
            (await db.Set<Widget>().SingleAsync()).Name = "second";
            await db.SaveChangesAsync();
        }

        await using (var db = new AuditDbContext(options))
        {
            db.Remove(await db.Set<Widget>().SingleAsync());
            await db.SaveChangesAsync();
        }

        await using var check = new AuditDbContext(options);
        var lines = await check.Set<AuditEntryRecord>().OrderBy(e => e.OccurredAt).ThenBy(e => e.Action).ToListAsync();
        lines.Select(e => e.Action).ShouldBe([AuditAction.Create, AuditAction.Update, AuditAction.Delete], ignoreOrder: true);
        lines.ShouldAllBe(e => e.ActorName == "Erika Mustermann" && e.ActorPersonnelNumber == "10042" && e.ActorKind == AuditActorKind.Person
                               && e.ActorEmployeeId == Person && e.EntityType == nameof(Widget) && e.EntityId == widget.Id.ToString());
        lines.Single(e => e.Action == AuditAction.Create).OccurredAt.ShouldBe(new DateTimeOffset(2028, 2, 29, 23, 30, 0, TimeSpan.Zero));
        lines.Single(e => e.Action == AuditAction.Update).OccurredAt.ShouldBe(new DateTimeOffset(2028, 3, 1, 0, 15, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task An_update_logs_only_what_changed_and_never_secrets()
    {
        var (options, _) = await CreateAsync(AsPerson(Person));
        await using (var db = new AuditDbContext(options))
        {
            db.Add(new Widget { Name = "a", PinHash = "secret-hash", Count = 1 });
            await db.SaveChangesAsync();
        }

        await using (var db = new AuditDbContext(options))
        {
            var widget = await db.Set<Widget>().SingleAsync();
            widget.Name = "b";
            widget.PinHash = "other-hash";
            await db.SaveChangesAsync();
        }

        await using var check = new AuditDbContext(options);
        var update = await check.Set<AuditEntryRecord>().SingleAsync(e => e.Action == AuditAction.Update);
        using var json = JsonDocument.Parse(update.ChangesJson!);
        json.RootElement.EnumerateObject().Select(p => p.Name).ShouldBe(["Name"]);
        json.RootElement.GetProperty("Name").GetProperty("old").GetString().ShouldBe("a");
        json.RootElement.GetProperty("Name").GetProperty("new").GetString().ShouldBe("b");

        var all = string.Concat(await check.Set<AuditEntryRecord>().Select(e => e.ChangesJson).ToListAsync());
        all.ShouldNotContain("hash");
    }

    [Fact]
    public async Task A_save_that_changes_nothing_writes_no_line()
    {
        var (options, _) = await CreateAsync(AsPerson(Person));
        await using (var db = new AuditDbContext(options))
        {
            db.Add(new Widget { Name = "a" });
            await db.SaveChangesAsync();
        }

        await using (var db = new AuditDbContext(options))
        {
            (await db.Set<Widget>().SingleAsync()).Name = "a";
            await db.SaveChangesAsync();
        }

        await using var check = new AuditDbContext(options);
        (await check.Set<AuditEntryRecord>().CountAsync()).ShouldBe(1);
    }

    [Fact]
    public async Task Without_a_request_nothing_is_logged()
    {
        var (options, _) = await CreateAsync(null);
        await using (var db = new AuditDbContext(options))
        {
            db.Add(new Widget { Name = "from a message handler" });
            await db.SaveChangesAsync();
        }

        await using var check = new AuditDbContext(options);
        (await check.Set<AuditEntryRecord>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task Excluded_types_are_not_logged()
    {
        var (options, _) = await CreateAsync(AsPerson(Person), o => o.Exclude<Copy>());
        await using (var db = new AuditDbContext(options))
        {
            db.Add(new Copy { Name = "copy" });
            await db.SaveChangesAsync();
        }

        await using var check = new AuditDbContext(options);
        (await check.Set<AuditEntryRecord>().CountAsync()).ShouldBe(0);
    }

    [Fact]
    public async Task An_unknown_person_is_logged_by_id_and_an_anonymous_caller_as_anonymous()
    {
        var unknown = Guid.NewGuid();
        var (options, _) = await CreateAsync(AsPerson(unknown));
        await using (var db = new AuditDbContext(options))
        {
            db.Add(new Widget { Name = "a" });
            await db.SaveChangesAsync();
        }

        var (anonymousOptions, _) = await CreateAsync(AsPerson(null));
        await using (var db = new AuditDbContext(anonymousOptions))
        {
            db.Add(new Widget { Name = "b" });
            await db.SaveChangesAsync();
        }

        await using var check = new AuditDbContext(options);
        var line = await check.Set<AuditEntryRecord>().SingleAsync();
        (line.ActorKind, line.ActorEmployeeId, line.ActorName).ShouldBe((AuditActorKind.Person, unknown, string.Empty));

        await using var anonymousCheck = new AuditDbContext(anonymousOptions);
        (await anonymousCheck.Set<AuditEntryRecord>().SingleAsync()).ActorKind.ShouldBe(AuditActorKind.Anonymous);
    }

    [Fact]
    public async Task The_log_cannot_be_changed_or_deleted()
    {
        var (options, _) = await CreateAsync(AsPerson(Person));
        await using var db = new AuditDbContext(options);
        db.Add(new Widget { Name = "a" });
        await db.SaveChangesAsync();

        var line = await db.Set<AuditEntryRecord>().SingleAsync();
        db.Remove(line);
        await Should.ThrowAsync<InvalidOperationException>(() => db.SaveChangesAsync());
    }

    [Theory]
    [InlineData("PinHash", true)]
    [InlineData("Pin", true)]
    [InlineData("RefreshToken", true)]
    [InlineData("WageType", true)]
    [InlineData("Mapping", false)]
    [InlineData("TemplateKey", false)]
    [InlineData("Shipping", false)]
    public void Secret_property_names_are_recognised_by_whole_words(string name, bool secret) =>
        AuditOptions.IsSecret(name).ShouldBe(secret);
}

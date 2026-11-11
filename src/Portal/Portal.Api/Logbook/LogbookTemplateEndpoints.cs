using System.Security.Claims;
using Portal.Api.Http;
using Portal.Core;
using Portal.Core.Logbook;
using Portal.Infrastructure.Logbook;
using Shared.Authorization;

namespace Portal.Api.Logbook;

public sealed record LogbookOptionDto(Guid Id, string Label);

/// <summary><c>Kind</c> is <c>singleChoice</c>, <c>multiChoice</c> or <c>freeText</c>.</summary>
public sealed record LogbookSectionDto(Guid Id, string Title, string? Hint, string Kind, bool Required, IReadOnlyList<LogbookOptionDto> Options);

public sealed record LogbookTemplateDto(int Version, DateTimeOffset? PublishedAt, IReadOnlyList<LogbookSectionDto> Sections);

public sealed record LogbookOptionInput(Guid? Id, string? Label);

/// <summary>Ids are optional: what has none (new sections and options) gets one; what keeps its id keeps the meaning of answers already given.</summary>
public sealed record LogbookSectionInput(Guid? Id, string? Title, string? Hint, string? Kind, bool Required, List<LogbookOptionInput>? Options);

public sealed record SetLogbookTemplateRequest(List<LogbookSectionInput>? Sections);

/// <summary>
/// The template of the weekly report (decision 49). Readable for everybody who fills in, reads or maintains reports (the privileges of the Berichtsheft);
/// only the holders of <see cref="PortalPrivileges.LogbookManageTemplate"/> change it. Every change of the content is a new version; version 0 is the suggestion
/// that applies until the first version is saved. Old versions stay readable.
/// </summary>
public static class LogbookTemplateEndpoints
{
    private static readonly string[] Readers = [PortalPrivileges.LogbookWrite, PortalPrivileges.LogbookReview, PortalPrivileges.LogbookManageTemplate];

    public static WebApplication MapLogbookTemplateEndpoints(this WebApplication app)
    {
        app.MapGet("/api/logbook/template", async (ClaimsPrincipal user, IPermissionCache cache, LogbookTemplateStore store, CancellationToken ct) =>
            {
                EnsureMayRead(user, cache);
                return Answer.Ok(ToDto(await store.CurrentAsync(ct)));
            })
            .RequireAuthorization();

        app.MapGet("/api/logbook/template/{version:int}", async (int version, ClaimsPrincipal user, IPermissionCache cache, LogbookTemplateStore store, CancellationToken ct) =>
            {
                EnsureMayRead(user, cache);
                return await store.GetAsync(version, ct) is { } template ? Answer.Ok(ToDto(template)) : throw ApiException.NotFound();
            })
            .RequireAuthorization();

        app.MapPut("/api/logbook/template", async (SetLogbookTemplateRequest request, ClaimsPrincipal user, LogbookTemplateStore store, TimeProvider time, CancellationToken ct) =>
            {
                var sections = Parse(request);
                var by = Guid.TryParse(user.FindFirst(AppClaimTypes.EmployeeId)?.Value, out var id) ? id : Guid.Empty;
                try
                {
                    return Answer.Ok(ToDto(await store.PublishAsync(sections, by, time.GetUtcNow(), ct)));
                }
                catch (LogbookTemplateConflictException)
                {
                    throw ApiException.Conflict("Die Vorlage wurde gleichzeitig von jemand anderem geändert. Bitte neu laden und noch einmal speichern.");
                }
            })
            .RequireAuthorization(AppPolicies.Privilege(PortalPrivileges.LogbookManageTemplate));

        return app;
    }

    private static void EnsureMayRead(ClaimsPrincipal user, IPermissionCache cache)
    {
        if (Guid.TryParse(user.FindFirst(AppClaimTypes.EmployeeId)?.Value, out var me)
            && cache.TryGet(me, out var snapshot) && snapshot is not null
            && Readers.Any(snapshot.Privileges.ContainsKey))
        {
            return;
        }

        throw new ApiException(StatusCodes.Status403Forbidden, EnvelopeAuthorizationResultHandler.Message("read"));
    }

    private static List<LogbookSection> Parse(SetLogbookTemplateRequest request)
    {
        if (request.Sections is null)
        {
            throw ApiException.Validation("sections", Validator.Required);
        }

        var errors = new List<ApiError>();
        var sections = new List<LogbookSection>();
        foreach (var input in request.Sections)
        {
            var kind = input.Kind?.Trim().ToLowerInvariant() switch
            {
                "singlechoice" => LogbookSectionKind.SingleChoice,
                "multichoice" => LogbookSectionKind.MultiChoice,
                "freetext" => LogbookSectionKind.FreeText,
                _ => LogbookSectionKind.Unknown,
            };
            sections.Add(new LogbookSection(
                input.Id is { } id && id != Guid.Empty ? id : Guid.CreateVersion7(),
                input.Title ?? string.Empty, input.Hint, kind, input.Required,
                [.. (input.Options ?? []).Select(o => new LogbookOption(o.Id is { } oid && oid != Guid.Empty ? oid : Guid.CreateVersion7(), o.Label ?? string.Empty))]));
        }

        foreach (var problem in LogbookTemplateValidator.Validate(sections))
        {
            errors.Add(new ApiError("sections", MessageOf(problem.Code)));
        }

        if (errors.Count > 0)
        {
            throw ApiException.Validation([.. errors.DistinctBy(e => e.Message)]);
        }

        return sections;
    }

    private static string MessageOf(LogbookTemplateError error) => error switch
    {
        LogbookTemplateError.NoSections => "Die Vorlage braucht mindestens einen Abschnitt.",
        LogbookTemplateError.TooManySections => $"Die Vorlage darf höchstens {LogbookLimits.MaxSections} Abschnitte haben.",
        LogbookTemplateError.IdMissing or LogbookTemplateError.DuplicateId => "Eine Id fehlt oder kommt doppelt vor.",
        LogbookTemplateError.TitleMissing => "Jeder Abschnitt braucht einen Titel.",
        LogbookTemplateError.TitleTooLong => $"Ein Titel darf höchstens {LogbookLimits.MaxTitle} Zeichen haben.",
        LogbookTemplateError.HintTooLong => $"Ein Hinweis darf höchstens {LogbookLimits.MaxHint} Zeichen haben.",
        LogbookTemplateError.UnknownKind => "Die Art eines Abschnitts muss singleChoice, multiChoice oder freeText sein.",
        LogbookTemplateError.TooFewOptions => "Eine Auswahl braucht mindestens zwei Möglichkeiten.",
        LogbookTemplateError.TooManyOptions => $"Eine Auswahl darf höchstens {LogbookLimits.MaxOptions} Möglichkeiten haben.",
        LogbookTemplateError.OptionsNotAllowed => "Ein Freitext-Abschnitt hat keine Auswahlmöglichkeiten.",
        LogbookTemplateError.OptionLabelMissing => "Jede Möglichkeit braucht einen Text.",
        LogbookTemplateError.OptionLabelTooLong => $"Der Text einer Möglichkeit darf höchstens {LogbookLimits.MaxOptionLabel} Zeichen haben.",
        LogbookTemplateError.OptionLabelNotUnique => "Die Möglichkeiten eines Abschnitts müssen sich unterscheiden.",
        _ => "Die Vorlage ist nicht gültig.",
    };

    private static LogbookTemplateDto ToDto(LogbookTemplate template) => new(
        template.Version, template.PublishedAt,
        [.. template.Sections.Select(s => new LogbookSectionDto(
            s.Id, s.Title, s.Hint,
            s.Kind switch { LogbookSectionKind.SingleChoice => "singleChoice", LogbookSectionKind.MultiChoice => "multiChoice", _ => "freeText" },
            s.Required, [.. s.Options.Select(o => new LogbookOptionDto(o.Id, o.Label))]))]);
}

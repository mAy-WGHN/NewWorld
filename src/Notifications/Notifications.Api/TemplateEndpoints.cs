using System.Security.Claims;
using Notifications.Core;
using Notifications.Core.Templates;
using Shared.Authorization;

namespace Notifications.Api;

/// <summary>
/// The texts of the notifications, maintained by HR (privilege <see cref="NotificationPrivileges.TemplateManage"/>). The files are the defaults; a change
/// overwrites only the texts, never the rules (which variables exist, which class stands where). A text that breaks a rule is refused (422 with the
/// problem), nothing about the texts is logged.
/// </summary>
public static class TemplateEndpoints
{
    public static WebApplication MapTemplateEndpoints(this WebApplication app)
    {
        var group = app.MapGroup("/api/notification-templates").RequireAuthorization(AppPolicies.Privilege(NotificationPrivileges.TemplateManage));

        group.MapGet("/", async (TemplateAdminService service, CancellationToken ct) => Results.Ok((await service.ListAsync(ct)).Select(ToDto)));

        group.MapGet("/{key}", async (string key, TemplateAdminService service, CancellationToken ct) =>
            await service.GetAsync(key, ct) is { } view ? Results.Ok(ToDto(view)) : Results.NotFound());

        group.MapPut("/{key}", async (string key, SaveTemplateRequest request, ClaimsPrincipal user, TemplateAdminService service, CancellationToken ct) =>
            Me(user) is not { } actor
                ? Results.Forbid()
                : Problem(await service.SaveAsync(key, ToTexts(request.InApp, request.Push, request.Email), actor, ct)) ?? Results.NoContent());

        group.MapDelete("/{key}", async (string key, TemplateAdminService service, CancellationToken ct) =>
            await service.ResetAsync(key, ct) == TemplateAdminError.None ? Results.NoContent() : Results.NotFound());

        group.MapPost("/{key}/preview", (string key, PreviewTemplateRequest request, TemplateAdminService service) =>
        {
            var preview = service.Preview(key, ToTexts(request.InApp, request.Push, request.Email), request.Sample);
            return Problem(preview.Result) ?? Results.Ok(ToDto(preview.Rendered!));
        });

        return app;
    }

    private static TemplateTexts ToTexts(TextDto? inApp, TextDto? push, TextDto? email) =>
        new(Pair(inApp), Pair(push), Pair(email));

    private static TextPair? Pair(TextDto? text) => text is null ? null : new TextPair(text.Title, text.Body);

    private static TextDto? ToDto(TextPair? text) => text is null ? null : new TextDto(text.Title, text.Body);

    private static TemplateTextsDto ToDto(TemplateTexts texts) => new(ToDto(texts.InApp), ToDto(texts.Push), ToDto(texts.Email));

    private static TemplateDto ToDto(TemplateView v) => new(
        v.Key, v.Mandatory, v.SensitiveText, v.MailMayContainSensitive, v.Customized, ToDto(v.Default), ToDto(v.Effective),
        [.. v.Variables.Select(x => new VariableDto(x.Name, x.Sensitive ? 2 : 1, x.FromRecipient ? "Recipient" : "Sender"))], v.ChangedAt, v.ChangedBy);

    private static IResult? Problem(TemplateSaveResult result) => result.Error switch
    {
        TemplateAdminError.None => null,
        TemplateAdminError.UnknownTemplate => Results.Problem(statusCode: StatusCodes.Status404NotFound, title: nameof(TemplateAdminError.UnknownTemplate)),
        TemplateAdminError.SensitiveTextLocked => Results.Problem(statusCode: StatusCodes.Status409Conflict, title: nameof(TemplateAdminError.SensitiveTextLocked)),
        _ => Results.Problem(statusCode: StatusCodes.Status422UnprocessableEntity, title: result.Problem.ToString()),
    };

    private static Guid? Me(ClaimsPrincipal user) => Guid.TryParse(user.FindFirst(AppClaimTypes.EmployeeId)?.Value, out var id) ? id : null;
}

using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Portal.Core;
using Portal.Infrastructure;
using Shared.Testing;
using static Portal.Api.Tests.CashRegisterTestHelpers;
using static Portal.Api.Tests.PortalApi;

namespace Portal.Api.Tests;

/// <summary>
/// Setting a seal is a form: a photo of the seal (uploaded first) and who attached it are required, the date and time are taken by the service. Lifting a
/// seal needs nothing. Who may look at the photo follows the reach of the register (like the history).
/// </summary>
public sealed class CashRegisterSealFormTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static async Task<Guid> RegisterAsync(PortalApi api, Guid store, string name = "Kasse 1")
    {
        using var response = await api.SendAsync(HttpMethod.Post, "/api/cash-registers", Hanna, new { storeId = store, name });
        response.StatusCode.ShouldBe(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        return (await DataAsync(response)).GetProperty("id").GetGuid();
    }

    private static async Task<JsonElement> ErrorOfAsync(HttpResponseMessage response, HttpStatusCode expected = HttpStatusCode.UnprocessableEntity)
    {
        response.StatusCode.ShouldBe(expected, await response.Content.ReadAsStringAsync());
        return await ErrorAsync(response);
    }

    private static string FieldOf(JsonElement error) => error.GetProperty("errors")[0].GetProperty("field").GetString()!;

    // ------------------------------------------------------------------ the form

    [Fact]
    public async Task A_seal_comes_with_the_photo_and_who_attached_it_and_the_service_takes_the_time()
    {
        await using var api = await StartAsync(postgres);
        var id = await RegisterAsync(api, StoreA);
        var photo = (await TryPhotoAsync(api, Hanna))!.Value;

        using var response = await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{id}/seal", Hanna, new { photoId = photo, sealedBy = Erika });

        response.StatusCode.ShouldBe(HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var data = await DataAsync(response);
        data.GetProperty("sealed").GetBoolean().ShouldBeTrue();
        data.GetProperty("sealPhotoId").GetGuid().ShouldBe(photo);
        data.GetProperty("sealAttachedBy").GetString().ShouldBe("EM");
        data.GetProperty("sealAttachedByName").GetString().ShouldBe("Erika Mustermann");
        data.GetProperty("stateChangedAt").GetDateTimeOffset().ShouldBe(Start); // taken by the service, not by the form
    }

    [Fact]
    public async Task Without_the_photo_or_without_the_person_there_is_no_seal_and_both_are_named()
    {
        await using var api = await StartAsync(postgres);
        var id = await RegisterAsync(api, StoreA);

        using var neither = await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{id}/seal", Hanna, new { });
        var error = await ErrorOfAsync(neither);
        error.GetProperty("errors").EnumerateArray().Select(e => e.GetProperty("field").GetString()).ShouldBe(["photoId", "sealedBy"], ignoreOrder: true);

        using var noPhoto = await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{id}/seal", Hanna, new { sealedBy = Hanna });
        FieldOf(await ErrorOfAsync(noPhoto)).ShouldBe("photoId");
        var photo = (await TryPhotoAsync(api, Hanna))!.Value;
        using var noPerson = await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{id}/seal", Hanna, new { photoId = photo });
        FieldOf(await ErrorOfAsync(noPerson)).ShouldBe("sealedBy");

        using var list = await api.SendAsync(HttpMethod.Get, "/api/cash-registers", Hanna);
        (await DataAsync(list)).GetProperty("items")[0].GetProperty("sealed").GetBoolean().ShouldBeFalse();
    }

    [Fact]
    public async Task An_unknown_person_or_photo_a_foreign_photo_and_a_used_photo_are_refused()
    {
        await using var api = await StartAsync(postgres);
        var id = await RegisterAsync(api, StoreA);
        var other = await RegisterAsync(api, StoreB);
        api.Grant(Erika, new TestGrant(PortalPrivileges.CashRegisterRecord, Unrestricted: true));
        var photo = (await TryPhotoAsync(api, Hanna))!.Value;

        using var unknownPerson = await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{id}/seal", Hanna, new { photoId = photo, sealedBy = Guid.NewGuid() });
        FieldOf(await ErrorOfAsync(unknownPerson)).ShouldBe("sealedBy");
        using var unknownPhoto = await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{id}/seal", Hanna, new { photoId = Guid.NewGuid(), sealedBy = Hanna });
        FieldOf(await ErrorOfAsync(unknownPhoto)).ShouldBe("photoId");
        using var foreignPhoto = await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{id}/seal", Erika, new { photoId = photo, sealedBy = Erika });
        FieldOf(await ErrorOfAsync(foreignPhoto)).ShouldBe("photoId"); // the photo is Hanna's

        using var fine = await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{id}/seal", Hanna, new { photoId = photo, sealedBy = Hanna });
        fine.StatusCode.ShouldBe(HttpStatusCode.OK);
        using var reused = await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{other}/seal", Hanna, new { photoId = photo, sealedBy = Hanna });
        FieldOf(await ErrorOfAsync(reused)).ShouldBe("photoId"); // one photo, one seal
    }

    [Fact]
    public async Task A_register_recorded_as_sealed_and_an_edit_into_sealed_need_the_form_too()
    {
        await using var api = await StartAsync(postgres);
        var id = await RegisterAsync(api, StoreA);

        using var create = await api.SendAsync(HttpMethod.Post, "/api/cash-registers", Hanna, new { storeId = StoreB, name = "Neu", @sealed = true });
        FieldOf(await ErrorOfAsync(create)).ShouldBe("photoId");
        using var edit = await api.SendAsync(HttpMethod.Put, $"/api/cash-registers/{id}", Hanna, new { storeId = StoreA, name = "Kasse 1", @sealed = true });
        FieldOf(await ErrorOfAsync(edit)).ShouldBe("photoId");

        using var list = await api.SendAsync(HttpMethod.Get, "/api/cash-registers", Hanna);
        (await DataAsync(list)).GetProperty("items").GetArrayLength().ShouldBe(1); // the refused create left nothing behind
    }

    [Fact]
    public async Task Editing_a_register_that_is_sealed_already_needs_no_new_form_and_lifting_needs_none()
    {
        await using var api = await StartAsync(postgres);
        var id = await RegisterAsync(api, StoreA);
        (await SealAsync(api, id, Hanna, Erika)).Dispose();

        using var keep = await api.SendAsync(HttpMethod.Put, $"/api/cash-registers/{id}", Hanna, new { storeId = StoreA, name = "Kasse eins", @sealed = true });
        keep.StatusCode.ShouldBe(HttpStatusCode.OK, await keep.Content.ReadAsStringAsync());
        (await DataAsync(keep)).GetProperty("sealAttachedByName").GetString().ShouldBe("Erika Mustermann"); // the seal is the same

        using var lift = await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{id}/unseal", Hanna);
        var lifted = await DataAsync(lift);
        lifted.GetProperty("sealed").GetBoolean().ShouldBeFalse();
        lifted.GetProperty("sealPhotoId").ValueKind.ShouldBe(JsonValueKind.Null);
        lifted.GetProperty("sealAttachedBy").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    [Fact]
    public async Task The_history_shows_photo_and_person_of_every_seal_and_a_register_can_be_sealed_in_one_step_when_recorded()
    {
        await using var api = await StartAsync(postgres);
        using var create = await api.SendAsync(HttpMethod.Post, "/api/cash-registers", Hanna, await SealedBodyAsync(api, Hanna, StoreA, "Kasse 1", Erika));
        create.StatusCode.ShouldBe(HttpStatusCode.Created, await create.Content.ReadAsStringAsync());
        var photo = (await DataAsync(create)).GetProperty("sealPhotoId").GetGuid();

        using var history = await api.SendAsync(HttpMethod.Get, $"/api/cash-registers/history?storeId={StoreA}", Hanna);
        var events = (await DataAsync(history)).EnumerateArray().ToList();
        events.Select(e => e.GetProperty("kind").GetString()).ShouldBe(["sealed", "recorded"]);
        events[0].GetProperty("photoId").GetGuid().ShouldBe(photo);
        events[0].GetProperty("attachedBy").GetString().ShouldBe("EM");
        events[0].GetProperty("attachedByName").GetString().ShouldBe("Erika Mustermann");
        events[1].GetProperty("photoId").ValueKind.ShouldBe(JsonValueKind.Null);
    }

    // ------------------------------------------------------------------ the photo

    [Fact]
    public async Task Only_a_picture_up_to_5_MB_is_taken_and_only_by_someone_who_may_record()
    {
        await using var api = await StartAsync(postgres);

        using var text = await api.UploadImageAsync(Hanna, "kein Bild"u8.ToArray(), "siegel.png", "/api/cash-registers/photos");
        using var tooBig = await api.UploadImageAsync(Hanna, [.. Png, .. new byte[5 * 1024 * 1024]], "siegel.png", "/api/cash-registers/photos");
        using var nobody = await api.UploadImageAsync(Ben, Png, "siegel.png", "/api/cash-registers/photos");
        using var anonymous = await api.UploadImageAsync(null, Png, "siegel.png", "/api/cash-registers/photos");
        using var fine = await api.UploadImageAsync(Hanna, Png, "siegel.png", "/api/cash-registers/photos");

        text.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        tooBig.StatusCode.ShouldBe(HttpStatusCode.UnprocessableEntity);
        nobody.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        fine.StatusCode.ShouldBe(HttpStatusCode.Created);
    }

    [Fact]
    public async Task The_photo_of_a_seal_can_be_looked_at_by_those_who_reach_the_register_only()
    {
        await using var api = await StartAsync(postgres);
        var id = await RegisterAsync(api, StoreA);
        var photo = (await TryPhotoAsync(api, Hanna))!.Value;
        (await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{id}/seal", Hanna, new { photoId = photo, sealedBy = Hanna })).Dispose();
        api.Grant(Erika, new TestGrant(PortalPrivileges.CashRegisterRead, StoreIds: [StoreA]));
        api.Grant(Ben, new TestGrant(PortalPrivileges.CashRegisterRead, StoreIds: [StoreB]));

        using var inReach = await api.SendAsync(HttpMethod.Get, $"/api/cash-registers/photos/{photo}", Erika);
        using var outOfReach = await api.SendAsync(HttpMethod.Get, $"/api/cash-registers/photos/{photo}", Ben);
        using var nobody = await api.SendAsync(HttpMethod.Get, $"/api/cash-registers/photos/{photo}", Revisor);
        using var anonymous = await api.SendAsync(HttpMethod.Get, $"/api/cash-registers/photos/{photo}", null);
        using var unknown = await api.SendAsync(HttpMethod.Get, $"/api/cash-registers/photos/{Guid.NewGuid()}", Hanna);

        inReach.StatusCode.ShouldBe(HttpStatusCode.OK);
        inReach.Content.Headers.ContentType!.MediaType.ShouldBe("image/png");
        (await inReach.Content.ReadAsByteArrayAsync()).ShouldBe(Png);
        outOfReach.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        nobody.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        unknown.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task A_photo_that_no_seal_uses_yet_is_private_to_who_uploaded_it()
    {
        await using var api = await StartAsync(postgres);
        var photo = (await TryPhotoAsync(api, Hanna))!.Value;
        api.Grant(Erika, new TestGrant(PortalPrivileges.CashRegisterRead, Unrestricted: true));

        using var own = await api.SendAsync(HttpMethod.Get, $"/api/cash-registers/photos/{photo}", Hanna);
        using var other = await api.SendAsync(HttpMethod.Get, $"/api/cash-registers/photos/{photo}", Erika);

        own.StatusCode.ShouldBe(HttpStatusCode.OK);
        other.StatusCode.ShouldBe(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task The_history_keeps_the_photo_of_a_deleted_register_for_those_who_reach_the_store()
    {
        await using var api = await StartAsync(postgres);
        var id = await RegisterAsync(api, StoreA);
        var photo = (await TryPhotoAsync(api, Hanna))!.Value;
        (await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{id}/seal", Hanna, new { photoId = photo, sealedBy = Hanna })).Dispose();
        (await api.SendAsync(HttpMethod.Delete, $"/api/cash-registers/{id}", Hanna)).Dispose();

        using var view = await api.SendAsync(HttpMethod.Get, $"/api/cash-registers/photos/{photo}", Hanna);

        view.StatusCode.ShouldBe(HttpStatusCode.OK);
    }

    // ------------------------------------------------------------------ who attached the seal: the people to choose from

    [Fact]
    public async Task The_people_search_finds_by_name_from_two_letters_and_is_for_those_who_record()
    {
        await using var api = await StartAsync(postgres);

        using var found = await api.SendAsync(HttpMethod.Get, "/api/cash-registers/people?query=erik", Hanna);
        using var tooShort = await api.SendAsync(HttpMethod.Get, "/api/cash-registers/people?query=e", Hanna);
        using var percent = await api.SendAsync(HttpMethod.Get, "/api/cash-registers/people?query=%25%25", Hanna);
        using var nobody = await api.SendAsync(HttpMethod.Get, "/api/cash-registers/people?query=erik", Ben);
        using var anonymous = await api.SendAsync(HttpMethod.Get, "/api/cash-registers/people?query=erik", null);

        (await DataAsync(found)).EnumerateArray().Select(p => p.GetProperty("name").GetString()).ShouldBe(["Erika Mustermann"]);
        (await DataAsync(tooShort)).GetArrayLength().ShouldBe(0);
        (await DataAsync(percent)).GetArrayLength().ShouldBe(0); // a wildcard is searched for as text
        nobody.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
        anonymous.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }
}

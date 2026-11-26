using System.Net;

namespace Portal.Api.Tests;

/// <summary>Setting a seal is a form: a photo (uploaded first by the same person) and who attached the seal. This does both steps.</summary>
internal static class CashRegisterTestHelpers
{
    /// <summary>Uploads a photo as the person; <c>null</c> if the person may not (no privilege).</summary>
    public static async Task<Guid?> TryPhotoAsync(PortalApi api, Guid person)
    {
        using var response = await api.UploadImageAsync(person, PortalApi.Png, "siegel.png", "/api/cash-registers/photos");
        return response.StatusCode == HttpStatusCode.Created ? (await PortalApi.DataAsync(response)).GetProperty("id").GetGuid() : null;
    }

    /// <summary>The body of the form: a fresh photo uploaded as the person, and by default the person is also the one who attached the seal.</summary>
    public static async Task<object> FormAsync(PortalApi api, Guid? person, Guid? sealedBy = null)
    {
        var photo = person is { } p ? await TryPhotoAsync(api, p) : null;
        return new { photoId = photo ?? Guid.NewGuid(), sealedBy = sealedBy ?? person ?? PortalApi.Hanna };
    }

    /// <summary>Sets (renews) the seal with the form; without a photo of their own a person gets a made-up id.</summary>
    public static async Task<HttpResponseMessage> SealAsync(PortalApi api, Guid id, Guid? person, Guid? sealedBy = null) =>
        await api.SendAsync(HttpMethod.Post, $"/api/cash-registers/{id}/seal", person, await FormAsync(api, person, sealedBy));


    /// <summary>The body of "create" or "edit" with the seal state sealed and its form.</summary>
    public static async Task<object> SealedBodyAsync(PortalApi api, Guid person, Guid storeId, string name, Guid? sealedBy = null)
    {
        var photo = await TryPhotoAsync(api, person) ?? Guid.NewGuid();
        return new { storeId, name, @sealed = true, photoId = photo, sealedBy = sealedBy ?? person };
    }
}

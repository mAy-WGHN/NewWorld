using System.Text.Json;
using Portal.Authentication;
using Portal.Common.Api;
using Portal.Serialization;
using Portal.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Portal.Tests.Unit;

public class ApiExceptionHandlerTests
{
    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "test";
        public string ContentRootPath { get; set; } = "/";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static async Task<(int Status, JsonElement Body)> HandleAsync(Exception exception, string environment)
    {
        var services = new ServiceCollection();
        services.ConfigureHttpJsonOptions(o => ApiJson.Configure(o.SerializerOptions));
        var context = new DefaultHttpContext { RequestServices = services.BuildServiceProvider() };
        context.Response.Body = new MemoryStream();

        var handler = new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance, new FakeEnvironment(environment));
        Assert.True(await handler.TryHandleAsync(context, exception, CancellationToken.None));

        context.Response.Body.Position = 0;
        var body = await JsonDocument.ParseAsync(context.Response.Body);
        return (context.Response.StatusCode, body.RootElement.Clone());
    }

    [Fact]
    public async Task ApiException_IsTranslatedIntoTheEnvelope()
    {
        var (status, body) = await HandleAsync(ApiException.Conflict("Schon vorhanden."), "Production");

        Assert.Equal(409, status);
        Assert.False(body.GetProperty("success").GetBoolean());
        Assert.Equal("Schon vorhanden.", body.GetProperty("message").GetString());
        Assert.Equal(0, body.GetProperty("errors").GetArrayLength());
    }

    [Fact]
    public async Task ApiException_KeepsFieldErrors()
    {
        var (status, body) = await HandleAsync(ApiException.Validation("title", "Zu lang."), "Production");

        Assert.Equal(422, status);
        var error = body.GetProperty("errors")[0];
        Assert.Equal("title", error.GetProperty("field").GetString());
        Assert.Equal("Zu lang.", error.GetProperty("message").GetString());
    }

    [Fact]
    public async Task UnexpectedException_HidesDetailsInProduction()
    {
        var (status, body) = await HandleAsync(new InvalidOperationException("geheime Interna"), "Production");

        Assert.Equal(500, status);
        Assert.Equal("Es ist ein interner Fehler aufgetreten.", body.GetProperty("message").GetString());
        Assert.DoesNotContain("geheime Interna", body.ToString());
        Assert.DoesNotContain("InvalidOperationException", body.ToString());
    }

    [Fact]
    public async Task UnexpectedException_ShowsClassAndMessageInDevelopment()
    {
        var (status, body) = await HandleAsync(new InvalidOperationException("boom"), "Development");

        Assert.Equal(500, status);
        Assert.Equal("InvalidOperationException: boom", body.GetProperty("message").GetString());
    }

    [Fact]
    public void NotFound_WithoutMessage_UsesTheFriendlyDefault()
    {
        Assert.Contains("möglicherweise wurde er in der Zwischenzeit gelöscht", ApiException.NotFound().Message);
    }
}

public class ApiJsonTests
{
    private static JsonSerializerOptions Options()
    {
        var options = new JsonSerializerOptions();
        ApiJson.Configure(options);
        return options;
    }

    private record Sample(DateTime At, DateTime? Maybe, Portal.Domain.Enums.ReportSeverity Severity, int PersonnelNumber);

    [Fact]
    public void Serialize_UsesCamelCase_LowercaseEnums_AndIsoTimestampsWithOffset()
    {
        var json = JsonSerializer.Serialize(
            new Sample(new DateTime(2026, 10, 12, 14, 3, 22, DateTimeKind.Utc), null, Portal.Domain.Enums.ReportSeverity.Critical, 7),
            Options());

        Assert.Contains("\"at\":\"2026-10-12T14:03:22+00:00\"", json);
        Assert.Contains("\"maybe\":null", json);
        Assert.Contains("\"severity\":\"critical\"", json);
        Assert.Contains("\"personnelNumber\":7", json);
    }

    [Fact]
    public void Serialize_TreatsUnspecifiedKindAsUtc()
    {
        var json = JsonSerializer.Serialize(new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Unspecified), Options());

        Assert.Equal("\"2026-01-02T03:04:05+00:00\"", json);
    }

    [Fact]
    public void Deserialize_KeepsTheClientWallClock_WithoutConvertingTheOffset()
    {
        var value = JsonSerializer.Deserialize<DateTime>("\"2026-03-05T10:00:00+02:00\"", Options());

        Assert.Equal(new DateTime(2026, 3, 5, 10, 0, 0), value);
        Assert.Equal(DateTimeKind.Utc, value.Kind);
    }

    [Fact]
    public void Deserialize_WithoutOffset_IsTakenAsIs()
    {
        var value = JsonSerializer.Deserialize<DateTime>("\"2026-03-05T10:00:00\"", Options());

        Assert.Equal(new DateTime(2026, 3, 5, 10, 0, 0), value);
    }

    [Fact]
    public void Deserialize_OfGarbage_Throws()
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<DateTime>("\"morgen\"", Options()));
    }
}

public class FaqImageStorageTests
{
    [Fact]
    public void DetectExtension_RecognizesPngJpegAndWebp_ByMagicBytes()
    {
        Assert.Equal("png", FaqImageStorage.DetectExtension([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0]));
        Assert.Equal("jpg", FaqImageStorage.DetectExtension([0xFF, 0xD8, 0xFF, 0xE0]));
        Assert.Equal("webp", FaqImageStorage.DetectExtension([.. "RIFF"u8, 1, 2, 3, 4, .. "WEBP"u8]));
    }

    [Fact]
    public void DetectExtension_RejectsEverythingElse()
    {
        Assert.Null(FaqImageStorage.DetectExtension("GIF89a......"u8));
        Assert.Null(FaqImageStorage.DetectExtension("<svg></svg>"u8));
        Assert.Null(FaqImageStorage.DetectExtension([.. "RIFF"u8, 1, 2, 3, 4, .. "WAVE"u8]));
        Assert.Null(FaqImageStorage.DetectExtension([0x89, 0x50]));
        Assert.Null(FaqImageStorage.DetectExtension([]));
    }

    [Theory]
    [InlineData("png", "image/png")]
    [InlineData("jpg", "image/jpeg")]
    [InlineData("webp", "image/webp")]
    [InlineData("bin", "application/octet-stream")]
    public void ContentType_MatchesTheExtension(string extension, string expected)
    {
        Assert.Equal(expected, FaqImageStorage.ContentType(extension));
    }
}

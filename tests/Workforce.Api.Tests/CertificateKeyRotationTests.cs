using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Shared.Testing;
using Workforce.Core.Certificates;
using Workforce.Infrastructure;
using Workforce.Infrastructure.Certificates;
using static Workforce.Api.Tests.WorkforceApi;

namespace Workforce.Api.Tests;

/// <summary>Key rotation of the encrypted certificates: the key ring, the old format, and the maintenance that encrypts files anew under the current key.</summary>
public sealed class CertificateKeyRotationTests(PostgresFixture postgres) : IClassFixture<PostgresFixture>
{
    private static readonly byte[] Pdf = Encoding.ASCII.GetBytes("%PDF-1.7 the certificate of Erika");
    private static readonly Guid Document = Guid.Parse("00000000-0000-0000-0000-0000000000d0");

    private static byte[] NewKey() => RandomNumberGenerator.GetBytes(32);

    private static Dictionary<byte, byte[]> Ring(byte id, byte[] key) => new Dictionary<byte, byte[]> { [id] = key };

    /// <summary>A file in the format from before the key ring: version 1, nonce, tag, ciphertext.</summary>
    private static byte[] LegacyFile(byte[] key, byte[] content, Guid documentId)
    {
        var result = new byte[1 + 12 + 16 + content.Length];
        result[0] = 1;
        RandomNumberGenerator.Fill(result.AsSpan(1, 12));
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(result.AsSpan(1, 12), content, result.AsSpan(29), result.AsSpan(13, 16), documentId.ToByteArray());
        return result;
    }

    // ---------------------------------------------------------------- the key ring

    [Fact]
    public void A_file_carries_the_id_of_its_key_and_an_older_key_still_opens_it()
    {
        var oldKey = NewKey();
        var oldProtector = new CertificateFileProtector(1, oldKey, new Dictionary<byte, byte[]>());
        var file = oldProtector.Protect(Pdf, Document);
        file[0].ShouldBe((byte)2);
        file[1].ShouldBe((byte)1);

        var rotated = new CertificateFileProtector(2, NewKey(), Ring(1, oldKey));

        rotated.Unprotect(file, Document).ShouldBe(Pdf);
        rotated.NeedsRotation(file).ShouldBeTrue();
        var again = rotated.Protect(Pdf, Document);
        again[1].ShouldBe((byte)2);
        rotated.NeedsRotation(again).ShouldBeFalse();
        rotated.HasPreviousKeys.ShouldBeTrue();
        oldProtector.HasPreviousKeys.ShouldBeFalse();
    }

    [Fact]
    public void A_file_under_a_key_that_is_not_configured_cannot_be_opened_and_a_file_cannot_be_moved_to_another_document()
    {
        var protector = new CertificateFileProtector(1, NewKey(), new Dictionary<byte, byte[]>());
        var file = protector.Protect(Pdf, Document);

        Should.Throw<CryptographicException>(() => new CertificateFileProtector(2, NewKey(), new Dictionary<byte, byte[]>()).Unprotect(file, Document));
        Should.Throw<CryptographicException>(() => protector.Unprotect(file, Guid.NewGuid()));
        Should.Throw<CryptographicException>(() => protector.Unprotect([2, 1, 0, 0], Document));
    }

    [Fact]
    public void The_old_format_without_a_key_id_is_read_with_the_current_or_an_older_key_and_counts_as_to_be_rotated()
    {
        var oldKey = NewKey();
        var legacy = LegacyFile(oldKey, Pdf, Document);

        var single = new CertificateFileProtector(oldKey);
        var ring = new CertificateFileProtector(2, NewKey(), Ring(1, oldKey));

        single.Unprotect(legacy, Document).ShouldBe(Pdf);
        ring.Unprotect(legacy, Document).ShouldBe(Pdf);
        ring.NeedsRotation(legacy).ShouldBeTrue();
        single.NeedsRotation(legacy).ShouldBeTrue(); // not yet in the new format
        Should.Throw<CryptographicException>(() => new CertificateFileProtector(NewKey()).Unprotect(legacy, Document));
    }

    [Fact]
    public void A_previous_key_with_the_id_of_the_current_key_or_with_a_wrong_length_is_refused()
    {
        var key = NewKey();

        Should.Throw<ArgumentException>(() => new CertificateFileProtector(1, key, Ring(1, NewKey())));
        Should.Throw<ArgumentException>(() => new CertificateFileProtector(1, key, Ring(2, new byte[16])));
        Should.Throw<ArgumentException>(() => new CertificateFileProtector(new byte[31]));
    }

    [Fact]
    public void The_configuration_names_only_the_setting_when_a_key_is_wrong()
    {
        var ex = Should.Throw<InvalidOperationException>(() => CertificateFileProtector.FromConfiguration("not base64 at all!", 1, new Dictionary<byte, string>(), "Workforce:Certificates:EncryptionKey"));

        ex.Message.ShouldContain("Workforce:Certificates:EncryptionKey");
        ex.Message.ShouldNotContain("not base64");
    }

    // ---------------------------------------------------------------- the maintenance

    [Fact]
    public async Task The_maintenance_encrypts_files_anew_under_the_current_key_and_the_document_stays_readable()
    {
        var oldKey = NewKey();
        var newKey = NewKey();
        await using var api = await StartAsync(postgres, services =>
        {
            services.RemoveAll<CertificateFileProtector>();
            services.AddSingleton(new CertificateFileProtector(1, oldKey, new Dictionary<byte, byte[]>()));
        });
        var absence = await api.PostAbsenceAsync("/api/me/absences/sick", Erika, new ReportSickRequest(new DateOnly(2026, 10, 23), new DateOnly(2026, 10, 25), "First"));
        var document = await api.UploadOkAsync($"/api/me/absences/{absence.Id}/certificates", Erika, Pdf, "2026-10-23", "2026-10-25");
        var storage = api.Services.GetRequiredService<ICertificateFileStorage>();
        var storageId = await StorageIdAsync(api, document.Id);
        var before = (await storage.ReadAsync(storageId, CancellationToken.None))!;
        before[1].ShouldBe((byte)1);

        // The ring after the key was replaced: new current key, the old one only for reading.
        var rotated = new CertificateFileProtector(2, newKey, Ring(1, oldKey));
        using var scope = api.Services.CreateScope();
        var store = new EfCertificateStore(scope.ServiceProvider.GetRequiredService<WorkforceDbContext>(), rotated, storage, new CertificateFileCleanup(storage));

        (await store.RotateKeysAsync(10, CancellationToken.None)).ShouldBe((1, 0));

        var after = (await storage.ReadAsync(storageId, CancellationToken.None))!;
        after[1].ShouldBe((byte)2);
        after.ShouldNotBe(before);
        (await store.ReadContentAsync((await store.GetAsync(document.Id, CancellationToken.None))!, CancellationToken.None)).ShouldBe(Pdf);
        (await store.RotateKeysAsync(10, CancellationToken.None)).ShouldBe((0, 0)); // nothing left to do
    }

    [Fact]
    public async Task A_round_encrypts_at_most_the_batch_and_reports_what_is_left_and_a_file_no_key_opens_stays_in_the_count()
    {
        var oldKey = NewKey();
        await using var api = await StartAsync(postgres, services =>
        {
            services.RemoveAll<CertificateFileProtector>();
            services.AddSingleton(new CertificateFileProtector(1, oldKey, new Dictionary<byte, byte[]>()));
        });
        var first = await UploadAsync(api, new DateOnly(2026, 10, 23));
        var second = await UploadAsync(api, new DateOnly(2026, 10, 26));
        var storage = api.Services.GetRequiredService<ICertificateFileStorage>();
        using var scope = api.Services.CreateScope();
        var rotated = new CertificateFileProtector(2, NewKey(), Ring(1, oldKey));
        var store = new EfCertificateStore(scope.ServiceProvider.GetRequiredService<WorkforceDbContext>(), rotated, storage, new CertificateFileCleanup(storage));

        (await store.RotateKeysAsync(1, CancellationToken.None)).ShouldBe((1, 1));
        (await store.RotateKeysAsync(1, CancellationToken.None)).ShouldBe((1, 0));

        // A file under a key that is gone is not touched and is counted as still to do: the old key must not be removed yet.
        var unknown = new CertificateFileProtector(9, NewKey(), new Dictionary<byte, byte[]>()).Protect(Pdf, first);
        await storage.ReplaceAsync(await StorageIdAsync(api, first), unknown, CancellationToken.None);
        (await store.RotateKeysAsync(10, CancellationToken.None)).ShouldBe((0, 1));
        (await storage.ReadAsync(await StorageIdAsync(api, first), CancellationToken.None))!.ShouldBe(unknown);
        _ = second;
    }

    [Fact]
    public async Task With_a_single_key_there_is_nothing_to_rotate_and_the_maintenance_round_runs_as_before()
    {
        await using var api = await StartAsync(postgres);
        await UploadAsync(api, new DateOnly(2026, 10, 23));
        var storage = api.Services.GetRequiredService<ICertificateFileStorage>();
        using var scope = api.Services.CreateScope();
        var store = new EfCertificateStore(scope.ServiceProvider.GetRequiredService<WorkforceDbContext>(), api.Services.GetRequiredService<CertificateFileProtector>(), storage, new CertificateFileCleanup(storage));

        (await store.RotateKeysAsync(10, CancellationToken.None)).ShouldBe((0, 0));
        await api.Services.GetServices<IHostedService>().OfType<CertificateMaintenanceService>().Single().RunOnceAsync(CancellationToken.None);
        (await storage.ListAsync(CancellationToken.None)).Count.ShouldBe(1);
    }

    private static async Task<Guid> UploadAsync(WorkforceApi api, DateOnly from)
    {
        var absence = await api.PostAbsenceAsync("/api/me/absences/sick", Erika, new ReportSickRequest(from, from.AddDays(2), "First"));
        var document = await api.UploadOkAsync($"/api/me/absences/{absence.Id}/certificates", Erika, Pdf, from.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture), from.AddDays(2).ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        return document.Id;
    }

    private static async Task<Guid> StorageIdAsync(WorkforceApi api, Guid documentId)
    {
        using var scope = api.Services.CreateScope();
        return (await scope.ServiceProvider.GetRequiredService<WorkforceDbContext>().CertificateDocuments.AsNoTracking().SingleAsync(d => d.Id == documentId)).StorageId;
    }
}

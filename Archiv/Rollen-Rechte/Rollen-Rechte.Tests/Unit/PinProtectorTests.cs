using Rollen_Rechte.Authentication;
using Microsoft.Extensions.Options;

namespace Rollen_Rechte.Tests.Unit;

public class PinProtectorTests
{
    private static string Key(byte seed) => Convert.ToBase64String(new byte[32].Select((_, i) => (byte)(i + seed)).ToArray());

    private static PinProtector Create(string? key) =>
        new(Options.Create(new AuthOptions { PinEncryptionKey = key }));

    [Fact]
    public void Protect_RoundTrips_AndIsRandomized()
    {
        var protector = Create(Key(1));

        var a = protector.Protect("4711");
        var b = protector.Protect("4711");

        Assert.NotEqual(a, b);
        Assert.DoesNotContain("4711", a);
        Assert.Equal("4711", protector.Unprotect(a));
        Assert.Equal("4711", protector.Unprotect(b));
    }

    [Fact]
    public void Unprotect_ReturnsNull_ForWrongKeyTamperedOrGarbageValues()
    {
        var value = Create(Key(1)).Protect("4711");
        var bytes = Convert.FromBase64String(value);
        bytes[^1] ^= 1;

        Assert.Null(Create(Key(2)).Unprotect(value));
        Assert.Null(Create(Key(1)).Unprotect(Convert.ToBase64String(bytes)));
        Assert.Null(Create(Key(1)).Unprotect("kein-base64!"));
        Assert.Null(Create(Key(1)).Unprotect(""));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("zu-kurz")]
    [InlineData("AAAA")]
    public void WithoutAValidKey_IsNotConfigured(string? key)
    {
        var protector = Create(key);

        Assert.False(protector.IsConfigured);
        Assert.Throws<InvalidOperationException>(() => protector.Protect("4711"));
        Assert.Null(protector.Unprotect("AAAA"));
    }
}

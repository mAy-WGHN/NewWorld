using System.Security.Cryptography;
using Access.Core.Login;

namespace Access.Core.Tests;

public class PinCryptoTests
{
    private static readonly string Key = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void Hash_verifies_the_same_pin_and_not_another()
    {
        var hasher = new PinHasher(iterations: 1000);

        var hash = hasher.Hash("4711");

        PinHasher.Verify("4711", hash).ShouldBeTrue();
        PinHasher.Verify("4712", hash).ShouldBeFalse();
        hash.ShouldNotContain("4711");
    }

    [Fact]
    public void Hashes_of_the_same_pin_differ_by_salt() =>
        new PinHasher(1000).Hash("4711").ShouldNotBe(new PinHasher(1000).Hash("4711"));

    [Theory]
    [InlineData("")]
    [InlineData("garbage")]
    [InlineData("pbkdf2-sha512$x$y$z")]
    public void Malformed_hash_never_verifies(string hash) => PinHasher.Verify("4711", hash).ShouldBeFalse();

    [Fact]
    public void Verification_uses_the_iterations_stored_in_the_hash()
    {
        var old = new PinHasher(1000).Hash("4711");

        PinHasher.Verify("4711", old).ShouldBeTrue();
        new PinHasher(5000).NeedsRehash(old).ShouldBeTrue();
        new PinHasher(1000).NeedsRehash(old).ShouldBeFalse();
    }

    [Fact]
    public void Protector_round_trips_and_hides_the_pin()
    {
        var protector = new PinProtector(Key);

        var cipher = protector.Protect("4711");

        cipher.ShouldNotContain("4711");
        protector.Unprotect(cipher).ShouldBe("4711");
        protector.Protect("4711").ShouldNotBe(cipher); // fresh nonce
    }

    [Fact]
    public void Wrong_key_or_manipulated_value_yields_null()
    {
        var cipher = new PinProtector(Key).Protect("4711");
        var tampered = Convert.ToBase64String(Convert.FromBase64String(cipher).Select((b, i) => i == 20 ? (byte)(b ^ 1) : b).ToArray());

        new PinProtector(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))).Unprotect(cipher).ShouldBeNull();
        new PinProtector(Key).Unprotect(tampered).ShouldBeNull();
        new PinProtector(Key).Unprotect("not base64!").ShouldBeNull();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("c2hvcnQ=")]
    public void Missing_or_short_key_means_not_configured(string? key) => new PinProtector(key).IsConfigured.ShouldBeFalse();
}

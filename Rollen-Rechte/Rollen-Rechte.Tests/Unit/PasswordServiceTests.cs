using Rollen_Rechte.Authentication;
using Microsoft.Extensions.Configuration;

namespace Rollen_Rechte.Tests.Unit;

public class PasswordServiceTests
{
    // Echte Hashes aus dem Symfony-Backend (PHP password_hash), Passwort "geheim-123".
    private const string PhpArgon2Id = "$argon2id$v=19$m=65536,t=4,p=1$RzBDa3ZJcUhHckEvVkVtaw$8NG0lM8RPTj848D7G9+CS6Kl1TEVKQFIi5XcyULNaU0";
    private const string PhpBcrypt = "$2y$04$4.8UDkJ.19IX4NzPvURa0u1h4DuU1o631VbdQ/dlO3QbvI1XEQfNe";
    private const string Password = "geheim-123";

    private static PasswordService Create(int cost = 4) =>
        new(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Auth:BcryptCost"] = cost.ToString() }).Build());

    [Fact]
    public void Hash_ProducesBcrypt_AndVerifiesRoundTrip()
    {
        var service = Create();

        var hash = service.Hash(Password);

        Assert.StartsWith("$2", hash);
        Assert.NotEqual(Password, hash);
        Assert.True(service.Verify(Password, hash));
        Assert.False(service.Verify("falsch", hash));
    }

    [Fact]
    public void Hash_IsSaltedPerCall()
    {
        var service = Create();

        Assert.NotEqual(service.Hash(Password), service.Hash(Password));
    }

    [Fact]
    public void Hash_HonoursTheConfiguredBcryptCost()
    {
        Assert.StartsWith("$2a$04$", Create(4).Hash(Password));
        Assert.StartsWith("$2a$06$", Create(6).Hash(Password));
    }

    [Fact]
    public void Verify_AcceptsExistingSymfonyBcryptHashes()
    {
        var service = Create();

        Assert.True(service.Verify(Password, PhpBcrypt));
        Assert.False(service.Verify("falsch", PhpBcrypt));
    }

    [Fact]
    public void Verify_AcceptsExistingSymfonyArgon2IdHashes()
    {
        var service = Create();

        Assert.True(service.Verify(Password, PhpArgon2Id));
        Assert.False(service.Verify("falsch", PhpArgon2Id));
        Assert.False(service.Verify(Password + " ", PhpArgon2Id));
    }

    [Theory]
    [InlineData("")]
    [InlineData("kein-hash")]
    [InlineData("$argon2id$kaputt")]
    [InlineData("$argon2id$v=19$m=x,t=y,p=z$AAAA$BBBB")]
    public void Verify_WithMalformedHash_ReturnsFalseInsteadOfThrowing(string hash)
    {
        Assert.False(Create().Verify(Password, hash));
    }

    [Fact]
    public void Verify_WithNoHash_ReturnsFalse_ForUnknownUsers()
    {
        Assert.False(Create().Verify(Password, null));
    }
}

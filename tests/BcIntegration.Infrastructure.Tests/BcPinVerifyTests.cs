using BcIntegration.Core.Errors;
using BcIntegration.Core.Login;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BcIntegration.Infrastructure.Tests;

/// <summary>The PIN goes to BC only, in the body of a request that is never logged; BC compares it and answers yes or no.</summary>
public sealed class BcPinVerifyTests : IDisposable
{
    private readonly LogCapture _logs = new();

    public void Dispose() => _logs.Dispose();

    private BcTestHost Start(string scenario) => BcTestHost.Start(scenario, configure: s => s.AddLogging(b => b.AddProvider(_logs)));

    private static IBcPinVerifier Verifier(BcTestHost bc) => bc.Services.GetRequiredService<IBcPinVerifier>();

    [Fact]
    public async Task Valid_pin_is_authenticated_with_the_name()
    {
        await using var bc = Start("pin-verify");

        var check = await Verifier(bc).VerifyAsync("10042", "4711", CancellationToken.None);

        check.ShouldBe(new PinCheck(true, "Erika Mustermann"));
    }

    [Fact]
    public async Task Wrong_pin_is_a_business_answer_not_an_error()
    {
        await using var bc = Start("pin-verify");

        (await Verifier(bc).VerifyAsync("10042", "0000", CancellationToken.None)).ShouldBe(new PinCheck(false, null));
    }

    [Theory]
    [InlineData("", "4711")]
    [InlineData("10042", "")]
    [InlineData("10042", "   ")]
    public async Task Empty_values_never_reach_bc_because_bc_would_accept_an_empty_pin(string number, string pin)
    {
        await using var bc = Start("pin-verify");

        (await Verifier(bc).VerifyAsync(number, pin, CancellationToken.None)).Authenticated.ShouldBeFalse();

        bc.Server.LogEntries.ShouldBeEmpty();
    }

    [Fact]
    public async Task Bc_outage_is_transient_and_neither_exception_nor_logs_contain_the_pin()
    {
        await using var bc = Start("pin-verify-server-error");

        var ex = await Should.ThrowAsync<BcTransientFailureException>(() => Verifier(bc).VerifyAsync("10042", "4711", CancellationToken.None));

        ex.ToString().ShouldNotContain("4711");
        ex.ToString().ShouldNotContain("Mustermann");
        _logs.Lines.ShouldAllBe(l => !l.Contains("4711", StringComparison.Ordinal) && !l.Contains("Mustermann", StringComparison.Ordinal));
        bc.RequestCount("POST").ShouldBe(1); // a write-like call with unknown outcome is not repeated blindly
    }

    [Fact]
    public async Task Missing_configuration_is_reported_not_guessed()
    {
        await using var bc = BcTestHost.Start("pin-verify", new Dictionary<string, string?> { ["Bc:PinVerifyPath"] = "" });

        var ex = await Should.ThrowAsync<BcTechnicalFailureException>(() => Verifier(bc).VerifyAsync("10042", "4711", CancellationToken.None));

        ex.Reason.ShouldBe("BcNotConfigured:PinVerifyPath");
    }
}

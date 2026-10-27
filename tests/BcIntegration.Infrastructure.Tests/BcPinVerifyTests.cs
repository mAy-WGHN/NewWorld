using BcIntegration.Core.Errors;
using BcIntegration.Core.Login;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace BcIntegration.Infrastructure.Tests;

/// <summary>
/// The PIN check runs like in the HR tool and the archived BC-Anbindung: SOAP call of the codeunit <c>Zeiterfassung</c>, function
/// <c>GetStatus</c> with personnel number, today's date and the PIN as "password". BC compares the PIN and answers
/// <c>{"success":bool,"employeeName":...}</c> inside <c>return_value</c>. The PIN is only ever in that request and never logged.
/// </summary>
public sealed class BcPinVerifyTests : IDisposable
{
    private readonly LogCapture _logs = new();

    public void Dispose() => _logs.Dispose();

    private BcTestHost Start(string scenario, IDictionary<string, string?>? overrides = null, Action<IServiceCollection>? configure = null) =>
        BcTestHost.Start(scenario, overrides, s =>
        {
            s.AddLogging(b => b.AddProvider(_logs));
            configure?.Invoke(s);
        });

    private static IBcPinVerifier Verifier(BcTestHost bc) => bc.Services.GetRequiredService<IBcPinVerifier>();

    // --- BC answers ------------------------------------------------------------------------------------------------

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

    [Fact]
    public async Task Success_without_a_name_does_not_count_as_authenticated()
    {
        await using var bc = Start("pin-verify-nameless");

        (await Verifier(bc).VerifyAsync("10042", "4711", CancellationToken.None)).ShouldBe(new PinCheck(false, null));
    }

    // --- the request is the SOAP call of the HR tool ---------------------------------------------------------------

    [Fact]
    public async Task Request_is_the_soap_call_of_zeiterfassung_get_status_with_the_pin_as_password()
    {
        var time = new FakeTimeProvider(new DateTimeOffset(2026, 10, 26, 23, 30, 0, TimeSpan.Zero));
        await using var bc = Start("pin-verify", configure: s => s.AddSingleton<TimeProvider>(time));

        await Verifier(bc).VerifyAsync("10042", "4711", CancellationToken.None);

        var request = bc.Server.LogEntries.Single().RequestMessage!;
        request.Method.ShouldBe("POST");
        request.Path.ShouldBe("/WS/test-company/Codeunit/Zeiterfassung");
        request.Headers!["SOAPAction"].Single().ShouldBe("\"urn:microsoft-dynamics-schemas/codeunit/Zeiterfassung:GetStatus\"");
        request.Headers["Content-Type"].Single().ShouldStartWith("text/xml");
        request.Headers["Authorization"].Single().ShouldStartWith("Bearer token-");
        request.Body!.ShouldContain("<GetStatus xmlns=\"urn:microsoft-dynamics-schemas/codeunit/Zeiterfassung\">");
        request.Body!.ShouldContain("<employeeNo>10042</employeeNo>");
        request.Body!.ShouldContain("<queryDate>2026-10-26</queryDate>"); // UTC date, as the HR tool sends it
        request.Body!.ShouldContain("<password>4711</password>");
    }

    [Fact]
    public async Task Special_characters_in_the_pin_are_escaped_in_the_xml()
    {
        await using var bc = Start("pin-verify");

        await Verifier(bc).VerifyAsync("10042", "a<b&c\"d", CancellationToken.None);

        var body = bc.Server.LogEntries.Single().RequestMessage!.Body!;
        body.ShouldContain("<password>a&lt;b&amp;c&quot;d</password>");
        body.ShouldNotContain("a<b");
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

    // --- failures --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Bc_outage_is_transient_and_neither_exception_nor_logs_contain_the_pin()
    {
        await using var bc = Start("pin-verify-server-error");

        var ex = await Should.ThrowAsync<BcTransientFailureException>(() => Verifier(bc).VerifyAsync("10042", "4711", CancellationToken.None));

        AssertNoSecrets(ex);
        // A call with unknown outcome is never repeated blindly. (0 is possible on a loaded machine: the attempt timeout can
        // expire before the server sees the request.)
        bc.RequestCount("POST").ShouldBeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task Soap_fault_in_a_successful_response_is_a_technical_failure_without_the_fault_text()
    {
        await using var bc = Start("pin-verify-soap-fault");

        var ex = await Should.ThrowAsync<BcTechnicalFailureException>(() => Verifier(bc).VerifyAsync("10042", "4711", CancellationToken.None));

        ex.Reason.ShouldBe("BcSoapFault");
        AssertNoSecrets(ex);
    }

    [Fact]
    public async Task A_return_value_that_is_not_json_is_a_technical_failure()
    {
        await using var bc = Start("pin-verify-plain-text");

        var ex = await Should.ThrowAsync<BcTechnicalFailureException>(() => Verifier(bc).VerifyAsync("10042", "4711", CancellationToken.None));

        ex.Reason.ShouldBe("BcInvalidResponse");
        AssertNoSecrets(ex);
    }

    [Fact]
    public async Task An_answer_with_a_doctype_is_refused_instead_of_resolving_external_entities()
    {
        await using var bc = Start("pin-verify-doctype");

        var ex = await Should.ThrowAsync<BcTechnicalFailureException>(() => Verifier(bc).VerifyAsync("10042", "4711", CancellationToken.None));

        ex.Reason.ShouldBe("BcInvalidResponse");
    }

    [Theory]
    [InlineData("Bc:SoapBaseUrl", "BcNotConfigured:SoapBaseUrl")]
    [InlineData("Bc:CompanyName", "BcNotConfigured:CompanyName")]
    public async Task Missing_configuration_is_reported_not_guessed(string setting, string reason)
    {
        await using var bc = Start("pin-verify", new Dictionary<string, string?> { [setting] = "" });

        var ex = await Should.ThrowAsync<BcTechnicalFailureException>(() => Verifier(bc).VerifyAsync("10042", "4711", CancellationToken.None));

        ex.Reason.ShouldBe(reason);
        bc.Server.LogEntries.ShouldBeEmpty();
    }

    private void AssertNoSecrets(Exception ex)
    {
        ex.ToString().ShouldNotContain("4711");
        ex.ToString().ShouldNotContain("Mustermann");
        _logs.Lines.ShouldAllBe(l => !l.Contains("4711", StringComparison.Ordinal) && !l.Contains("Mustermann", StringComparison.Ordinal));
    }
}

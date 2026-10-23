using BcIntegration.Infrastructure.BusinessCentral;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace BcIntegration.Infrastructure.Tests;

/// <summary>
/// Runs only with <c>BC_SANDBOX_TESTS=1</c>; otherwise skipped (visible in the test output). Never part of the default run.
/// </summary>
public sealed class BcSandboxFactAttribute : FactAttribute
{
    public BcSandboxFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("BC_SANDBOX_TESTS") != "1")
        {
            Skip = "Real BC sandbox: set BC_SANDBOX_TESTS=1 and Bc__BaseUrl, Bc__CompanyId, Bc__Credential (plus AZURE_* for the credential).";
        }
    }
}

/// <summary>
/// Against a real BC sandbox, read-only. Verifies the assumptions of the BC client (TODO(BC-Modell)) without writing anything.
/// Configuration comes from environment variables only, never from files in the repository.
/// </summary>
[Trait("Category", "BcSandbox")]
public class BcSandboxTests
{
    private static BcClient CreateClient()
    {
        var configuration = new ConfigurationBuilder().AddEnvironmentVariables().Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBcClient(configuration);
        return services.BuildServiceProvider().GetRequiredService<BcClient>();
    }

    [BcSandboxFact]
    public async Task Employees_endpoint_answers_with_the_assumed_shape()
    {
        var employees = await CreateClient().GetEmployeesModifiedSinceAsync(TimeProvider.System.GetUtcNow().AddDays(-1), CancellationToken.None);

        employees.ShouldAllBe(e => e.BcSystemId != Guid.Empty && e.PersonnelNumber.Length > 0);
    }
}

using NetArchTest.Rules;

namespace Architecture.Tests;

public class DependencyRulesTests
{
    private static readonly string[] ForbiddenInCore =
        ["Microsoft.EntityFrameworkCore", "Npgsql", "Azure", "Microsoft.Identity", "System.Net.Http"];

    [Fact]
    public void Core_projects_have_no_infrastructure_dependencies()
    {
        foreach (var core in Solution.All("Core"))
        {
            var result = Types.InAssembly(core).ShouldNot().HaveDependencyOnAny(ForbiddenInCore).GetResult();

            result.IsSuccessful.ShouldBeTrue($"{core.GetName().Name}: {string.Join(", ", result.FailingTypeNames ?? [])}");
            core.GetReferencedAssemblies().Select(a => a.Name!)
                .Where(n => ForbiddenInCore.Any(f => n.StartsWith(f, StringComparison.Ordinal)))
                .ShouldBeEmpty($"{core.GetName().Name} must not reference infrastructure assemblies");
        }
    }

    [Fact]
    public void Contracts_depend_only_on_shared_messaging()
    {
        foreach (var contracts in Solution.All("Contracts"))
        {
            var projectRefs = contracts.GetReferencedAssemblies().Select(a => a.Name!)
                .Where(n => Solution.Services.Any(s => n.StartsWith(s + ".", StringComparison.Ordinal)) || n.StartsWith("Shared.", StringComparison.Ordinal))
                .ToList();

            projectRefs.ShouldBeSubsetOf(["Shared.Messaging"], $"{contracts.GetName().Name} may only reference Shared.Messaging");
        }
    }

    [Fact]
    public void A_service_references_another_service_only_through_its_contracts()
    {
        foreach (var service in Solution.Services)
        {
            foreach (var layer in Solution.Layers)
            {
                var assembly = Solution.Load(service, layer);
                var foreign = assembly.GetReferencedAssemblies().Select(a => a.Name!)
                    .Where(n => Solution.Services.Any(other => other != service && n.StartsWith(other + ".", StringComparison.Ordinal)))
                    .Where(n => !n.EndsWith(".Contracts", StringComparison.Ordinal))
                    .ToList();

                foreign.ShouldBeEmpty($"{assembly.GetName().Name} references another service beyond its Contracts");
            }
        }
    }

    [Fact]
    public void Core_does_not_reference_infrastructure_or_api_of_its_own_service()
    {
        foreach (var service in Solution.Services)
        {
            var refs = Solution.Load(service, "Core").GetReferencedAssemblies().Select(a => a.Name);
            refs.ShouldNotContain($"{service}.Infrastructure");
            refs.ShouldNotContain($"{service}.Api");
        }
    }

    [Fact]
    public void Shared_libraries_do_not_depend_on_any_service()
    {
        foreach (var shared in new[] { PureMessaging, typeof(Shared.Authorization.AppPolicies).Assembly, PureApprovals, SharedInfrastructure })
        {
            shared.GetReferencedAssemblies().Select(a => a.Name!)
                .Where(n => Solution.Services.Any(s => n.StartsWith(s + ".", StringComparison.Ordinal)))
                .ShouldBeEmpty($"{shared.GetName().Name} must not depend on a service");
        }
    }

    private static readonly System.Reflection.Assembly PureMessaging = typeof(Shared.Messaging.IMessage).Assembly;
    private static readonly System.Reflection.Assembly PureApprovals = typeof(Shared.Approvals.ApprovalRequest).Assembly;
    private static readonly System.Reflection.Assembly SharedInfrastructure = typeof(Shared.Infrastructure.Messaging.OutboxMessage).Assembly;

    [Fact]
    public void Shared_messaging_and_approvals_reference_only_the_base_class_library()
    {
        foreach (var pure in new[] { PureMessaging, PureApprovals })
        {
            pure.GetReferencedAssemblies().Select(a => a.Name!)
                .Where(n => !n.StartsWith("System", StringComparison.Ordinal) && n != "netstandard")
                .ShouldBeEmpty($"{pure.GetName().Name} must stay free of packages (Contracts and Core depend on it)");
        }
    }

    [Fact]
    public void Shared_authorization_has_no_persistence_or_messaging_dependencies()
    {
        var result = Types.InAssembly(typeof(Shared.Authorization.AppPolicies).Assembly)
            .ShouldNot().HaveDependencyOnAny("Microsoft.EntityFrameworkCore", "Npgsql", "Azure.Messaging")
            .GetResult();

        result.IsSuccessful.ShouldBeTrue(string.Join(", ", result.FailingTypeNames ?? []));
    }

    [Fact]
    public void Core_and_contracts_never_reference_shared_infrastructure()
    {
        foreach (var assembly in Solution.All("Core").Concat(Solution.All("Contracts")))
        {
            assembly.GetReferencedAssemblies().Select(a => a.Name)
                .ShouldNotContain("Shared.Infrastructure", $"{assembly.GetName().Name} must not reference Shared.Infrastructure");
        }
    }
}

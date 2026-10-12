using BusinessCentralBlazor.Domain;

namespace BusinessCentralBlazor.UnitTests.Architecture;

public sealed class DomainDependencyTests
{
    [Fact]
    public void DomainAssemblyDoesNotReferenceOuterApplicationLayers()
    {
        var referencedAssemblies = typeof(DomainAssemblyMarker).Assembly.GetReferencedAssemblies();

        Assert.DoesNotContain(
            referencedAssemblies,
            assembly => assembly.Name is "BusinessCentralBlazor.Application"
                or "BusinessCentralBlazor.Infrastructure"
                or "BusinessCentralBlazor.Web");
    }
}

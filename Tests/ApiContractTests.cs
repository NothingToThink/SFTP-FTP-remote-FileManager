using Microsoft.AspNetCore.Mvc.ApiExplorer;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace Tests;

public class ApiContractTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ApiContractTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    // GET/HEAD/DELETE bodies have no defined semantics (RFC 9110): browsers refuse to send them
    // and proxies may drop them. ApiExplorer also catches bodies inferred by [ApiController].
    [Fact]
    public void GetHeadDelete_NeverBindFromBody()
    {
        var provider = _factory.Services.GetRequiredService<IApiDescriptionGroupCollectionProvider>();
        var offenders = provider.ApiDescriptionGroups.Items
            .SelectMany(g => g.Items)
            .Where(d => d.HttpMethod is "GET" or "HEAD" or "DELETE")
            .Where(d => d.ParameterDescriptions.Any(p => p.Source == BindingSource.Body))
            .Select(d => $"{d.HttpMethod} {d.RelativePath}")
            .ToList();

        Assert.True(offenders.Count == 0,
            $"GET/HEAD/DELETE must not read the body, pass data via query instead:\n{string.Join("\n", offenders)}");
    }
}

using System.Net;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Linkbelli.IntegrationTests;

public class ApiSmokeTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    [Fact]
    public async Task Root_returns_api_info()
    {
        var client = factory.CreateClient();
        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Linkbelli API", body);
    }
}

using System.Net;

namespace Linkbelli.IntegrationTests;

[Collection(IntegrationCollection.Name)]
public class ApiSmokeTests(PostgresApiFactory factory)
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

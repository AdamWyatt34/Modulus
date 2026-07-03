using System.Net;
using Shouldly;
using Xunit;

namespace SampleApp.Orders.Tests.Integration;

public class OrdersEndpointTests : OrdersIntegrationTestBase
{
    [Fact]
    public async Task Get_module_endpoint_returns_200()
    {
        var response = await Client.GetAsync("/api/orders");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

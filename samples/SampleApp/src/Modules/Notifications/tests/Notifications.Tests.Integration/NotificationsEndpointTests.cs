using System.Net;
using Shouldly;
using Xunit;

namespace SampleApp.Notifications.Tests.Integration;

public class NotificationsEndpointTests : NotificationsIntegrationTestBase
{
    [Fact]
    public async Task Get_module_endpoint_returns_200()
    {
        var response = await Client.GetAsync("/api/notifications");

        response.StatusCode.ShouldBe(HttpStatusCode.OK);
    }
}

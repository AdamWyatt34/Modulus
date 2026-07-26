using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Modulus.Messaging.Retention;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Retention;

public class RetentionOptionsValidationTests
{
    private static IServiceCollection NewServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        return services;
    }

    [Fact]
    public void Disabled_retention_skips_validation_and_registers_no_sweep()
    {
        var services = NewServices();

        services.AddModulusMessaging(options =>
        {
            options.Retention.Enabled = false;
            options.Retention.ProcessedOutboxAge = TimeSpan.Zero; // would throw if validated
        });

        services.ShouldNotContain(d =>
            d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(MessagingRetentionService));
    }

    [Fact]
    public void Enabled_retention_registers_the_sweep_service()
    {
        var services = NewServices();

        services.AddModulusMessaging(options => options.Retention.Enabled = true);

        services.ShouldContain(d =>
            d.ServiceType == typeof(IHostedService)
            && d.ImplementationType == typeof(MessagingRetentionService));
    }

    [Theory]
    [InlineData("ProcessedOutboxAge")]
    [InlineData("InboxAge")]
    [InlineData("SweepInterval")]
    public void Enabled_retention_with_sub_minute_timespan_throws(string property)
    {
        var services = NewServices();

        var ex = Should.Throw<ArgumentOutOfRangeException>(() =>
            services.AddModulusMessaging(options =>
            {
                options.Retention.Enabled = true;
                switch (property)
                {
                    case "ProcessedOutboxAge":
                        options.Retention.ProcessedOutboxAge = TimeSpan.FromSeconds(30);
                        break;
                    case "InboxAge":
                        options.Retention.InboxAge = TimeSpan.FromSeconds(30);
                        break;
                    case "SweepInterval":
                        options.Retention.SweepInterval = TimeSpan.FromSeconds(30);
                        break;
                }
            }));

        ex.Message.ShouldContain(property);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10_001)]
    public void Enabled_retention_with_out_of_range_batch_size_throws(int batchSize)
    {
        var services = NewServices();

        var ex = Should.Throw<ArgumentOutOfRangeException>(() =>
            services.AddModulusMessaging(options =>
            {
                options.Retention.Enabled = true;
                options.Retention.PurgeBatchSize = batchSize;
            }));

        ex.Message.ShouldContain("PurgeBatchSize");
    }
}

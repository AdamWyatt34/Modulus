using System.Text.Json;
using Modulus.Messaging.AzureServiceBus.IntegrationTests.Fixtures;
using Modulus.Messaging.Serialization;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.AzureServiceBus.IntegrationTests;

// A plain [Fact], deliberately without [Trait("Category", "Integration")]: this must run in
// the default (non-Docker) test filter so a rename of a fixture event type or a change to
// AzureServiceBusTopology's naming scheme fails fast, instead of silently breaking the
// Integration-only scenarios that depend on Config.json matching what AutoProvision=false
// expects to already exist.
public sealed class ConfigJsonDriftGuardTests
{
    private static readonly string ConfigPath = Path.Combine(AppContext.BaseDirectory, "Config.json");

    [Fact]
    public void ConfigJson_EntityNames_MatchAzureServiceBusTopologyOutputs()
    {
        var declaredTopics = LoadDeclaredTopics();

        AssertTopicAndSubscription(declaredTopics, typeof(RoundTripEvent), RoundTripTests.EndpointName);
        AssertTopicAndSubscription(declaredTopics, typeof(DeadLetterEvent), DeadLetterTests.EndpointName);
    }

    private static void AssertTopicAndSubscription(
        Dictionary<string, HashSet<string>> declaredTopics,
        Type eventType,
        string endpointName)
    {
        var expectedTopic = AzureServiceBusTopology.TopicName(MessageTypeRegistry.GetStableName(eventType));
        var expectedSubscription = AzureServiceBusTopology.SubscriptionName(endpointName);

        declaredTopics.ShouldContainKey(
            expectedTopic,
            $"Config.json must declare a topic named '{expectedTopic}' for {eventType.Name} "
            + "(AzureServiceBusTopology.TopicName output).");

        declaredTopics[expectedTopic].ShouldContain(
            expectedSubscription,
            $"Config.json topic '{expectedTopic}' must declare a subscription named "
            + $"'{expectedSubscription}' for endpoint '{endpointName}' (AzureServiceBusTopology.SubscriptionName output).");
    }

    private static Dictionary<string, HashSet<string>> LoadDeclaredTopics()
    {
        using var document = JsonDocument.Parse(File.ReadAllText(ConfigPath));

        var result = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var ns in document.RootElement.GetProperty("UserConfig").GetProperty("Namespaces").EnumerateArray())
        {
            foreach (var topic in ns.GetProperty("Topics").EnumerateArray())
            {
                var topicName = topic.GetProperty("Name").GetString()!;
                var subscriptions = topic.GetProperty("Subscriptions").EnumerateArray()
                    .Select(sub => sub.GetProperty("Name").GetString()!)
                    .ToHashSet(StringComparer.Ordinal);

                result[topicName] = subscriptions;
            }
        }

        return result;
    }
}

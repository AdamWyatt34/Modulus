using System.Diagnostics;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Messaging.Abstractions;
using Modulus.Messaging.Diagnostics;
using Modulus.Messaging.Outbox;
using Modulus.Messaging.Tests.Fixtures;
using Modulus.Messaging.Transports;
using Shouldly;
using Xunit;

namespace Modulus.Messaging.Tests.Outbox;

// End-to-end over the outbox half of the trace story: Save captures the ambient business
// activity onto the row; the dispatcher links its producer span to it and injects the
// dispatch context into the envelope headers the transport ships.
public sealed class OutboxTracePropagationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly List<Activity> _completed = [];
    private readonly ActivityListener _listener;
    private readonly ActivitySource _testSource = new("OutboxTracePropagationTests");

    public OutboxTracePropagationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name
                is MessagingDiagnostics.OutboxActivitySourceName
                or "OutboxTracePropagationTests",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => { lock (_completed) _completed.Add(activity); },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose()
    {
        _listener.Dispose();
        _testSource.Dispose();
        _connection.Dispose();
    }

    private ServiceProvider BuildProvider(FakeMessageTransport transport)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxDbContext>(options => options.UseSqlite(_connection));
        services.AddModulusMessaging(options =>
        {
            options.Transport = Transport.InMemory;
            options.Assemblies.Add(typeof(TestOrderCreatedEvent).Assembly);
        });
        services.AddSingleton<IMessageTransport>(transport);

        var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        scope.ServiceProvider.GetRequiredService<OutboxDbContext>().Database.EnsureCreated();
        return provider;
    }

    [Fact]
    public async Task Save_captures_the_ambient_activity_context_on_the_row()
    {
        await using var provider = BuildProvider(new FakeMessageTransport());

        ActivityTraceId businessTraceId;
        using (var business = _testSource.StartActivity("place-order"))
        {
            business.ShouldNotBeNull();
            business.TraceStateString = "vendor=x";
            businessTraceId = business.TraceId;

            using var scope = provider.CreateScope();
            var store = scope.ServiceProvider.GetRequiredService<IOutboxStore>();
            await store.Save(new TestOrderCreatedEvent { OrderId = 1, CustomerName = "T" });
        }

        using var readScope = provider.CreateScope();
        var row = (await readScope.ServiceProvider.GetRequiredService<IOutboxStore>()
            .ClaimPending("test-reader", TimeSpan.FromMinutes(5), 10, int.MaxValue)).ShouldHaveSingleItem();

        row.TraceParent.ShouldNotBeNull();
        row.TraceParent.ShouldContain(businessTraceId.ToString());
        row.TraceState.ShouldBe("vendor=x");
    }

    [Fact]
    public async Task Save_without_ambient_activity_leaves_trace_columns_null()
    {
        await using var provider = BuildProvider(new FakeMessageTransport());

        Activity.Current.ShouldBeNull();
        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOutboxStore>()
                .Save(new TestOrderCreatedEvent { OrderId = 2, CustomerName = "T" });
        }

        using var readScope = provider.CreateScope();
        var row = (await readScope.ServiceProvider.GetRequiredService<IOutboxStore>()
            .ClaimPending("test-reader", TimeSpan.FromMinutes(5), 10, int.MaxValue)).ShouldHaveSingleItem();

        row.TraceParent.ShouldBeNull();
        row.TraceState.ShouldBeNull();
    }

    [Fact]
    public async Task Dispatch_links_to_the_saved_context_and_injects_dispatch_headers()
    {
        var transport = new FakeMessageTransport();
        await using var provider = BuildProvider(transport);

        ActivityTraceId businessTraceId;
        var @event = new TestOrderCreatedEvent { OrderId = 3, CustomerName = "T" };
        using (var business = _testSource.StartActivity("place-order"))
        {
            business.ShouldNotBeNull();
            businessTraceId = business.TraceId;

            using var scope = provider.CreateScope();
            await scope.ServiceProvider.GetRequiredService<IOutboxStore>().Save(@event);
        }

        var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();
        var dispatched = await dispatcher.DispatchPendingAsync();

        dispatched.ShouldBe(1);

        // The listener is process-global while test classes run in parallel — other classes'
        // outbox.dispatch spans land in _completed too; select this row's by message id.
        Activity dispatchActivity;
        lock (_completed)
        {
            dispatchActivity = _completed
                .Where(a => Equals(a.GetTagItem("modulus.message_id"), @event.EventId))
                .ShouldHaveSingleItem();
        }

        // The originating business trace is reachable through the link, not the parent.
        dispatchActivity.Parent.ShouldBeNull();
        var link = dispatchActivity.Links.ShouldHaveSingleItem();
        link.Context.TraceId.ShouldBe(businessTraceId);

        // The envelope carries the dispatch span's context — the consumer joins that trace.
        var envelope = transport.Published.ShouldHaveSingleItem();
        envelope.Headers.ShouldNotBeNull();
        envelope.Headers["traceparent"].ShouldContain(dispatchActivity.TraceId.ToString());
    }

    [Fact]
    public async Task Dispatch_of_a_pre_migration_row_without_trace_context_still_publishes()
    {
        var transport = new FakeMessageTransport();
        await using var provider = BuildProvider(transport);

        using (var scope = provider.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IOutboxStore>()
                .Save(new TestOrderCreatedEvent { OrderId = 4, CustomerName = "T" });
        }

        var dispatched = await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync();

        dispatched.ShouldBe(1);
        var envelope = transport.Published.ShouldHaveSingleItem();
        // No saved context: dispatch still emits its own span and injects it.
        envelope.Headers.ShouldNotBeNull();
        envelope.Headers.ShouldContainKey("traceparent");
    }
}

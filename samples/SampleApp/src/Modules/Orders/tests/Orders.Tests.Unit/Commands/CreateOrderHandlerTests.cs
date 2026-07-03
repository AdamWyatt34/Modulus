using Modulus.Messaging.Abstractions;
using Shouldly;
using Xunit;
using SampleApp.Orders.Application.Commands.CreateOrder;
using SampleApp.Orders.Domain.Entities;
using SampleApp.Orders.Domain.Repositories;
using SampleApp.Orders.Integration.IntegrationEvents;

namespace SampleApp.Orders.Tests.Unit.Commands;

public class CreateOrderHandlerTests
{
    [Fact]
    public async Task Handle_should_return_success_with_new_order_id()
    {
        var repository = new FakeOrderRepository();
        var outbox = new FakeOutboxStore();
        var handler = new CreateOrderHandler(repository, outbox);

        var result = await handler.Handle(new CreateOrder("Ada Lovelace", 42.50m));

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBe(Guid.Empty);
        repository.Added.Count.ShouldBe(1);
        repository.Added[0].CustomerName.ShouldBe("Ada Lovelace");
    }

    [Fact]
    public async Task Handle_should_save_OrderPlaced_event_to_outbox()
    {
        var repository = new FakeOrderRepository();
        var outbox = new FakeOutboxStore();
        var handler = new CreateOrderHandler(repository, outbox);

        var result = await handler.Handle(new CreateOrder("Ada Lovelace", 42.50m));

        var @event = outbox.Saved.ShouldHaveSingleItem().ShouldBeOfType<OrderPlaced>();
        @event.OrderId.ShouldBe(result.Value);
        @event.Total.ShouldBe(42.50m);
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        public List<Order> Added { get; } = [];

        public Task AddAsync(Order entity, CancellationToken cancellationToken = default)
        {
            Added.Add(entity);
            return Task.CompletedTask;
        }

        public Task<Order?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Added.FirstOrDefault(o => o.Id == id));

        public Task<IReadOnlyList<Order>> ListAllAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<Order>>(Added);

        public void Update(Order entity)
        {
        }

        public void Remove(Order entity) => Added.Remove(entity);
    }

    private sealed class FakeOutboxStore : IOutboxStore
    {
        public List<IIntegrationEvent> Saved { get; } = [];

        public Task Save(IIntegrationEvent @event, CancellationToken cancellationToken = default)
        {
            Saved.Add(@event);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxMessage>> GetPending(
            int batchSize, int maxAttempts, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<OutboxMessage>>([]);

        public Task MarkAsProcessed(IEnumerable<Guid> ids, CancellationToken cancellationToken = default)
            => Task.CompletedTask;

        public Task MarkAsFailed(Guid messageId, string error, CancellationToken cancellationToken = default)
            => Task.CompletedTask;
    }
}

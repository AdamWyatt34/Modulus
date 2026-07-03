using Modulus.Mediator.Abstractions;

namespace SampleApp.BuildingBlocks.Domain.Entities;

public interface IHasDomainEvents
{
    IReadOnlyCollection<IDomainEvent> DomainEvents { get; }

    void ClearDomainEvents();
}

using Microsoft.Extensions.DependencyInjection;
using Modulus.Mediator.Abstractions;
using Modulus.Mediator.Behaviors;
using Modulus.Mediator.Tests.Fixtures;
using Shouldly;
using Xunit;

namespace Modulus.Mediator.Tests.Behaviors;

public class UnitOfWorkBehaviorTests
{
    [Fact]
    public async Task Handle_SuccessfulCommand_CallsSaveChanges()
    {
        var uow = new RecordingUnitOfWork();
        var services = new ServiceCollection()
            .AddSingleton<IUnitOfWork>(uow)
            .BuildServiceProvider();
        var behavior = new UnitOfWorkBehavior<TestCommand, Result>(services);

        var result = await behavior.Handle(
            new TestCommand("ok"),
            () => Task.FromResult(Result.Success()),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        uow.SaveCount.ShouldBe(1);
    }

    [Fact]
    public async Task Handle_FailedCommand_DoesNotCallSaveChanges()
    {
        var uow = new RecordingUnitOfWork();
        var services = new ServiceCollection()
            .AddSingleton<IUnitOfWork>(uow)
            .BuildServiceProvider();
        var behavior = new UnitOfWorkBehavior<TestCommand, Result>(services);

        var result = await behavior.Handle(
            new TestCommand("fail"),
            () => Task.FromResult(Result.Failure(Error.Failure("Test", "nope"))),
            CancellationToken.None);

        result.IsFailure.ShouldBeTrue();
        uow.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_Query_DoesNotCallSaveChanges()
    {
        var uow = new RecordingUnitOfWork();
        var services = new ServiceCollection()
            .AddSingleton<IUnitOfWork>(uow)
            .BuildServiceProvider();
        var behavior = new UnitOfWorkBehavior<GetItemQuery, Result<string>>(services);

        var result = await behavior.Handle(
            new GetItemQuery(42),
            () => Task.FromResult<Result<string>>("Item-42"),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        uow.SaveCount.ShouldBe(0);
    }

    [Fact]
    public async Task Handle_NoUnitOfWorkRegistered_DoesNotThrow()
    {
        var services = new ServiceCollection().BuildServiceProvider();
        var behavior = new UnitOfWorkBehavior<TestCommand, Result>(services);

        var result = await behavior.Handle(
            new TestCommand("ok"),
            () => Task.FromResult(Result.Success()),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
    }

    [Fact]
    public async Task Handle_PassesCancellationToken_ToSaveChanges()
    {
        var uow = new RecordingUnitOfWork();
        var services = new ServiceCollection()
            .AddSingleton<IUnitOfWork>(uow)
            .BuildServiceProvider();
        var behavior = new UnitOfWorkBehavior<TestCommand, Result>(services);
        using var cts = new CancellationTokenSource();

        await behavior.Handle(
            new TestCommand("ok"),
            () => Task.FromResult(Result.Success()),
            cts.Token);

        uow.LastToken.ShouldBe(cts.Token);
    }

    [Fact]
    public async Task Handle_CommandWithResult_CallsSaveChanges()
    {
        var uow = new RecordingUnitOfWork();
        var services = new ServiceCollection()
            .AddSingleton<IUnitOfWork>(uow)
            .BuildServiceProvider();
        var behavior = new UnitOfWorkBehavior<CreateItemCommand, Result<int>>(services);

        var result = await behavior.Handle(
            new CreateItemCommand("ok"),
            () => Task.FromResult<Result<int>>(7),
            CancellationToken.None);

        result.IsSuccess.ShouldBeTrue();
        uow.SaveCount.ShouldBe(1);
    }

    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int SaveCount { get; private set; }
        public CancellationToken LastToken { get; private set; }

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
        {
            SaveCount++;
            LastToken = cancellationToken;
            return Task.FromResult(1);
        }
    }
}

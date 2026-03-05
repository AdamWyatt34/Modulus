using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;
using Modulus.Generators.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Modulus.Generators.Tests;

public class HandlerRegistrationGeneratorTests
{
    private const string SystemUsings = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using System.Collections.Generic;
        """;

    [Fact]
    public void Generate_CommandHandlers_RegistersAsScoped()
    {
        var source = SystemUsings + """
            using Modulus.Mediator.Abstractions;

            namespace TestApp;

            public record CreateOrderCommand : ICommand;

            public sealed class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand>
            {
                public Task<Result> Handle(CreateOrderCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(Result.Success());
            }
            """;

        var (outputCompilation, _, runResult) = GeneratorTestHelper.RunHandlerRegistrationGenerator(source, "TestApp");

        var generatedSource = GeneratorTestHelper.GetGeneratedSource(runResult, "ModulusHandlerRegistrations.g.cs");

        generatedSource.ShouldContain("// Commands");
        generatedSource.ShouldContain(
            "services.AddScoped<global::Modulus.Mediator.Abstractions.ICommandHandler<global::TestApp.CreateOrderCommand>, global::TestApp.CreateOrderCommandHandler>();");
        generatedSource.ShouldContain("namespace TestApp;");

        var errors = outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Generate_CommandHandlerWithResult_RegistersCorrectInterface()
    {
        var source = SystemUsings + """
            using Modulus.Mediator.Abstractions;

            namespace TestApp;

            public record OrderId(Guid Value);
            public record CreateOrderCommand : ICommand<OrderId>;

            public sealed class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand, OrderId>
            {
                public Task<Result<OrderId>> Handle(CreateOrderCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult<Result<OrderId>>(new OrderId(Guid.NewGuid()));
            }
            """;

        var (outputCompilation, _, runResult) = GeneratorTestHelper.RunHandlerRegistrationGenerator(source, "TestApp");

        var generatedSource = GeneratorTestHelper.GetGeneratedSource(runResult, "ModulusHandlerRegistrations.g.cs");

        generatedSource.ShouldContain("// Commands");
        generatedSource.ShouldContain(
            "services.AddScoped<global::Modulus.Mediator.Abstractions.ICommandHandler<global::TestApp.CreateOrderCommand, global::TestApp.OrderId>, global::TestApp.CreateOrderCommandHandler>();");

        var errors = outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Generate_QueryHandler_RegistersAsScoped()
    {
        var source = SystemUsings + """
            using Modulus.Mediator.Abstractions;

            namespace TestApp;

            public record OrderDto(string Name);
            public record GetOrderQuery : IQuery<OrderDto>;

            public sealed class GetOrderQueryHandler : IQueryHandler<GetOrderQuery, OrderDto>
            {
                public Task<Result<OrderDto>> Handle(GetOrderQuery query, CancellationToken cancellationToken = default)
                    => Task.FromResult<Result<OrderDto>>(new OrderDto("Test"));
            }
            """;

        var (outputCompilation, _, runResult) = GeneratorTestHelper.RunHandlerRegistrationGenerator(source, "TestApp");

        var generatedSource = GeneratorTestHelper.GetGeneratedSource(runResult, "ModulusHandlerRegistrations.g.cs");

        generatedSource.ShouldContain("// Queries");
        generatedSource.ShouldContain(
            "services.AddScoped<global::Modulus.Mediator.Abstractions.IQueryHandler<global::TestApp.GetOrderQuery, global::TestApp.OrderDto>, global::TestApp.GetOrderQueryHandler>();");

        var errors = outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Generate_MultipleDomainEventHandlers_RegistersAll()
    {
        var source = SystemUsings + """
            using Modulus.Mediator.Abstractions;

            namespace TestApp;

            public record OrderCreatedEvent : IDomainEvent
            {
                public Guid Id { get; } = Guid.NewGuid();
                public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
            }

            public sealed class OrderCreatedEventHandler : IDomainEventHandler<OrderCreatedEvent>
            {
                public Task Handle(OrderCreatedEvent domainEvent, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }

            public sealed class SendOrderConfirmationHandler : IDomainEventHandler<OrderCreatedEvent>
            {
                public Task Handle(OrderCreatedEvent domainEvent, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var (outputCompilation, _, runResult) = GeneratorTestHelper.RunHandlerRegistrationGenerator(source, "TestApp");

        var generatedSource = GeneratorTestHelper.GetGeneratedSource(runResult, "ModulusHandlerRegistrations.g.cs");

        generatedSource.ShouldContain("// Domain Events");
        generatedSource.ShouldContain(
            "services.AddScoped<global::Modulus.Mediator.Abstractions.IDomainEventHandler<global::TestApp.OrderCreatedEvent>, global::TestApp.OrderCreatedEventHandler>();");
        generatedSource.ShouldContain(
            "services.AddScoped<global::Modulus.Mediator.Abstractions.IDomainEventHandler<global::TestApp.OrderCreatedEvent>, global::TestApp.SendOrderConfirmationHandler>();");

        var errors = outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Generate_Validator_RegistersAsIValidator()
    {
        var source = SystemUsings + """
            using FluentValidation;
            using Modulus.Mediator.Abstractions;

            namespace TestApp;

            public record CreateOrderCommand : ICommand;

            public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
            {
                public CreateOrderCommandValidator()
                {
                }
            }
            """;

        var (outputCompilation, _, runResult) = GeneratorTestHelper.RunHandlerRegistrationGenerator(source, "TestApp");

        var generatedSource = GeneratorTestHelper.GetGeneratedSource(runResult, "ModulusHandlerRegistrations.g.cs");

        generatedSource.ShouldContain("// Validators");
        generatedSource.ShouldContain(
            "services.AddScoped<global::FluentValidation.IValidator<global::TestApp.CreateOrderCommand>, global::TestApp.CreateOrderCommandValidator>();");

        var errors = outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Generate_NoHandlers_GeneratesEmptyMethod()
    {
        var source = """
            namespace TestApp;

            public class SomeService
            {
                public void DoWork() { }
            }
            """;

        var (outputCompilation, _, runResult) = GeneratorTestHelper.RunHandlerRegistrationGenerator(source, "TestApp");

        var generatedSource = GeneratorTestHelper.GetGeneratedSource(runResult, "ModulusHandlerRegistrations.g.cs");

        generatedSource.ShouldContain("public static class ModulusHandlerRegistrations");
        generatedSource.ShouldContain("public static IServiceCollection AddModulusHandlers(this IServiceCollection services)");
        generatedSource.ShouldContain("return services;");
        generatedSource.ShouldNotContain("AddScoped");

        var errors = outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Generate_OpenGenericHandler_SkippedWithDiagnostic()
    {
        var source = SystemUsings + """
            using Modulus.Mediator.Abstractions;

            namespace TestApp;

            public class GenericHandler<TCommand> : ICommandHandler<TCommand>
                where TCommand : ICommand
            {
                public Task<Result> Handle(TCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(Result.Success());
            }
            """;

        var (_, _, runResult) = GeneratorTestHelper.RunHandlerRegistrationGenerator(source, "TestApp");

        var generatedSource = GeneratorTestHelper.GetGeneratedSource(runResult, "ModulusHandlerRegistrations.g.cs");
        generatedSource.ShouldNotContain("AddScoped");

        var diagnostics = runResult.Results
            .SelectMany(r => r.Diagnostics)
            .Where(d => d.Id == "MODGEN003")
            .ToList();
        diagnostics.Count.ShouldBe(1);
        diagnostics[0].Severity.ShouldBe(DiagnosticSeverity.Info);
        diagnostics[0].GetMessage().ShouldContain("GenericHandler");
    }

    [Fact]
    public void Generate_MixedAssembly_GeneratesAllCategories()
    {
        var source = SystemUsings + """
            using FluentValidation;
            using Modulus.Mediator.Abstractions;
            using Modulus.Messaging.Abstractions;

            namespace TestApp;

            // Command types
            public record CreateOrderCommand : ICommand;
            public record OrderId(Guid Value);
            public record PlaceOrderCommand : ICommand<OrderId>;

            // Query types
            public record OrderDto(string Name);
            public record GetOrderQuery : IQuery<OrderDto>;

            // Event types
            public record OrderCreatedEvent : IDomainEvent
            {
                public Guid Id { get; } = Guid.NewGuid();
                public DateTime OccurredOnUtc { get; } = DateTime.UtcNow;
            }

            public record PaymentCompletedEvent : IIntegrationEvent
            {
                public Guid EventId { get; } = Guid.NewGuid();
                public DateTime OccurredOn { get; } = DateTime.UtcNow;
                public string? CorrelationId { get; }
            }

            // Handlers
            public sealed class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand>
            {
                public Task<Result> Handle(CreateOrderCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(Result.Success());
            }

            public sealed class PlaceOrderCommandHandler : ICommandHandler<PlaceOrderCommand, OrderId>
            {
                public Task<Result<OrderId>> Handle(PlaceOrderCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult<Result<OrderId>>(new OrderId(Guid.NewGuid()));
            }

            public sealed class GetOrderQueryHandler : IQueryHandler<GetOrderQuery, OrderDto>
            {
                public Task<Result<OrderDto>> Handle(GetOrderQuery query, CancellationToken cancellationToken = default)
                    => Task.FromResult<Result<OrderDto>>(new OrderDto("Test"));
            }

            public sealed class OrderCreatedEventHandler : IDomainEventHandler<OrderCreatedEvent>
            {
                public Task Handle(OrderCreatedEvent domainEvent, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }

            public sealed class PaymentCompletedHandler : IIntegrationEventHandler<PaymentCompletedEvent>
            {
                public Task Handle(PaymentCompletedEvent @event, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }

            public sealed class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
            {
                public CreateOrderCommandValidator() { }
            }
            """;

        var (outputCompilation, _, runResult) = GeneratorTestHelper.RunHandlerRegistrationGenerator(source, "TestApp");

        var generatedSource = GeneratorTestHelper.GetGeneratedSource(runResult, "ModulusHandlerRegistrations.g.cs");

        // Verify all categories present
        generatedSource.ShouldContain("// Commands");
        generatedSource.ShouldContain("// Queries");
        generatedSource.ShouldContain("// Domain Events");
        generatedSource.ShouldContain("// Integration Events");
        generatedSource.ShouldContain("// Validators");

        // Verify specific registrations
        generatedSource.ShouldContain("ICommandHandler<global::TestApp.CreateOrderCommand>, global::TestApp.CreateOrderCommandHandler>");
        generatedSource.ShouldContain("ICommandHandler<global::TestApp.PlaceOrderCommand, global::TestApp.OrderId>, global::TestApp.PlaceOrderCommandHandler>");
        generatedSource.ShouldContain("IQueryHandler<global::TestApp.GetOrderQuery, global::TestApp.OrderDto>, global::TestApp.GetOrderQueryHandler>");
        generatedSource.ShouldContain("IDomainEventHandler<global::TestApp.OrderCreatedEvent>, global::TestApp.OrderCreatedEventHandler>");
        generatedSource.ShouldContain("IIntegrationEventHandler<global::TestApp.PaymentCompletedEvent>, global::TestApp.PaymentCompletedHandler>");
        generatedSource.ShouldContain("IValidator<global::TestApp.CreateOrderCommand>, global::TestApp.CreateOrderCommandValidator>");

        var errors = outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.ShouldBeEmpty();
    }

    [Fact]
    public void Generate_Registrations_ResolveFromServiceProvider()
    {
        var source = SystemUsings + """
            using Modulus.Mediator.Abstractions;

            namespace TestApp;

            public record CreateOrderCommand : ICommand;

            public sealed class CreateOrderCommandHandler : ICommandHandler<CreateOrderCommand>
            {
                public Task<Result> Handle(CreateOrderCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(Result.Success());
            }
            """;

        var (outputCompilation, _, _) = GeneratorTestHelper.RunHandlerRegistrationGenerator(source, "TestApp");

        var errors = outputCompilation.GetDiagnostics().Where(d => d.Severity == DiagnosticSeverity.Error).ToList();
        errors.ShouldBeEmpty();

        // Emit the compilation to an in-memory assembly and verify DI resolution
        using var ms = new MemoryStream();
        var emitResult = outputCompilation.Emit(ms);
        emitResult.Success.ShouldBeTrue();

        ms.Seek(0, SeekOrigin.Begin);
        var assembly = Assembly.Load(ms.ToArray());

        var registrationClass = assembly.GetType("TestApp.ModulusHandlerRegistrations");
        registrationClass.ShouldNotBeNull();

        var method = registrationClass!.GetMethod("AddModulusHandlers", BindingFlags.Public | BindingFlags.Static);
        method.ShouldNotBeNull();

        var services = new ServiceCollection();
        method!.Invoke(null, [services]);

        var provider = services.BuildServiceProvider();

        var handlerType = assembly.GetType("TestApp.CreateOrderCommandHandler");
        handlerType.ShouldNotBeNull();

        var commandType = assembly.GetType("TestApp.CreateOrderCommand");
        commandType.ShouldNotBeNull();

        var interfaceType = typeof(Modulus.Mediator.Abstractions.ICommandHandler<>).MakeGenericType(commandType!);
        var resolved = provider.GetService(interfaceType);
        resolved.ShouldNotBeNull();
        resolved.ShouldBeOfType(handlerType!);
    }
}

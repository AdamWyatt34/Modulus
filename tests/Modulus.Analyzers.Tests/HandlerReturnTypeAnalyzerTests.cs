using Modulus.Analyzers.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Modulus.Analyzers.Tests;

public class HandlerReturnTypeAnalyzerTests
{
    private readonly HandlerReturnTypeAnalyzer _analyzer = new();

    [Fact]
    public async Task CommandHandler_ReturningTaskResult_NoDiagnostic()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Modulus.Mediator.Abstractions;

            public record MyCommand : ICommand;

            public class MyHandler : ICommandHandler<MyCommand>
            {
                public Task<Result> Handle(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(Result.Success());
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task CommandHandlerWithResult_ReturningTaskResultT_NoDiagnostic()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Modulus.Mediator.Abstractions;

            public record MyCommand : ICommand<int>;

            public class MyHandler : ICommandHandler<MyCommand, int>
            {
                public Task<Result<int>> Handle(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult<Result<int>>(42);
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task QueryHandler_ReturningTaskResultT_NoDiagnostic()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Modulus.Mediator.Abstractions;

            public record MyQuery : IQuery<string>;

            public class MyHandler : IQueryHandler<MyQuery, string>
            {
                public Task<Result<string>> Handle(MyQuery query, CancellationToken cancellationToken = default)
                    => Task.FromResult<Result<string>>("hello");
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task HandlerLikeClass_NotImplementingInterface_NoDiagnostic()
    {
        const string source = """
            using System.Threading.Tasks;

            public class MyHandler
            {
                public Task<string> Handle(string input)
                    => Task.FromResult(input);
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task AbstractHandler_NoDiagnostic()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Modulus.Mediator.Abstractions;

            public record MyCommand : ICommand;

            public abstract class BaseHandler : ICommandHandler<MyCommand>
            {
                public abstract Task<Result> Handle(MyCommand command, CancellationToken cancellationToken = default);
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task CommandHandler_ReturningTaskString_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Modulus.Mediator.Abstractions;

            public record MyCommand : ICommand;

            public class MyHandler : ICommandHandler<MyCommand>
            {
                public Task<string> Handle(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult("bad");
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("MOD002");
        diagnostics[0].GetMessage().ShouldContain("MyHandler");
    }

    [Fact]
    public async Task QueryHandler_ReturningTaskDto_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Modulus.Mediator.Abstractions;

            public record OrderDto(int Id);
            public record MyQuery : IQuery<OrderDto>;

            public class MyHandler : IQueryHandler<MyQuery, OrderDto>
            {
                public Task<OrderDto> Handle(MyQuery query, CancellationToken cancellationToken = default)
                    => Task.FromResult(new OrderDto(1));
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("MOD002");
    }

    [Fact]
    public async Task CommandHandlerWithResult_ReturningTaskInt_ReportsDiagnostic()
    {
        const string source = """
            using System.Threading;
            using System.Threading.Tasks;
            using Modulus.Mediator.Abstractions;

            public record MyCommand : ICommand<int>;

            public class MyHandler : ICommandHandler<MyCommand, int>
            {
                public Task<int> Handle(MyCommand command, CancellationToken cancellationToken = default)
                    => Task.FromResult(42);
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("MOD002");
    }

    [Fact]
    public async Task DomainEventHandler_NoDiagnostic()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Modulus.Mediator.Abstractions;

            public record MyEvent : IDomainEvent
            {
                public Guid Id { get; init; }
                public DateTime OccurredOnUtc { get; init; }
            }

            public class MyHandler : IDomainEventHandler<MyEvent>
            {
                public Task Handle(MyEvent domainEvent, CancellationToken cancellationToken = default)
                    => Task.CompletedTask;
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.ShouldBeEmpty();
    }
}

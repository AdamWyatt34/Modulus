using Modulus.Analyzers.Tests.Helpers;
using Shouldly;
using Xunit;

namespace Modulus.Analyzers.Tests;

public class ExceptionThrowingInHandlerTests
{
    private readonly ExceptionThrowingInHandlerAnalyzer _analyzer = new();

    private const string HandlerPreamble = """
        using System;
        using System.Threading;
        using System.Threading.Tasks;
        using Modulus.Mediator.Abstractions;

        public record MyCommand : ICommand;

        public class NotFoundException : Exception
        {
            public NotFoundException(string message) : base(message) { }
        }

        public class ValidationException : Exception
        {
            public ValidationException(string message) : base(message) { }
        }

        public class ConflictException : Exception
        {
            public ConflictException(string message) : base(message) { }
        }

        public class OrderNotFoundException : Exception
        {
            public OrderNotFoundException(string message) : base(message) { }
        }
        """;

    [Fact]
    public async Task ThrowNotFoundException_InHandler_ReportsDiagnostic()
    {
        var source = HandlerPreamble + """

            public class MyHandler : ICommandHandler<MyCommand>
            {
                public Task<Result> Handle(MyCommand command, CancellationToken cancellationToken = default)
                {
                    throw new NotFoundException("Not found");
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("MOD003");
        diagnostics[0].Severity.ShouldBe(Microsoft.CodeAnalysis.DiagnosticSeverity.Warning);
    }

    [Fact]
    public async Task ThrowValidationException_InHandler_ReportsDiagnostic()
    {
        var source = HandlerPreamble + """

            public class MyHandler : ICommandHandler<MyCommand>
            {
                public Task<Result> Handle(MyCommand command, CancellationToken cancellationToken = default)
                {
                    throw new ValidationException("Invalid input");
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("MOD003");
    }

    [Fact]
    public async Task ThrowConflictException_InHandler_ReportsDiagnostic()
    {
        var source = HandlerPreamble + """

            public class MyHandler : ICommandHandler<MyCommand>
            {
                public Task<Result> Handle(MyCommand command, CancellationToken cancellationToken = default)
                {
                    throw new ConflictException("Already exists");
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("MOD003");
    }

    [Fact]
    public async Task ThrowArgumentNullException_InHandler_NoDiagnostic()
    {
        var source = HandlerPreamble + """

            public class MyHandler : ICommandHandler<MyCommand>
            {
                public Task<Result> Handle(MyCommand command, CancellationToken cancellationToken = default)
                {
                    throw new ArgumentNullException(nameof(command));
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task ThrowInvalidOperationException_InHandler_NoDiagnostic()
    {
        var source = HandlerPreamble + """

            public class MyHandler : ICommandHandler<MyCommand>
            {
                public Task<Result> Handle(MyCommand command, CancellationToken cancellationToken = default)
                {
                    throw new InvalidOperationException("Something went wrong");
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task ThrowNotFoundException_OutsideHandler_NoDiagnostic()
    {
        var source = HandlerPreamble + """

            public class MyService
            {
                public void DoSomething()
                {
                    throw new NotFoundException("Not found");
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task BareReThrow_InHandler_NoDiagnostic()
    {
        var source = HandlerPreamble + """

            public class MyHandler : ICommandHandler<MyCommand>
            {
                public Task<Result> Handle(MyCommand command, CancellationToken cancellationToken = default)
                {
                    try { throw new NotFoundException("test"); }
                    catch { throw; }
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        // Only the first throw should be flagged, not the bare re-throw
        diagnostics.Length.ShouldBe(1);
    }

    [Fact]
    public async Task CustomOrderNotFoundException_InHandler_ReportsDiagnostic()
    {
        var source = HandlerPreamble + """

            public class MyHandler : ICommandHandler<MyCommand>
            {
                public Task<Result> Handle(MyCommand command, CancellationToken cancellationToken = default)
                {
                    throw new OrderNotFoundException("Order not found");
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.Length.ShouldBe(1);
        diagnostics[0].Id.ShouldBe("MOD003");
    }

    [Fact]
    public async Task ThrowGenericException_InHandler_NoDiagnostic()
    {
        var source = HandlerPreamble + """

            public class MyHandler : ICommandHandler<MyCommand>
            {
                public Task<Result> Handle(MyCommand command, CancellationToken cancellationToken = default)
                {
                    throw new Exception("Something failed");
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task Handler_NoThrow_NoDiagnostic()
    {
        var source = HandlerPreamble + """

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
    public async Task Handler_ThrowInLambda_NoDiagnostic()
    {
        var source = HandlerPreamble + """

            public class MyHandler : ICommandHandler<MyCommand>
            {
                public Task<Result> Handle(MyCommand command, CancellationToken cancellationToken = default)
                {
                    Func<Task> inner = async () => throw new NotFoundException("not found in lambda");
                    return Task.FromResult(Result.Success());
                }
            }
            """;

        var diagnostics = await AnalyzerTestHelper.GetDiagnosticsAsync(_analyzer, source);

        diagnostics.ShouldBeEmpty();
    }

    [Fact]
    public async Task CodeFix_ReplacesThrowWithReturnError()
    {
        var source = HandlerPreamble + """

            public class MyHandler : ICommandHandler<MyCommand>
            {
                public Task<Result> Handle(MyCommand command, CancellationToken cancellationToken = default)
                {
                    throw new NotFoundException("Not found");
                }
            }
            """;

        var fixedSource = await AnalyzerTestHelper.ApplyCodeFixAsync(
            _analyzer,
            new ExceptionThrowingInHandlerCodeFixProvider(),
            source,
            "MOD003");

        fixedSource.ShouldContain("return Error.NotFound(");
        fixedSource.ShouldNotContain("throw new NotFoundException");
    }
}

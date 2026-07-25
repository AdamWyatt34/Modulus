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
    public async Task ThrowNotFoundException_InRecordHandler_ReportsDiagnostic()
    {
        // `record`/`record class` handlers are a valid handler shape (the generator registers
        // them the same as classes); the containing-type walk used to look only for
        // ClassDeclarationSyntax and missed record handlers entirely.
        var source = HandlerPreamble + """

            public sealed record MyHandler : ICommandHandler<MyCommand>
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
    }

    [Fact]
    public async Task CodeFix_NonAsyncHandler_WrapsInTaskFromResult_AndCompiles()
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

        // A non-async `Task<Result> Handle(...)` can't `return Error.X(...)` directly — Error
        // doesn't convert to Task<Result> (CS0029) — so the fix must build the Task itself.
        fixedSource.ShouldContain("return Task.FromResult<Result>(Error.NotFound(");
        fixedSource.ShouldNotContain("throw new NotFoundException");

        AnalyzerTestHelper.CompileAndGetErrors(fixedSource).ShouldBeEmpty();
    }

    [Fact]
    public async Task CodeFix_AsyncHandler_ReturnsBareResult_AndCompiles()
    {
        var source = HandlerPreamble + """

            public class MyHandler : ICommandHandler<MyCommand>
            {
                public async Task<Result> Handle(MyCommand command, CancellationToken cancellationToken = default)
                {
                    await Task.Yield();
                    throw new NotFoundException("Not found");
                }
            }
            """;

        var fixedSource = await AnalyzerTestHelper.ApplyCodeFixAsync(
            _analyzer,
            new ExceptionThrowingInHandlerCodeFixProvider(),
            source,
            "MOD003");

        // Async methods return the inner Result — the compiler wraps it in Task<Result> itself,
        // so wrapping in Task.FromResult here would be redundant (and wrong: Task<Task<Result>>).
        fixedSource.ShouldContain("return Error.NotFound(");
        fixedSource.ShouldNotContain("Task.FromResult");
        fixedSource.ShouldNotContain("throw new NotFoundException");

        AnalyzerTestHelper.CompileAndGetErrors(fixedSource).ShouldBeEmpty();
    }

    [Fact]
    public async Task CodeFix_NonAsyncHandlerWithResultOfT_WrapsInTypedTaskFromResult_AndCompiles()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Modulus.Mediator.Abstractions;

            public record MyQuery : IQuery<int>;

            public class NotFoundException : Exception
            {
                public NotFoundException(string message) : base(message) { }
            }

            public class MyHandler : IQueryHandler<MyQuery, int>
            {
                public Task<Result<int>> Handle(MyQuery query, CancellationToken cancellationToken = default)
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

        fixedSource.ShouldContain("return Task.FromResult<Result<int>>(Error.NotFound(");
        fixedSource.ShouldNotContain("throw new NotFoundException");

        AnalyzerTestHelper.CompileAndGetErrors(fixedSource).ShouldBeEmpty();
    }

    [Fact]
    public async Task CodeFix_NonStringConstructorArgument_WrapsInInterpolation_AndCompiles()
    {
        const string source = """
            using System;
            using System.Threading;
            using System.Threading.Tasks;
            using Modulus.Mediator.Abstractions;

            public record MyCommand : ICommand;

            public class OrderNotFoundException : Exception
            {
                public int OrderId { get; }
                public OrderNotFoundException(int orderId) : base($"Order {orderId} not found") => OrderId = orderId;
            }

            public class MyHandler : ICommandHandler<MyCommand>
            {
                public Task<Result> Handle(MyCommand command, CancellationToken cancellationToken = default)
                {
                    int orderId = 42;
                    throw new OrderNotFoundException(orderId);
                }
            }
            """;

        var fixedSource = await AnalyzerTestHelper.ApplyCodeFixAsync(
            _analyzer,
            new ExceptionThrowingInHandlerCodeFixProvider(),
            source,
            "MOD003");

        fixedSource.ShouldContain("$\"{orderId}\"");
        fixedSource.ShouldNotContain("throw new OrderNotFoundException");

        AnalyzerTestHelper.CompileAndGetErrors(fixedSource).ShouldBeEmpty();
    }

    [Fact]
    public async Task CodeFix_ThrowExpression_NoFixOffered()
    {
        var source = HandlerPreamble + """

            public class MyHandler : ICommandHandler<MyCommand>
            {
                public Task<Result> Handle(MyCommand command, CancellationToken cancellationToken = default)
                {
                    var value = command ?? throw new NotFoundException("Not found");
                    return Task.FromResult(Result.Success());
                }
            }
            """;

        // Substituting a `return` statement at a throw-EXPRESSION's position (e.g. `x ?? throw
        // ...`) would produce an invalid syntax tree — the fix must not offer anything here.
        await Should.ThrowAsync<InvalidOperationException>(() =>
            AnalyzerTestHelper.ApplyCodeFixAsync(
                _analyzer,
                new ExceptionThrowingInHandlerCodeFixProvider(),
                source,
                "MOD003"));
    }
}

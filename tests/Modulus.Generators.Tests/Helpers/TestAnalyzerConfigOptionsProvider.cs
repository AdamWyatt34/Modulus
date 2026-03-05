using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

namespace Modulus.Generators.Tests.Helpers;

internal sealed class TestAnalyzerConfigOptionsProvider(string rootNamespace) : AnalyzerConfigOptionsProvider
{
    public override AnalyzerConfigOptions GlobalOptions { get; } = new TestGlobalOptions(rootNamespace);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) =>
        EmptyOptions.Instance;

    public override AnalyzerConfigOptions GetOptions(AdditionalText textFile) =>
        EmptyOptions.Instance;

    private sealed class TestGlobalOptions(string rootNamespace) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, out string value)
        {
            if (key == "build_property.RootNamespace")
            {
                value = rootNamespace;
                return true;
            }

            value = null!;
            return false;
        }
    }

    private sealed class EmptyOptions : AnalyzerConfigOptions
    {
        public static readonly EmptyOptions Instance = new();

        public override bool TryGetValue(string key, out string value)
        {
            value = null!;
            return false;
        }
    }
}

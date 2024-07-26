using System;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.IdentityModel.JsonWebTokens;

namespace AnalyzerWithDependencies;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class SampleAnalyzer : DiagnosticAnalyzer
{
    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics => [
        new DiagnosticDescriptor("AWD001", "Sample analyzer", "Hello {0}", "Design", DiagnosticSeverity.Warning, isEnabledByDefault: true)];

    public override void Initialize(AnalysisContext context)
    {
        context.EnableConcurrentExecution();
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.Analyze | GeneratedCodeAnalysisFlags.ReportDiagnostics);

        context.RegisterCompilationAction(ctx =>
        {
            var compilation = ctx.Compilation;
            // Exercise some external dependency
            var token = new JsonWebToken("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiIxMjM0NTY3ODkwIiwibmFtZSI6IkpvaG4gRG9lIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c");
            var name = token.GetPayloadValue<string>("name");   
            var diagnostic = Diagnostic.Create(SupportedDiagnostics[0], Location.None, name);
            ctx.ReportDiagnostic(diagnostic);
        });
    }
}

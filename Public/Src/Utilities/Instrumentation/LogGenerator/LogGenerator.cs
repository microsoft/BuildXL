// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.IO;
using Newtonsoft.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using BuildXL.LogGen.Core;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.CodeGenerationHelper;

using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;
using Diagnostic = Microsoft.CodeAnalysis.Diagnostic;

namespace BuildXL.LogGenerator
{
    /// <summary>
    /// C# Source Generator for generating BuildXL Logs.
    /// </summary>
    [Generator]
    public class LogGenerator : ISourceGenerator
    {
        private ErrorReport m_errorReport = new ();

        /// <inheritdoc/>
        public void Execute(GeneratorExecutionContext context)
        {
            try
            {
                m_errorReport = new SourceGeneratorErrorReporter(context);
                ExecuteCore(context);
            }
            catch(Exception e)
            {
                // Unhandled exceptions are not great when they happen during source generation
                // because it shows only ex.Message.
                // Reporting the full exception as an error instead.
                m_errorReport.ReportError($"Error generating logs: {e}");
            }
        }

        private void ExecuteCore(GeneratorExecutionContext context)
        {
            if (!TryGetConfiguration(context, out var configuration))
            {
                return;
            }
            
            if (context.SyntaxReceiver is not GeneratedLogSyntaxReceiver receiver)
            {
                // Need to log error here?
                return;
            }

            if (context.Compilation is not CSharpCompilation cSharpCompilation || cSharpCompilation.SyntaxTrees.FirstOrDefault()?.Options is not CSharpParseOptions)
            {
                return;
            }

            var compilation = context.Compilation;

            // Getting diagnostics per file that can be reported during log generation.
            Dictionary<string, IReadOnlyList<Diagnostic>> errorsPerFile = compilation.GetDiagnostics(context.CancellationToken)
                .Where(d => d.Severity == DiagnosticSeverity.Error)
                .Select(d => (filePath: d.Location.SourceTree?.FilePath, diagnostics: d))
                .Where(tpl => tpl.filePath != null)
                .ToMultiValueDictionary(tpl => tpl.filePath!, tpl => tpl.diagnostics);

            // Getting the candidate symbols that we should generate logs for.
            List<INamedTypeSymbol> candidates = receiver.Candidates.Select(
                classDeclaration =>
                {
                    var model = compilation.GetSemanticModel(classDeclaration.SyntaxTree);
                    return model.GetDeclaredSymbol(classDeclaration)!;
                }).ToList();

            // Generating logging classes
            ParserHelpers.TryGenerateLoggingClasses(
                candidates,
                errorsPerFile,
                m_errorReport,
                configuration.Aliases.ToDictionary(a => a.Key, a => a.Value),
                out var loggingClasses);

            // Generate the new log source
            var declaration = GenerateLogFile(loggingClasses, configuration);
            var parsedOutput = ParseCompilationUnit(declaration);
            var final = parsedOutput.NormalizeWhitespace().ToFullString();
            
            // We don't have to have unique file name here, because we know that the log generator is unique per project.
            context.AddSource("Log.g.cs", final);
        }

        /// <inheritdoc/>
        public void Initialize(GeneratorInitializationContext context)
        {
            context.RegisterForSyntaxNotifications(() => new GeneratedLogSyntaxReceiver());
        }

        private bool TryGetConfiguration(GeneratorExecutionContext context, [NotNullWhen(true)] out LogGenConfiguration? configuration)
        {
            configuration = null;
            if (context.AdditionalFiles.IsEmpty)
            {
                m_errorReport.ReportError("LogGen config file is missing. It should be passed as 'AdditionalFiles'.");
            }

            var file = context.AdditionalFiles[0];
            var content = file.GetText()?.ToString();
            if (string.IsNullOrEmpty(content))
            {
                m_errorReport.ReportError($"The LogGen config file ({file.Path}) is empty.");
                return false;
            }

            try
            {
                configuration = JsonConvert.DeserializeObject<LogGenConfiguration>(content!);
                if (configuration == null)
                {
                    m_errorReport.ReportError($"LogGen unable to parse ({file.Path}). JSON: {content}.");
                }

                return configuration != null;
            }
            catch (Exception e)
            {
                m_errorReport.ReportError($"LogGen config file ({file.Path}) parsing error: {e}.");
                return false;
            }
        }

        private string GenerateLogFile(List<LoggingClass> loggingClasses, LogGenConfiguration configuration)
        {
            string logFile;
            List<GeneratorBase> generators = new();

            using (MemoryStream ms = new MemoryStream())
            using (StreamWriter writer = new StreamWriter(ms))
            {
                CodeGenerator gen = new CodeGenerator(c => writer.Write(c));
                
                LogWriterHelpers.WriteLogToStream(loggingClasses, gen, configuration.GenerationNamespace, configuration.TargetFramework, configuration.TargetRuntime, ref generators, m_errorReport);

                writer.Flush();
                ms.Seek(0, SeekOrigin.Begin);
                using (StreamReader sr = new StreamReader(ms))
                {
                    logFile = sr.ReadToEnd();
                }
            }

            return logFile;
        }
    }
}

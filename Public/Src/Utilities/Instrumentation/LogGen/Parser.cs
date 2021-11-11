// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.LogGen.Generators;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Diagnostic = Microsoft.CodeAnalysis.Diagnostic;
using EventGenerators = BuildXL.Utilities.Instrumentation.Common.Generators;
using BuildXL.LogGen.Core;

namespace BuildXL.LogGen
{
    internal sealed class Parser
    {
        private readonly Configuration m_configuration;
        private readonly ErrorReport m_errorReport;

        internal Parser(Configuration configuration, ErrorReport errorReport)
        {
            m_configuration = configuration;
            m_errorReport = errorReport;
        }

        public bool DiscoverLoggingSites(out List<LoggingClass> loggingClasses)
        {
            loggingClasses = null;
            
            // First create a compilation to act upon values and run codegen
            var syntaxTrees = new ConcurrentBag<SyntaxTree>();

            CSharpParseOptions opts = new CSharpParseOptions(
                preprocessorSymbols: m_configuration.PreprocessorDefines.ToArray(),
                languageVersion: LanguageVersion.Latest);

            Parallel.ForEach(
                m_configuration.SourceFiles.Distinct(StringComparer.OrdinalIgnoreCase),
                file =>
                {
                    if (File.Exists(file))
                    {
                        string text = File.ReadAllText(file);
                        syntaxTrees.Add(CSharpSyntaxTree.ParseText(text, path: file, options: opts));
                    }
                });

            var metadataFileReferences = new ConcurrentBag<MetadataReference>();
            Parallel.ForEach(
                m_configuration.References.Distinct(StringComparer.OrdinalIgnoreCase),
                reference =>
                {
                    if (File.Exists(reference))
                    {
                        metadataFileReferences.Add(MetadataReference.CreateFromFile(reference));
                    }
                });

            if (m_errorReport.Errors != 0)
            {
                return false;
            }

            Compilation compilation = CSharpCompilation.Create(
                "temp",
                syntaxTrees,
                metadataFileReferences,
                new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    deterministic: true
                )
            );

            // Hold on to all of the errors. Most are probably ok but some may be relating to event definitions and
            // should cause errors
            MultiValueDictionary<string, Diagnostic> errorsByFile = new MultiValueDictionary<string, Diagnostic>(StringComparer.OrdinalIgnoreCase);
            foreach (Diagnostic d in compilation.GetDiagnostics())
            {
                if (d.Location == null || d.Location.SourceTree == null)
                {
                    continue; // TODO
                }

                if (d.Severity == DiagnosticSeverity.Error)
                {
                    Console.WriteLine(d.ToString());
                    errorsByFile.Add(d.Location.SourceTree.FilePath, d);
                }
            }

            var symbols = new List<INamedTypeSymbol>();

            ParserHelpers.FindTypesInNamespace(compilation.Assembly.GlobalNamespace, (symbol) => true, symbols, true);
            return ParserHelpers.TryGenerateLoggingClasses(symbols, errorsByFile, m_errorReport, m_configuration.Aliases, out loggingClasses);
        }
    }
}

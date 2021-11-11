// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Linq;
using BuildXL.LogGen.Core;
using Microsoft.CodeAnalysis;

namespace BuildXL.LogGenerator
{
    internal sealed class SourceGeneratorErrorReporter : ErrorReport
    {
        private static readonly DiagnosticDescriptor s_descriptor = new DiagnosticDescriptor(
#pragma warning disable RS2008 // Enable analyzer release tracking
            "LOG001",
#pragma warning restore RS2008 // Enable analyzer release tracking
            "Error during log generation",
            "{0}",
            "Correctness",
            DiagnosticSeverity.Error,
            isEnabledByDefault: true);

        private GeneratorExecutionContext m_generatorExecutionContext;

        public SourceGeneratorErrorReporter(GeneratorExecutionContext generatorExecutionContext) 
            => m_generatorExecutionContext = generatorExecutionContext;

        /// <inheritdoc />
        protected override void ReportErrorCore(string format, params object[] args)
        {
            m_generatorExecutionContext.ReportDiagnostic(
                Diagnostic.Create(s_descriptor, location: null, messageArgs: SafeFormat(format, args)));
        }

        /// <inheritdoc />
        protected override void ReportErrorCore(ISymbol symbol, string errorFormat, params object[] args)
        {
            m_generatorExecutionContext.ReportDiagnostic(
                Diagnostic.Create(s_descriptor, location: symbol.Locations.FirstOrDefault(), messageArgs: SafeFormat(errorFormat, args)));
        }
    }
}
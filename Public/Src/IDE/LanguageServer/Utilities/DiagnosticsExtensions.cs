// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.Types;
using TypeScript.Net.Utilities;
using Diagnostic = Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic;
using Range = Microsoft.VisualStudio.LanguageServer.Protocol.Range;

namespace BuildXL.Ide.LanguageServer
{
    /// <summary>
    /// Extensions to use for <see cref="Range"/> class.
    /// </summary>
    public static class DiagnosticsExtensions
    {
        /// <nodoc />
        public static Diagnostic ToProtocolDiagnostic(this TypeScript.Net.Diagnostics.Diagnostic semanticDiagnostic, string source = "DScript")
        {
            return new Microsoft.VisualStudio.LanguageServer.Protocol.Diagnostic
            {
                // diag.code = 0; This can be omitted according to protocol spec
                Source = source,
                Message = semanticDiagnostic.MessageText.ToString(),
                Severity = WorkspaceSeverityToDiagnostic(semanticDiagnostic.Category),
                Range = semanticDiagnostic.GetRange(),
            };
        }

        /// <nodoc />
        public static Range GetRange(this TypeScript.Net.Diagnostics.Diagnostic diagnostic)
        {
            Contract.Requires(diagnostic.File != null, "GetRange is valid only for file-based diagnostics.");
            var sourceFile = diagnostic.File;
            var lineAndColumn = diagnostic.GetLineAndColumn(sourceFile);


            return ToRange(lineAndColumn, sourceFile.LineMap, diagnostic.Length);
        }

        /// <nodoc />
        public static Range ToRange(this LineAndColumn lineAndColumn, LineMap lineMap, int? length)
        {
            var start = lineAndColumn.ToPosition();

            // Compute the absolute end of the range by getting the absolute start and offseting that with the length
            var absoluteStart = lineMap.Map[lineAndColumn.Line - 1] + lineAndColumn.Character - 1;
            var absoluteEnd = absoluteStart + length ?? 0;

            var end = LineInfo.FromLineMap(lineMap, absoluteEnd).ToPosition();

            return new Range { Start = start, End = end };
        }

        /// <nodoc />
        public static Range ToRange(this INode node)
        {
            return new Range()
            {
                Start = node.GetStartPosition(),
                End = node.GetEndPosition(),
            };
        }

        /// <nodoc />
        public static Range ToRange(this ISourceFile spec)
        {
            return new Range
            {
                Start = new Position {Character = 0, Line = 0},
                End = LineInfoExtensions.GetLineAndColumnBy(spec.End, spec, false).ToPosition(),
            };
        }

        /// <nodoc />
        public static Range ToRange(this ITextSpan textSpan, ISourceFile sourceFile)
        {
            return new Range()
            {
                Start = textSpan.GetStartPosition(sourceFile),
                End = textSpan.GetEndPosition(sourceFile),
            };
        }

        private static DiagnosticSeverity WorkspaceSeverityToDiagnostic(DiagnosticCategory diagnosticCategory)
        {
            switch (diagnosticCategory)
            {
                case DiagnosticCategory.Error:
                    return DiagnosticSeverity.Error;
                case DiagnosticCategory.Warning:
                    return DiagnosticSeverity.Warning;
                case DiagnosticCategory.Message:
                    return DiagnosticSeverity.Information;
                default:
                    throw new ArgumentOutOfRangeException(nameof(diagnosticCategory), diagnosticCategory, null);
            }
        }
    }
}

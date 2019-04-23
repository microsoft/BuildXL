// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.FrontEnd.Script.Analyzer.Tracing;
using BuildXL.FrontEnd.Script.Analyzer.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Script.Analyzer.Analyzers
{
    /// <summary>
    /// Pretty Print analyzer
    /// </summary>
    public class PrettyPrint : Analyzer
    {
        /// <inheritdoc />
        public override AnalyzerKind Kind { get; } = AnalyzerKind.PrettyPrint;

        /// <summary>
        /// Indicates if special rules for addIf formatting should be enabled or not.
        /// </summary>
        public bool SpecialAddIfFormatting { get; private set; }

        /// <summary>
        /// Allows derived classes to create a custom pretty printer
        /// </summary>
        protected virtual INodeVisitor GetPrettyPrintVisitor(ScriptWriter writer)
        {
            return new DScriptPrettyPrintVisitor(writer)
            {
                SpecialAddIfFormatting = SpecialAddIfFormatting,
            };
        }

        /// <inheritdoc />
        public override bool HandleOption(CommandLineUtilities.Option opt)
        {
            if (string.Equals("specialAddIfFormatting", opt.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals("specialAddIfFormatting+", opt.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals("s", opt.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals("s+", opt.Name, StringComparison.OrdinalIgnoreCase))
            {
                SpecialAddIfFormatting = true;
                return true;
            }

            if (string.Equals("specialAddIfFormatting-", opt.Name, StringComparison.OrdinalIgnoreCase) ||
                string.Equals("s-", opt.Name, StringComparison.OrdinalIgnoreCase))
            {
                SpecialAddIfFormatting = false;
                return true;
            }

            return base.HandleOption(opt);
        }

        /// <inheritdoc />
        public override void WriteHelp(HelpWriter writer)
        {
            writer.WriteOption("specialAddIfFormatting[+|-]", "Whether addIf functions should be printed in a non-standard way.", shortName: "s");
            base.WriteHelp(writer);
        }

        /// <inheritdoc />
        public override bool AnalyzeSourceFile(Workspace workspace, AbsolutePath path, ISourceFile sourceFile)
        {
            var filePath = path.ToString(PathTable, PathFormat.HostOs);

            using (var writer = new ScriptWriter())
            {
                var visitor = GetPrettyPrintVisitor(writer);
                sourceFile.Cast<IVisitableNode>().Accept(visitor);
                var formattedText = writer.ToString();

                string existingText = sourceFile.Text.Substring(0, sourceFile.Text.Length);

                bool matches = string.Equals(existingText, formattedText, StringComparison.Ordinal);
                if (!matches)
                {
                    if (Fix)
                    {
                        try
                        {
                            File.WriteAllText(filePath, formattedText);
                        }
                        catch (IOException ex)
                        {
                            Logger.PrettyPrintErrorWritingSpecFile(LoggingContext, new Location() { File = filePath }, ex.Message);
                            return false;
                        }
                        catch (UnauthorizedAccessException ex)
                        {
                            Logger.PrettyPrintErrorWritingSpecFile(LoggingContext, new Location() { File = filePath }, ex.Message);
                            return false;
                        }
                    }
                    else
                    {
                        ReportFirstDifference(Logger, LoggingContext, existingText, formattedText, filePath);
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// Helper to print
        /// </summary>
        public static void ReportFirstDifference(Logger logger, LoggingContext loggingContext, string existingText, string formattedText, string filePath)
        {
            var sourceLines = existingText.Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            var targetLines = formattedText.Split(new[] { Environment.NewLine }, StringSplitOptions.None);

            var minLines = Math.Min(sourceLines.Length, targetLines.Length);
            for (int i = 0; i < minLines; i++)
            {
                var sourceLine = sourceLines[i];
                var targetLine = targetLines[i];
                if (!string.Equals(sourceLine, targetLine, StringComparison.Ordinal))
                {
                    var minChars = Math.Min(sourceLine.Length, targetLine.Length);
                    for (int j = 0; j < minChars; j++)
                    {
                        if (sourceLine[j] != targetLine[j])
                        {
                            logger.PrettyPrintUnexpectedChar(
                                loggingContext,
                                new Location { File = filePath, Line = i + 1, Position = j + 1 },
                                targetLine[j].ToString(),
                                sourceLine[j].ToString(),
                                sourceLine,
                                new string(' ', j) + '^');
                            return;
                        }
                    }

                    var lineToPrint = sourceLine.Length > targetLine.Length ? sourceLine : targetLine;
                    var lineMarkerPosition = sourceLine.Length > targetLine.Length ? targetLine.Length : sourceLine.Length;
                    var expectedCharacter = sourceLine.Length > targetLine.Length ? "<newline>" : targetLine.Substring(sourceLine.Length);
                    var encounteredCharacter = sourceLine.Length > targetLine.Length ? sourceLine.Substring(targetLine.Length) : "<newline>";

                    var endOfLineMarker = new string(' ', lineMarkerPosition) + '^';
                    logger.PrettyPrintUnexpectedChar(
                        loggingContext,
                        new Location { File = filePath, Line = i + 1, Position = lineMarkerPosition },
                        expectedCharacter,
                        encounteredCharacter,
                        lineToPrint,
                        endOfLineMarker);
                    return;
                }
            }

            if (sourceLines.Length > targetLines.Length)
            {
                logger.PrettyPrintExtraSourceLines(loggingContext, new Location { File = filePath, Line = targetLines.Length }, sourceLines[targetLines.Length]);
            }
            else if (sourceLines.Length < targetLines.Length)
            {
                logger.PrettyPrintExtraTargetLines(loggingContext, new Location { File = filePath, Line = sourceLines.Length }, targetLines[sourceLines.Length]);
            }
            else
            {
                Contract.Assert(false, "No differences detected..., why was this method called?");
            }
        }

        /// <summary>
        /// Runs DScript pretty-printer on given <paramref name="node"/> to return its formatted string representation.
        /// </summary>
        public static string GetFormattedText(INode node)
        {
            Contract.Requires(node != null);

            var scriptWriter = new ScriptWriter();
            var visitor = new DScriptPrettyPrintVisitor(scriptWriter, false);
            node.Cast<IVisitableNode>().Accept(visitor);
            return scriptWriter.ToString();
        }
    }
}

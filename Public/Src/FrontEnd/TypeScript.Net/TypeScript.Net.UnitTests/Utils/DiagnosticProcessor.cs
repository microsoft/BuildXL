// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.Types;
using TypeScript.Net.Utilities;
using Xunit;

namespace TypeScript.Net.UnitTests.Utils
{
    /// <summary>
    /// Encapsulates the relative path and content of a typescript test file.
    /// </summary>
    public readonly struct TestFile
    {
        public TestFile(string unitName, string content)
        {
            UnitName = unitName;
            Content = content;
        }

        public readonly string UnitName;
        public readonly string Content;
    }

    /// <summary>
    /// Helper class that processes diagnostics to produce an errors.txt style of string blob.
    /// </summary>
    public static class DiagnosticProcessor
    {
        /// <summary>
        /// Pattern matcher for full paths.
        /// </summary>
        /// <remarks>
        /// Full paths either start with a drive letter or / for *nix, shouldn't have \ in the path at this point
        /// </remarks>
        private static readonly Regex s_fullPath = new Regex(@"(\w+:|\/)?([\w+\-\.]|\/)*\.tsx?");

        public static void CheckTestCodeOutput(string fileName)
        {
            throw new NotImplementedException();
        }

        public static string GetErrorBaseline(TestFile inputFile, List<Diagnostic> diagnostics)
        {
            return GetErrorBaseline(new[] { inputFile }, diagnostics);
        }

        public static string GetErrorBaseline(IEnumerable<TestFile> inputFiles, List<Diagnostic> diagnostics)
        {
            diagnostics.Sort(DiagnosticUtilities.DiagnosticComparer.Instance);

            List<string> outputLines = new List<string>();

            // Count up all errors that were found in files other than lib.d.ts so we don't miss any
            int totalErrorsReportedInNonLibraryFiles = 0;

            // Report global errors
            var globalErrors = diagnostics.Where(err => err.File == null);
            foreach (var g in globalErrors)
            {
                OutputErrorText(g, outputLines, ref totalErrorsReportedInNonLibraryFiles);
            }

            // 'merge' the lines of each input file with any errors associated with it
            foreach (var inputFile in inputFiles.Where(f => !string.IsNullOrEmpty(f.Content)))
            {
                // Filter down to the errors in the file
                IEnumerable<Diagnostic> fileErrors = diagnostics.Where(e =>
                    (e.File != null) && (e.File.FileName == inputFile.UnitName));

                // Header
                outputLines.Add("==== " + inputFile.UnitName + " (" + fileErrors.Count() + " errors) ====");

                // Make sure we emit something for every error
                int markedErrorCount = 0;

                var lineStarts = LineMap.ComputeLineStarts(inputFile.Content);
                var lines = inputFile.Content.Split(new char[] { '\n' });
                if (lines.Length == 1)
                {
                    lines = lines[0].Split(new char[] { '\r' });
                }

                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    string line = lines[lineIndex];

                    if (line.Length > 0 && line[line.Length - 1] == '\r')
                    {
                        line = line.Substring(0, line.Length - 1);
                    }

                    int thisLineStart = lineStarts[lineIndex];

                    // On the last line of the file, fake the next line start number so that we handle errors on the last character of the file correctly
                    int nextLineStart = (lineIndex == lines.Length - 1) ?
                                            inputFile.Content.Length :
                                            lineStarts[lineIndex + 1];

                    // Emit this line from the original file
                    outputLines.Add("    " + line);

                    foreach (Diagnostic err in fileErrors)
                    {
                        // Does any error start or continue on to this line? Emit squiggles
                        int end = err.TextSpanEnd;

                        if ((end >= thisLineStart) &&
                            ((err.Start < nextLineStart) || (lineIndex == lines.Length - 1)))
                        {
                            // How many characters from the start of this line the error starts at (could be positive or negative)
                            int relativeOffset = err.Start.Value - thisLineStart;

                            // How many characters of the error are on this line (might be longer than this line in reality)
                            int length = (end - err.Start.Value) - Math.Max(0, thisLineStart - err.Start.Value);

                            // Calculate the start of the squiggle
                            int squiggleStart = Math.Max(0, relativeOffset);

                            StringBuilder builder = new StringBuilder();

                            // outputLines.push("    " + line.substr(0, squiggleStart).replace(/[^\s]/g, " ") + new Array(Math.min(length, line.length - squiggleStart) + 1).join("~"));
                            builder.Append("    ");
                            builder.Append(new string(' ', squiggleStart));
                            builder.Append(new string('~', Math.Min(length, line.Length - squiggleStart)));

                            outputLines.Add(builder.ToString());

                            // If the error ended here, or we're at the end of the file, emit its message
                            if ((lineIndex == lines.Length - 1) ||
                                (nextLineStart > end))
                            {
                                OutputErrorText(err, outputLines, ref totalErrorsReportedInNonLibraryFiles);
                                markedErrorCount++;
                            }
                        }
                    }
                }

                Assert.Equal(markedErrorCount, fileErrors.Count());
            }

            // TODO: Do we need to implement IsBuiltFile?
            var numLibraryDiagnostics = diagnostics.Where(diagnostic =>
                diagnostic.File != null &&
                IsLibraryFile(diagnostic.File.FileName)).Count();

            var numTest262HarnessDiagnostics = diagnostics.Where(diagnostic =>
                diagnostic.File != null &&
                diagnostic.File.FileName.Contains("test262-harness")).Count();

            // Verify we didn't miss any errors in total
            Assert.Equal(
                totalErrorsReportedInNonLibraryFiles + numLibraryDiagnostics + numTest262HarnessDiagnostics,
                diagnostics.Count);

            StringBuilder result = new StringBuilder();
            result
                .Append(MinimalDiagnosticsToString(diagnostics))
                .AppendLine()
                .AppendLine()
                .Append(string.Join("\r\n", outputLines));

            return result.ToString();
        }

        private static void OutputErrorText(
            Diagnostic error,
            List<string> outputLines,
            ref int totalErrorsReportedInNonLibraryFiles)
        {
            string message = error.MessageText.ToString();

            IEnumerable<string> errLines = RemoveFullPaths(message)
                .Split(new char[] { '\n' })
                .Select(s =>
                    (s.Length > 0 && s[s.Length - 1] == '\r') ?
                        s.Substring(0, s.Length - 1) :
                        s)
                .Where(s => s.Length > 0)
                .Select(s => "!!! " + error.Category.ToString().ToLower() + " TS" + error.Code + ": " + s);

            foreach (string e in errLines)
            {
                outputLines.Add(e);
            }

            // Do not count errors from lib.d.ts here, they are computed separately as numLibraryDiagnostics
            // If lib.d.ts is explicitly included in input files and there are some errors in it (i.e., because of duplicate identifiers)
            // then they will be added twice thus triggering 'total errors' assertion with condition
            // 'totalErrorsReportedInNonLibraryFiles + numLibraryDiagnostics + numTest262HarnessDiagnostics, diagnostics.length
            if (error.File == null || !IsLibraryFile(error.File.FileName))
            {
                totalErrorsReportedInNonLibraryFiles++;
            }
        }

        private static string MinimalDiagnosticsToString(List<Diagnostic> diagnostics)
        {
            StringBuilder errorOutput = new StringBuilder();

            foreach (var diagnostic in diagnostics)
            {
                if (diagnostic.File != null)
                {
                    LineAndColumn lineAndCharacter = Scanning.Scanner.GetLineAndCharacterOfPosition(diagnostic.File, diagnostic.Start.Value);

                    errorOutput
                        .Append(diagnostic.File.FileName)
                        .Append("(")
                        .Append(lineAndCharacter.Line)
                        .Append(",")
                        .Append(lineAndCharacter.Character)
                        .Append("): ");
                }

                errorOutput
                    .Append(diagnostic.Category.ToString().ToLower())
                    .Append(" TS")
                    .Append(diagnostic.Code)
                    .Append(": ")
                    .Append(diagnostic.MessageText.ToString())
                    .AppendLine();
            }

            return errorOutput.ToString();
        }

        /// <summary>
        /// Replaces instances of full paths with fileNames only.
        /// </summary>
        /// <remarks>
        /// See removeFullPaths in runnerbase.ts
        /// </remarks>
        private static string RemoveFullPaths(string path)
        {
            string fixedPath = path;

            MatchCollection fullPathMatches = s_fullPath.Matches(fixedPath);

            if (fullPathMatches.Count > 0)
            {
                foreach (Match match in fullPathMatches)
                {
                    fixedPath = fixedPath.Replace(match.Value, Path.GetFileName(match.Value));
                }
            }

            return fixedPath;
        }

        private static bool IsLibraryFile(string filePath)
        {
            return (Path.GetFileName(filePath) == "lib.d.ts") ||
                   (Path.GetFileName(filePath) == "lib.core.d.ts");
        }
    }
}

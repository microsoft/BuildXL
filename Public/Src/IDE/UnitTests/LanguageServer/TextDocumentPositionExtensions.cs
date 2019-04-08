// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using Microsoft.VisualStudio.LanguageServer.Protocol;
using System;
using System.Text.RegularExpressions;
using BuildXL.Ide.LanguageServer.UnitTests.Helpers;

namespace BuildXL.Ide.LanguageServer.UnitTests
{
    internal static class TextDocumentPositionExtensions
    {
        /// <summary>
        /// Regex for parsing file and position string.
        /// The regex structure is: 
        /// .+? - non-greedy anything
        /// (
        /// any number of digits
        /// ,
        /// any number of digits
        /// )
        /// </summary>
        private static Regex s_parseRegex = new Regex(@"(?<file>.+?)\((?<line>\d+),(?<column>\d+)\)");

        /// <summary>
        /// Converts a string with file and position inside of it to <see cref="TextDocumentPositionParams"/>.
        /// </summary>
        /// <remarks>
        /// Expected format of the <paramref name="fileAndPosition"/> is fileName(line,column).
        /// </remarks>
        public static TextDocumentPositionParams ToParams(this string fileAndPosition, WorkspaceLoaderTestFixture fixture)
        {
            var (file, line, column) = Parse();

            // In the given format string, the line and column starts with 1, so need to extract 1 from both of them.
            return new TextDocumentPositionParams()
            {
                // type optionA = "Opt{caret}ionA";
                Position = new Position()
                {
                    Line = line - 1,
                    Character = column - 1,
                },
                TextDocument = new TextDocumentIdentifier()
                {
                    Uri = fixture.GetChildUri(file).ToString()
                }
            };

            (string file, int line, int column) Parse()
            {
                
                var matches = s_parseRegex.Match(fileAndPosition);
                if (!matches.Success)
                {
                    throw new InvalidOperationException($"Can't parse '{fileAndPosition}'. Expected format is 'file(line,column)'");
                }

                var groups = matches.Groups;
                
                return (groups["file"].Value, int.Parse(groups["line"].Value), int.Parse(groups["column"].Value));
            }
        }
    }
}

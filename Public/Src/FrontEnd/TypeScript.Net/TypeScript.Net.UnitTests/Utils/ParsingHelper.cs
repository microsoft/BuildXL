// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using TypeScript.Net.Binding;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.DScript;
using TypeScript.Net.Extensions;
using TypeScript.Net.Parsing;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using Xunit;

namespace TypeScript.Net.UnitTests.Utils
{
    /// <summary>
    /// Helper class that simplifies parsing of the source files.
    /// </summary>
    internal static class ParsingHelper
    {
        /// <summary>
        /// Parses specified <paramref name="code"/> and returns the all statements from the <see cref="ISourceFile"/>.
        /// </summary>
        /// <remarks>
        /// If <paramref name="roundTripTesting"/> is true, <see cref="ParseSourceFileWithRoundTrip"/> would be called to get
        /// source file from the string representation.
        /// </remarks>
        public static List<IStatement> ParseStatementsFrom(string code, bool roundTripTesting = true, ParsingOptions parsingOptions = null)
        {
            Contract.Requires(code != null);
            Contract.Ensures(Contract.Result<List<IStatement>>() != null);

            var sourceFile = roundTripTesting
                ? ParseSourceFileWithRoundTrip(code, parsingOptions)
                : ParseSourceFile(code, parsingOptions: parsingOptions);
            return sourceFile.Statements.ToList();
        }

        /// <summary>
        /// Parses specified <paramref name="code"/> and returns the first statement from the <see cref="ISourceFile"/>.
        /// </summary>
        /// <remarks>
        /// If <paramref name="roundTripTesting"/> is true, <see cref="ParseSourceFileWithRoundTrip"/> would be called to get
        /// source file from the string representation.
        /// </remarks>
        public static TNode ParseFirstStatementFrom<TNode>(string code, bool roundTripTesting = true, ParsingOptions parsingOptions = null) where TNode : INode
        {
            Contract.Requires(code != null);
            Contract.Ensures(Contract.Result<TNode>() != null);

            var result = (TNode)ParseStatementsFrom(code, roundTripTesting, parsingOptions).First();
            return result;
        }

        /// <summary>
        /// Parses specified <paramref name="code"/> and returns the second statement form the <see cref="ISourceFile"/>.
        /// </summary>
        /// <remarks>
        /// If <paramref name="roundTripTesting"/> is true, <see cref="ParseSourceFileWithRoundTrip"/> would be called to get
        /// source file from the string representation.
        /// </remarks>
        public static TNode ParseSecondStatementFrom<TNode>(string code, bool roundTripTesting = true, ParsingOptions parsingOptions = null) where TNode : INode
        {
            Contract.Requires(code != null);
            Contract.Ensures(Contract.Result<TNode>() != null);

            return (TNode)ParseStatementsFrom(code, roundTripTesting, parsingOptions).Skip(1).First();
        }

        /// <summary>
        /// Parses specified expression statement <paramref name="code"/> and returns the contained expression.
        /// </summary>
        /// <remarks>
        /// If <paramref name="roundTripTesting"/> is true, <see cref="ParseSourceFileWithRoundTrip"/> would be called to get
        /// source file from the string representation.
        /// </remarks>
        public static TNode ParseExpressionStatement<TNode>(string code, bool roundTripTesting = true, ParsingOptions parsingOptions = null) where TNode : class, INode
        {
            Contract.Requires(code != null);
            Contract.Ensures(Contract.Result<TNode>() != null);

            return ((IExpressionStatement)ParseStatementsFrom(code, roundTripTesting, parsingOptions).First()).Expression.Cast<TNode>();
        }

        /// <summary>
        /// Parses specified <paramref name="code"/> and returns a set of parse errors.
        /// </summary>
        public static IReadOnlyList<Diagnostic> ParseAndGetDiagnostics(string code, ParsingOptions parsingOptions = null)
        {
            Contract.Requires(code != null);
            Contract.Ensures(Contract.Result<IReadOnlyList<Diagnostic>>() != null);

            var parser = new Parser();

            ISourceFile node = parser.ParseSourceFile("fakeFileName.dsc", code, ScriptTarget.Es2015, syntaxCursor: null,
                setParentNodes: true, parsingOptions: parsingOptions);

            return parser.ParseDiagnostics;
        }

        /// <summary>
        /// Parses specified <paramref name="code"/> into <see cref="ISourceFile"/>.
        /// </summary>
        public static ISourceFile ParseSourceFile(string code, string fileName = "fakeFileName.dsc", ParsingOptions parsingOptions = null, Parser parser = null)
        {
            Contract.Requires(code != null);
            Contract.Ensures(Contract.Result<ISourceFile>() != null);

            parser = parser ?? new Parser();

            ISourceFile node = parser.ParseSourceFile(fileName, code, ScriptTarget.Es2015, syntaxCursor: null,
                setParentNodes: true, parsingOptions: parsingOptions);

            // Source file should not be null
            Assert.NotNull(node);

            if (parser.ParseDiagnostics.Count != 0)
            {
                var message = string.Join("\r\n", parser.ParseDiagnostics.Select(d => d.MessageText));
                throw new Exception($"Parsing failed. Diagnostics:\r\n{message}");
            }

            var binder = new Binder();

            binder.BindSourceFile(node, new CompilerOptions());

            if (node.BindDiagnostics.Count != 0)
            {
                var message = string.Join("\r\n", node.BindDiagnostics.Select(d => d.MessageText));
                throw new Exception($"Binding failed. Diagnostics:\r\n{message}");
            }

            return node;
        }

        /// <summary>
        /// Parses specified <paramref name="code"/> into <see cref="ISourceFile"/>.
        /// This function will not throw if parsing or binding will fail.
        /// </summary>
        public static ISourceFile ParsePotentiallyBrokenSourceFile(string code, string fileName = "fakeFileName.ts", ParsingOptions parsingOptions = null)
        {
            Contract.Requires(code != null);
            Contract.Ensures(Contract.Result<ISourceFile>() != null);

            var parser = new Parser();

            parsingOptions = parsingOptions?.WithFailOnMissingSemicolons(false);
            ISourceFile node = parser.ParseSourceFile(fileName, code, ScriptTarget.Es2015, syntaxCursor: null, setParentNodes: true, parsingOptions: parsingOptions);

            // Source file should not be null
            Assert.NotNull(node);

            var binder = new Binder();
            binder.BindSourceFile(node, new CompilerOptions());

            return node;
        }

        /// <summary>
        /// Parses specified <paramref name="code"/> and ensures that code could be parsed more than once and it still provides correct results.
        /// </summary>
        public static ISourceFile ParseSourceFileWithRoundTrip(string code, ParsingOptions parsingOptions = null)
        {
            Contract.Requires(code != null);
            Contract.Ensures(Contract.Result<ISourceFile>() != null);

            var sourceFile = ParseSourceFile(code);
            var sourceFileAsText = sourceFile.GetFormattedText();

            var secondSourceFile = ParseSourceFile(sourceFileAsText, parsingOptions: parsingOptions);
            var secondSourceFileAsText = secondSourceFile.GetFormattedText();

            Assert.Equal(sourceFileAsText, secondSourceFileAsText);

            return sourceFile;
        }
    }
}

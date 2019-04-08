// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using TypeScript.Net.DScript;
using TypeScript.Net.Extensions;
using TypeScript.Net.Parsing;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;

#pragma warning disable SA1649 // File name must match first type name

namespace TypeScript.Net.Api
{
    /// <summary>
    /// This class contains entry points that are guaranteed to be forwards compatible.
    /// </summary>
    public static class Helpers
    {
        private static readonly CompilerOptions s_publicCompilerOptions = new CompilerOptions();

        private static readonly ParsingOptions s_publicParsingOptions = new ParsingOptions(
            namespacesAreAutomaticallyExported: true,
            generateWithQualifierFunctionForEveryNamespace: false,
            preserveTrivia: false,
            allowBackslashesInPathInterpolation: true,
            useSpecPublicFacadeAndAstWhenAvailable: false,
            escapeIdentifiers: true,
            failOnMissingSemicolons: true);

        /// <summary>
        /// Parses the given file content
        /// </summary>
        public static ISourceFile Parse(string text)
        {
            var parser = new Parser();
            ISourceFile result = parser.ParseSourceFileContent("test.bxt", text, s_publicParsingOptions);
            return result;
        }

        /// <summary>
        /// Binds the given sourcefile
        /// </summary>
        public static void Bind(ISourceFile sourceFile)
        {
            var binder = new TypeScript.Net.Binding.Binder();
            binder.BindSourceFile(sourceFile, s_publicCompilerOptions);
        }

        /// <summary>
        /// Prints out the node
        ///
        /// Note: This api is here temporarily. OsgTools should not take a dependency on this for UnitTest comparisons.
        /// </summary>
        public static string Print(INode node)
        {
            return node?.GetFormattedText();
        }
    }

    /// <summary>
    /// Publically supported Extension methods
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Checks if this tagged template is a path interpolation such that it can contains path separators
        /// </summary>
        public static bool IsPathInterpolation(this ITaggedTemplateExpression taggedTemplateExpression)
        {
            return InterpolationUtilities.IsPathInterpolation(taggedTemplateExpression);
        }

        /// <summary>
        /// Returns true when <paramref name="array"/> is null or empty.
        /// </summary>
        public static bool IsNullOrEmpty<T>(this INodeArray<T> array)
        {
            return NodeArrayExtensions.IsNullOrEmpty(array);
        }

        /// <summary>
        /// Returns the first or default value
        /// </summary>
        public static T FirstOrDefault<T>(this INodeArray<T> array)
        {
            return NodeArrayExtensions.FirstOrDefault(array);
        }

        /// <summary>
        /// Returns the first
        /// </summary>
        public static T First<T>(this INodeArray<T> array)
        {
            return NodeArrayExtensions.First(array);
        }

        /// <summary>
        /// Returns whether the <paramref name="node"/> is injected.
        /// In other words: The node is added after the parsing is over.
        /// </summary>
        public static bool IsInjectedForDScript(this INode node)
        {
            return NodeExtensions.IsInjectedForDScript(node);
        }

        /// <summary>
        /// Invokes <paramref name="func"/> callback for each child of the given node.
        /// Travesal stops when <paramref name="func"/>  returns a 'truthy' value which is then returned from the function.
        /// </summary>
        public static T ForEachChild<T>(this INode node, Func<INode, T> func)
            where T : class
        {
            return NodeWalker.ForEachChild(node, func);
        }

        /// <summary>
        /// Invokes <paramref name="func"/>  callback for each recursive child of the given node.
        /// Travesal stops when <paramref name="func"/>  returns a 'truthy' value which is then returned from the function.
        /// </summary>
        public static T ForEachChildRecursively<T>(this INode node, Func<INode, T> func, bool recurseThroughIdentifiers = false)
            where T : class
        {
            return NodeWalker.ForEachChildRecursively(node, func, recurseThroughIdentifiers);
        }

        /// <summary>
        /// Prints a syntaxKind token to the text that it is parsed from
        /// </summary>
        public static string ToDisplayString(this SyntaxKind syntaxKind)
        {
            return SyntaxKindExtensions.ToDisplayString(syntaxKind);
        }
    }
}

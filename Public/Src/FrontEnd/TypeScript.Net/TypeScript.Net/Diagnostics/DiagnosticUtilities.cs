// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using BuildXL.Utilities.Instrumentation.Common;
using TypeScript.Net.Scanning;
using TypeScript.Net.Types;
using static System.String;

namespace TypeScript.Net.Diagnostics
{
    /// <summary>
    /// Utilities for building and reporting diagnostic.
    /// </summary>
    public static class DiagnosticUtilities
    {
        // Localization is not implemented!
        private static readonly Map<string> s_localizedDiagnosticMessages = null;

        internal static IDiagnosticCollection CreateDiagnosticCollection()
        {
            return new DiagnosticCollection();
        }

        /// <nodoc/>
        public static int GetAbsolutePosition(this Location location, ISourceFile sourceFile)
        {
            return sourceFile.LineMap.Map[location.Line - 1] + location.Position - 1;
        }

        /// <nodoc />
        public static ITextSpan GetSpanOfTokenAtPosition(ISourceFile sourceFile, int pos)
        {
            var scanner = Scanner.CreateScanner(sourceFile.LanguageVersion, /*preserveTrivia*/ false, sourceFile.BackslashesAllowedInPathInterpolation, sourceFile.LanguageVariant,
                sourceFile.Text, /*onError:*/ null, pos);
            scanner.Scan();
            var start = scanner.TokenPos;

            return TextUtilities.CreateTextSpanFromBounds(start, scanner.TextPos);
        }

        /// <nodoc />
        public static ITextSpan GetSpanOfTokenAtPosition(ISourceFile sourceFile, TextSource source, int pos)
        {
            var scanner = Scanner.CreateScanner(sourceFile.LanguageVersion, /*preserveTrivia*/ false, sourceFile.BackslashesAllowedInPathInterpolation, sourceFile.LanguageVariant,
                source, /*onError:*/ null, pos);
            scanner.Scan();
            var start = scanner.TokenPos;

            return TextUtilities.CreateTextSpanFromBounds(start, scanner.TextPos);
        }

        /// <nodoc />
        public static ITextSpan GetErrorSpanForNode(ISourceFile sourceFile, INode node)
        {
            INode errorNode = node;
            switch (node.Kind)
            {
                case SyntaxKind.SourceFile:
                {
                    var pos = Scanner.SkipTrivia(sourceFile.Text, 0, /*stopAfterLineBreak*/ false);
                    if (pos == sourceFile.Text.Length)
                    {
                        // file is empty - return span for the beginning of the file
                        return TextUtilities.CreateTextSpan(0, 0);
                    }

                    return GetSpanOfTokenAtPosition(sourceFile, pos);
                }

                // This list is a work in progress. Add missing node kinds to improve their error
                // spans.
                case SyntaxKind.VariableDeclaration:
                case SyntaxKind.BindingElement:
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.ClassExpression:
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.ModuleDeclaration:
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.EnumMember:
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.MethodDeclaration:
                {
                    errorNode = node.Cast<IDeclaration>().Name;
                    break;
                }
            }

            if (errorNode == null)
            {
                // If we don't have a better node, then just set the error on the first token of
                // construct.
                return GetSpanOfTokenAtPosition(sourceFile, node.Pos);
            }

            var pos1 = NodeUtilities.NodeIsMissing(errorNode)
                ? errorNode.Pos
                : errorNode.GetNodeStartPositionWithoutTrivia(sourceFile);

            return TextUtilities.CreateTextSpanFromBounds(pos1, errorNode.End);
        }

        /// <nodoc />
        public static DiagnosticMessageChain ChainDiagnosticMessages(DiagnosticMessageChain details, IDiagnosticMessage message,
            params object[] args)
        {
            var text = GetLocaleSpecificMessage(message);

            // arguments is a javascript concept!
            if (args?.Length > 0)
            {
                text = FormatStringFromArgs(text, args);
            }

            return new DiagnosticMessageChain(text, message.Category, message.Code, details);
        }

        /// <nodoc />
        public static DiagnosticMessageChain ConcatenateDiagnosticMessageChains(
            DiagnosticMessageChain headChain,
            DiagnosticMessageChain tailChain)
        {
            var lastChain = headChain;
            while (lastChain.Next != null)
            {
                lastChain = lastChain.Next;
            }

            lastChain.Next = tailChain;
            return headChain;
        }

        /// <nodoc />
        public static List<Diagnostic> SortAndDeduplicateDiagnostics(List<Diagnostic> diagnostics)
        {
            // TODO: Verify equivalence - return deduplicateSortedDiagnostics(diagnostics.sort(compareDiagnostics));
            diagnostics.Sort(DiagnosticComparer.Instance);
            return DeduplicateSortedDiagnostics(diagnostics);
        }

        /// <nodoc />
        public static List<Diagnostic> DeduplicateSortedDiagnostics(List<Diagnostic> diagnostics)
        {
            if (diagnostics.Count < 2)
            {
                return diagnostics;
            }

            var newDiagnostics = new List<Diagnostic> { diagnostics[0] };
            var previousDiagnostic = diagnostics[0];

            for (var i = 1; i < diagnostics.Count; i++)
            {
                var currentDiagnostic = diagnostics[i];
                var isDupe = CompareDiagnostics(currentDiagnostic, previousDiagnostic) == Comparison.EqualTo;
                if (!isDupe)
                {
                    newDiagnostics.Add(currentDiagnostic);
                    previousDiagnostic = currentDiagnostic;
                }
            }

            return newDiagnostics;
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Performance", "CA1801")]
        public static string FormatStringFromArgs(string text, object[] args, int baseIndex = 0)
        {
            // baseIndex = baseIndex || 0;
            return Format(CultureInfo.InvariantCulture, text, args);

            // return text.replace(/{ (\d +)}/ g, (match, index?) => args[+index + baseIndex]);
        }

        private enum Comparison
        {
            LessThan = -1,
            EqualTo = 0,
            GreaterThan = 1,
        }

        /// <nodoc/>
        public class DiagnosticComparer : IComparer<Diagnostic>
        {
            /// <nodoc/>
            public static readonly DiagnosticComparer Instance = new DiagnosticComparer();

            /// <inheritdoc/>
            public int Compare(Diagnostic x, Diagnostic y)
            {
                return (int)CompareDiagnostics(x, y);
            }
        }

        /// <nodoc />
        internal static string GetLocaleSpecificMessage(IDiagnosticMessage message)
        {
            return s_localizedDiagnosticMessages != null && s_localizedDiagnosticMessages.ContainsKey(message.Key)
                ? s_localizedDiagnosticMessages[message.Key]
                : message.Message;
        }

        private static Comparison CompareValues<T>(T a, T b) where T : IEquatable<T>, IComparable<T>
        {
            // HINT: this is a fix, because in some cases, both fields of diagnostic instance are null.
            if (ReferenceEquals(a, null) && ReferenceEquals(b, null))
            {
                return Comparison.EqualTo;
            }

            if (a == null)
            {
                return Comparison.LessThan;
            }

            if (b == null)
            {
                return Comparison.GreaterThan;
            }

            if (a.Equals(b))
            {
                return Comparison.EqualTo;
            }

            return a.CompareTo(b) < 0 ? Comparison.LessThan : Comparison.GreaterThan;
        }

        private static string GetDiagnosticFileName(Diagnostic diagnostic)
        {
            return diagnostic.File?.FileName;
        }

        private static Comparison CompareDiagnostics(Diagnostic d1, Diagnostic d2)
        {
            // TODO: Verify equivalence!
            // return compareValues(getDiagnosticFileName(d1), getDiagnosticFileName(d2)) ||
            //     compareValues(d1.start, d2.start) ||
            //     compareValues(d1.length, d2.length) ||
            //     compareValues(d1.code, d2.code) ||
            //     compareMessageText(d1.messageText, d2.messageText) ||
            //     Comparison.EqualTo;
            Comparison comparison = CompareValues(GetDiagnosticFileName(d1), GetDiagnosticFileName(d2));
            if (comparison != Comparison.EqualTo)
            {
                return comparison;
            }

            comparison = CompareValues(d1.Start ?? 0, d2.Start ?? 0);
            if (comparison != Comparison.EqualTo)
            {
                return comparison;
            }

            comparison = CompareValues(d1.Length ?? 0, d2.Length ?? 0);
            if (comparison != Comparison.EqualTo)
            {
                return comparison;
            }

            comparison = CompareValues(d1.Code, d2.Code);
            if (comparison != Comparison.EqualTo)
            {
                return comparison;
            }

            comparison = CompareMessageText(d1.MessageText, d2.MessageText);
            if (comparison != Comparison.EqualTo)
            {
                return comparison;
            }

            return Comparison.EqualTo;
        }

        private static Comparison CompareMessageText(Message text1, Message text2)
        {
            while (text1 != null && text2 != null)
            {
                // We still have both chains.
                var string1 = text1.AsString() ?? text1.AsDiagnosticMessageChain().MessageText;
                var string2 = text2.AsString() ?? text2.AsDiagnosticMessageChain().MessageText;

                var res = CompareValues(string1, string2);
                if (res != Comparison.EqualTo)
                {
                    return res;
                }

                // Old code (for understandability):
                // text1 = typeof text1 === "string" ? undefined : text1.next;
                // text2 = typeof text2 === "string" ? undefined : text2.next;
                text1 = text1.AsString() != null ? null : text1.AsDiagnosticMessageChain().Next;
                text2 = text2.AsString() != null ? null : text2.AsDiagnosticMessageChain().Next;
            }

            if (text1 == null && text2 == null)
            {
                // if the chains are done, then these messages are the same.
                return Comparison.EqualTo;
            }

            // We still have one chain remaining.  The shorter chain should come first.
            return text1 != null ? Comparison.GreaterThan : Comparison.LessThan;
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using TypeScript.Net.Scanning;

namespace TypeScript.Net.Types
{
    /// <summary>
    /// Set of extension methods for <see cref="SyntaxKind"/> enums.
    /// </summary>
    public static class SyntaxKindExtensions
    {
        private static readonly Dictionary<SyntaxKind, string> s_specialSyntaxKinds = new Dictionary<SyntaxKind, string>()
        {
            [SyntaxKind.OmittedExpression] = string.Empty,
        };

        /// <nodoc />
        public static string ToDisplayString(this SyntaxKind kind)
        {
            Scanner.TokenStrings.TryGetValue(kind, out var result);

            // Some kinds should be treated differently. For instance, 'OmittedExpression' should be printed as space.
            if (result == null)
            {
                s_specialSyntaxKinds.TryGetValue(kind, out result);
            }

            // Not all kinds are presented in tokenStrings, so using string representation
            // for other kinds.
            return result ?? kind.ToString();
        }

        /// <nodoc />
        public static bool IsIdentifierOrKeyword(this SyntaxKind kind)
        {
            return kind >= SyntaxKind.Identifier;
        }

        /// <nodoc />
        public static bool IsToken(this SyntaxKind kind)
        {
            return kind >= SyntaxKind.FirstToken && kind <= SyntaxKind.LastToken;
        }

        /// <nodoc />
        public static bool IsKeyword(this SyntaxKind kind)
        {
            return kind >= SyntaxKind.FirstKeyword && kind <= SyntaxKind.LastKeyword;
        }

        /// <nodoc />
        public static bool IsTrivia(this SyntaxKind kind)
        {
            return kind >= SyntaxKind.FirstTriviaToken && kind <= SyntaxKind.LastTriviaToken;
        }

        /// <nodoc />
        public static bool IsWord(this SyntaxKind kind)
        {
            return kind == SyntaxKind.Identifier || IsKeyword(kind);
        }

        /// <nodoc />
        public static bool IsPropertyName(this SyntaxKind kind)
        {
            return
                kind == SyntaxKind.StringLiteral ||
                kind == SyntaxKind.NumericLiteral ||
                IsWord(kind);
        }

        /// <nodoc />
        public static bool IsStringOrNumericLiteral(this SyntaxKind kind)
        {
            return kind == SyntaxKind.StringLiteral || kind == SyntaxKind.NumericLiteral;
        }

        /// <nodoc />
        public static bool IsModifierKind(this SyntaxKind kind)
        {
            switch (kind)
            {
                case SyntaxKind.AbstractKeyword:
                case SyntaxKind.AsyncKeyword:
                case SyntaxKind.ConstKeyword:
                case SyntaxKind.DeclareKeyword:
                case SyntaxKind.DefaultKeyword:
                case SyntaxKind.ExportKeyword:
                case SyntaxKind.PublicKeyword:
                case SyntaxKind.PrivateKeyword:
                case SyntaxKind.ProtectedKeyword:
                case SyntaxKind.StaticKeyword:
                case SyntaxKind.ReadonlyKeyword:
                    return true;
            }

            return false;
        }

        /// <nodoc />
        public static NodeFlags ModifierToFlag(this SyntaxKind kind)
        {
            switch (kind)
            {
                case SyntaxKind.StaticKeyword: return NodeFlags.Static;
                case SyntaxKind.PublicKeyword: return NodeFlags.Public;
                case SyntaxKind.ProtectedKeyword: return NodeFlags.Protected;
                case SyntaxKind.PrivateKeyword: return NodeFlags.Private;
                case SyntaxKind.AbstractKeyword: return NodeFlags.Abstract;
                case SyntaxKind.ExportKeyword: return NodeFlags.Export;
                case SyntaxKind.DeclareKeyword: return NodeFlags.Ambient;
                case SyntaxKind.ConstKeyword: return NodeFlags.Const;
                case SyntaxKind.DefaultKeyword: return NodeFlags.Default;
                case SyntaxKind.AsyncKeyword: return NodeFlags.Async;
                case SyntaxKind.ReadonlyKeyword: return NodeFlags.Readonly;
            }

            return 0;
        }

        /// <nodoc />
        public static bool IsAssignmentOperator(this SyntaxKind kind)
        {
            return kind >= SyntaxKind.FirstAssignment && kind <= SyntaxKind.LastAssignment;
        }

        /// <nodoc />
        public static bool IsLiteralKind(SyntaxKind kind)
        {
            return kind >= SyntaxKind.FirstLiteralToken && kind <= SyntaxKind.LastLiteralToken;
        }

        /// <nodoc />
        public static bool IsTextualLiteralKind(SyntaxKind kind)
        {
            return kind == SyntaxKind.StringLiteral || kind == SyntaxKind.NoSubstitutionTemplateLiteral;
        }

        /// <nodoc />
        public static bool IsTemplateLiteralKind(SyntaxKind kind)
        {
            return kind >= SyntaxKind.FirstTemplateToken && kind <= SyntaxKind.LastTemplateToken;
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.Constants;
using JetBrains.Annotations;
using TypeScript.Net.DScript;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace TypeScript.Net.Parsing
{
    /// <summary>
    /// Parser that converts <code>p``</code>, <code>d``</code> and <code>f``</code> literals directly to <see cref="AbsolutePath"/>.
    /// </summary>
    /// TODO: eventually we should have just one parser. Once will be confident enough with this one, we can make the base class abstract.
    public class DScriptParser : Parser
    {
#pragma warning disable CA1823 // Unused field 'm_qualifierNameAsAtom': StyleCop analyzer can't see that the variable was used inside an anonymous function.
        private SymbolAtom m_qualifierNameAsAtom;

        private readonly PathTable m_pathTable;

        /// <nodoc />
        public DScriptParser([NotNull]PathTable pathTable)
        {
            m_pathTable = pathTable;
        }

        /// <inheritdoc/>
        protected override SourceFile CreateSourceFile(string fileName, ScriptTarget languageVersion, bool allowBackslashesInPathInterpolation)
        {
            var result = base.CreateSourceFile(fileName, languageVersion, allowBackslashesInPathInterpolation);
            result.PathTable = m_pathTable;
            return result;
        }

        /// <inheritdoc/>
        protected override ILiteralExpression ParseLiteralNodeFactory([CanBeNull]string factoryName)
        {
            if (string.IsNullOrEmpty(factoryName))
            {
                return base.ParseLiteralNodeFactory(factoryName);
            }

            char factory = factoryName[0];
            switch (factory)
            {
                case Names.PathInterpolationFactory:
                case Names.FileInterpolationFactory:
                case Names.DirectoryInterpolationFactory:
                    return ParseAbsolutePath() ?? base.ParseLiteralNodeFactory(factoryName);
                case Names.RelativePathInterpolationFactory:
                    return ParseRelativePath() ?? base.ParseLiteralNodeFactory(factoryName);
                case Names.PathAtomInterpolationFactory:
                    return ParsePathAtom() ?? base.ParseLiteralNodeFactory(factoryName);
            }

            return base.ParseLiteralNodeFactory(factoryName);
        }

        /// <summary>
        /// Parses template literal fragment like "/literal" fragment in the string <code>"${expression}/literal"</code>.
        /// </summary>
        protected override ITemplateLiteralFragment ParseTemplateLiteralFragment()
        {
            var node = CreateNodeAndSetParent<StringIdTemplateLiteralFragment>(m_token);

            // TODO: need to change the scanner to use StringId instead of string.
            // Or, maybe, switch to ReadOnlySpan<T>.
            var text = StringId.Create(m_pathTable.StringTable, m_scanner.TokenValue);

            if (!text.IsValid)
            {
                Contract.Assert(false, I($"Can't create StringId from '{m_scanner.TokenValue}'."));
            }

            node.TextAsStringId = text;

            if (m_scanner.HasExtendedUnicodeEscape)
            {
                node.HasExtendedUnicodeEscape = true;
            }

            if (m_scanner.IsUnterminated)
            {
                node.IsUnterminated = true;
            }

            NextToken();
            FinishNode(node);

            return node;
        }

        private ILiteralExpression ParsePathAtom()
        {
            var text = m_scanner.TokenValue;

            // If path is an empty string, than bailing out.
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            if (PathAtom.TryCreate(m_pathTable.StringTable, text, out var atom))
            {
                return CreatePathAtomLiteralExpression(m_token, atom);
            }

            return null;
        }

        private TNode CreateNodeAndSetParent<TNode>(SyntaxKind kind)
            where TNode : INode, new()
        {
            var node = CreateNode<TNode>(kind);
            node.Parent = m_sourceFile;
            return node;
        }

        private ILiteralExpression ParseRelativePath()
        {
            var text = m_scanner.TokenValue;

            // If path is an empty string, than bailing out.
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            if (RelativePath.TryCreate(m_pathTable.StringTable, text, out var relativePath))
            {
                return CreateRelativePathLiteralExpression(m_token, relativePath);
            }

            return null;
        }

        private ILiteralExpression ParseAbsolutePath()
        {
            var text = m_scanner.TokenValue;

            // If path is an empty string, than bailing out.
            if (string.IsNullOrEmpty(text))
            {
                return null;
            }

            // If the path is package relative, then need to skip the first character (i.e. '/').
            if (IsPackageRelative(text) &&
                RelativePath.TryCreate(m_pathTable.StringTable, text.Substring(1), out var packageRelativePath))
            {
                return CreateRelativePathLiteralExpression(m_token, packageRelativePath, packageRelative: true);
            }

            // 'text' in this case can be a valid absolute path, like 'c:/foobar.txt',
            // spec relative "absolute path" like 'foobar.txt' or
            // unc path like //unc/foobar.txt.
            // The latter is valid absolute and relative path, this function creates absolute path
            // literals and falls back to relative path only for spec-relative paths.
            if (AbsolutePath.TryCreate(m_pathTable, text, out var absolutePath))
            {
                return CreateAbsolutePathLiteralExpression(m_token, absolutePath);
            }

            if (RelativePath.TryCreate(m_pathTable.StringTable, text, out var relativePath))
            {
                return CreateRelativePathLiteralExpression(m_token, relativePath);
            }

            return null;
        }

        /// <inheritdoc />
        protected override bool IsQualifierDeclaration(IStatement statement)
        {
            return statement.IsQualifierDeclaration(GetQualifierNameAsAtom(), Names.CurrentQualifier);

            SymbolAtom GetQualifierNameAsAtom()
            {
                if (!m_qualifierNameAsAtom.IsValid)
                {
                    m_qualifierNameAsAtom = SymbolAtom.Create(m_pathTable.StringTable, Names.CurrentQualifier);
                }

                return m_qualifierNameAsAtom;
            }
        }

        private static bool IsPackageRelative(string pathLike)
        {
            if (pathLike.Length < 2)
            {
                return false;
            }

            // TODO: While disabling this for now is fine, '/' denotes the root path of every absolut Unix path and can't be misused as char
            // to indicate package relative relationships - we either change this to some more unique symbol or come up with a better solution
            if (OperatingSystemHelper.IsUnixOS)
            {
                return false;
            }

            return pathLike[0] == '/' && pathLike[1] != '/';
        }

        private AbsolutePathLiteralExpression CreateAbsolutePathLiteralExpression(SyntaxKind token, AbsolutePath path)
        {
            var node = CreateNodeAndSetParent<AbsolutePathLiteralExpression>(token);
            node.Path = path;

            NextToken();
            FinishNode(node);

            return node;
        }

        private RelativePathLiteralExpression CreateRelativePathLiteralExpression(SyntaxKind token, RelativePath path, bool packageRelative = false)
        {
            var node = packageRelative
                ? (RelativePathLiteralExpression)CreateNodeAndSetParent<PackageRelativePathLiteralExpression>(token)
                : CreateNodeAndSetParent<RelativePathLiteralExpression>(token);
            node.Path = path;

            NextToken();
            FinishNode(node);

            return node;
        }

        private PathAtomLiteralExpression CreatePathAtomLiteralExpression(SyntaxKind token, PathAtom atom)
        {
            var node = CreateNodeAndSetParent<PathAtomLiteralExpression>(token);
            node.Atom = atom;

            NextToken();
            FinishNode(node);

            return node;
        }
    }
}

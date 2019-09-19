// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using JetBrains.Annotations;
using TypeScript.Net.Types;

namespace TypeScript.Net.Parsing
{
    /// <summary>
    /// Parser for DScript files that were stripped to their public surface version
    /// </summary>
    /// <remarks>
    /// This parser uses the original position of the statement (annotated as a decorator) so each declaration
    /// has the original position of the node.
    /// </remarks>
    public class PublicSurfaceParser : DScriptParser
    {
        private readonly byte[] m_serializedAstContent;
        private readonly int m_serializedAstLength;

        /// <nodoc />
        public PublicSurfaceParser(BuildXL.Utilities.PathTable pathTable, [JetBrains.Annotations.NotNull]byte[] serializedAstContent, int serializedAstLength)
            : base(pathTable)
        {
            Contract.Requires(serializedAstContent != null);
            Contract.Requires(serializedAstLength <= serializedAstContent.Length);

            m_serializedAstContent = serializedAstContent;
            m_serializedAstLength = serializedAstLength;
        }

        /// <inheritdoc/>
        protected override SourceFile CreateSourceFile(string fileName, ScriptTarget languageVersion, bool allowBackslashesInPathInterpolation)
        {
            var sourceFile = base.CreateSourceFile(fileName, languageVersion, allowBackslashesInPathInterpolation);

            sourceFile.SetSerializedAst(m_serializedAstContent, m_serializedAstLength);

            return sourceFile;
        }

        /// <inheritdoc/>
        protected override IEnumDeclaration ParseEnumDeclaration(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            var originalDecorators = RemovePositionDecorator(decorators);
            var enumDeclaration = base.ParseEnumDeclaration(fullStart, triviaLength, originalDecorators, modifiers);

            UpdateDeclarationtWithOriginalPosition(decorators, enumDeclaration);

            return enumDeclaration;
        }

        /// <inheritdoc/>
        public override IEnumMember ParseEnumMember()
        {
            var node = base.ParseEnumMember();

            UpdateDeclarationtWithOriginalPosition(node.Decorators, node);
            var originalDecorators = RemovePositionDecorator(node.Decorators);

            node.Decorators = originalDecorators;

            return node;
        }

        /// <inheritdoc/>
        protected override IFunctionDeclaration ParseFunctionDeclaration(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            var originalDecorators = RemovePositionDecorator(decorators);
            var functionDeclaration = base.ParseFunctionDeclaration(fullStart, triviaLength, originalDecorators, modifiers);

            UpdateDeclarationtWithOriginalPosition(decorators, functionDeclaration);

            return functionDeclaration;
        }

        /// <inheritdoc/>
        protected override IInterfaceDeclaration ParseInterfaceDeclaration(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            var originalDecorators = RemovePositionDecorator(decorators);
            var interfaceDeclaration = base.ParseInterfaceDeclaration(fullStart, triviaLength, originalDecorators, modifiers);

            UpdateDeclarationtWithOriginalPosition(decorators, interfaceDeclaration);

            return interfaceDeclaration;
        }

        /// <inheritdoc/>
        protected override ITypeAliasDeclaration ParseTypeAliasDeclaration(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            var originalDecorators = RemovePositionDecorator(decorators);
            var typeAliasDeclaration = base.ParseTypeAliasDeclaration(fullStart, triviaLength, originalDecorators, modifiers);

            UpdateDeclarationtWithOriginalPosition(decorators, typeAliasDeclaration);

            return typeAliasDeclaration;
        }

        /// <inheritdoc/>
        protected override IVariableStatement ParseVariableStatement(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            var originalDecorators = RemovePositionDecorator(decorators);
            var variableStatement = base.ParseVariableStatement(fullStart, triviaLength, originalDecorators, modifiers);

            Contract.Assert(variableStatement.DeclarationList.Declarations.Count == 1, "A public surface file should only have one declaration per statement");

            var declaration = variableStatement.DeclarationList.Declarations[0];

            UpdateDeclarationtWithOriginalPosition(decorators, declaration);

            return variableStatement;
        }

        /// <inheritdoc/>
        protected override IModuleDeclaration ParseModuleDeclaration(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            var originalDecorators = RemovePositionDecorator(decorators);
            var moduleDeclaration = base.ParseModuleDeclaration(fullStart, triviaLength, originalDecorators, modifiers);

            Contract.Assert(moduleDeclaration.Body.AsModuleDeclaration() == null, "A public surface file should only have one module per declaration");

            UpdateDeclarationtWithOriginalPosition(decorators, moduleDeclaration);

            return moduleDeclaration;
        }

        /// <inheritdoc/>
        protected override IExportDeclaration ParseExportDeclaration(int fullStart, int triviaLength, NodeArray<IDecorator> decorators, ModifiersArray modifiers)
        {
            var originalDecorators = RemovePositionDecorator(decorators);

            var exportDeclaration = base.ParseExportDeclaration(fullStart, triviaLength, originalDecorators, modifiers);

            Contract.Assert(exportDeclaration.ExportClause == null || exportDeclaration.ExportClause.Elements.Count == 1, "A public surface file should only have one export specifier per declaration");

            if (exportDeclaration.ExportClause == null)
            {
                UpdateDeclarationtWithOriginalPosition(decorators, exportDeclaration);
            }
            else
            {
                var exportSpecifier = exportDeclaration.ExportClause.Elements[0];
                UpdateDeclarationtWithOriginalPosition(decorators, exportSpecifier);
            }

            return exportDeclaration;
        }

        private static int GetOriginalPositionFromPositionDecorator(NodeArray<IDecorator> decorators)
        {
            Contract.Assert(decorators.Count > 0, "A public surface file should have a decorator here");

            var positionDecorator = decorators[0];
            var stringPosition = positionDecorator.Expression.Cast<ILiteralExpression>().Text;

            int position;
            var result = int.TryParse(stringPosition, out position);

            Contract.Assert(result, "The first decorator is expected to be a position");
            return position;
        }

        private static NodeArray<IDecorator> RemovePositionDecorator(NodeArray<IDecorator> decorators)
        {
            Contract.Assert(decorators.Count > 0);

            var originalDecorators = new IDecorator[decorators.Count - 1];
            if (originalDecorators.Length > 0)
            {
                decorators.CopyTo(1, originalDecorators, 0, decorators.Count - 1);
            }

            return new NodeArray<IDecorator>(originalDecorators);
        }

        private static void UpdateDeclarationtWithOriginalPosition(NodeArray<IDecorator> decorators, IDeclaration declaration)
        {
            var originalStart = GetOriginalPositionFromPositionDecorator(decorators);
            declaration.Pos = originalStart;
        }
    }
}

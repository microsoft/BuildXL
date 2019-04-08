// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using Codex.Analysis.External;
using TypeScript.Net.Types;
using TypeScript.Net.Types.Nodes;

namespace BuildXL.FrontEnd.Script.Analyzer.Codex
{
    /// <summary>
    /// Ast visitor for processing references.
    /// </summary>
    public sealed class CodexReferenceVisitor : DfsVisitor
    {
        private readonly CodexContext m_codex;
        private readonly CodexFile m_file;
        private readonly Stack<CodexId<CodexRefKind>> m_currentReferenceKinds;

        private readonly CodexDefinitionVisitor m_definitionVisitor;
        private int m_localIdCursor;

        internal CodexReferenceVisitor(
            CodexContext codex,
            CodexFile file,
            CodexDefinitionVisitor definitionVisitor)
        {
            m_codex = codex;
            m_file = file;
            m_definitionVisitor = definitionVisitor;

            m_currentReferenceKinds = new Stack<CodexId<CodexRefKind>>();
            m_currentReferenceKinds.Push(m_codex.Reference);
        }

        /// <inheritdoc />
        public override void VisitPropertyAssignment(IPropertyAssignment node)
        {
            CreateAndRegisterReferenceSpan(node.Name, node.Name, m_codex.Property, m_codex.Write);
            base.VisitPropertyAssignment(node);
        }

        /// <inheritdoc />
        public override void VisitTypeParameterDeclaration(ITypeParameterDeclaration node)
        {
            // Type parameters are locals
            RegisterLocalSymbolDefinition(node, m_codex.TypeParameterName);

            base.VisitTypeParameterDeclaration(node);
        }

        /// <inheritdoc />
        public override void VisitFunctionDeclaration(IFunctionDeclaration node)
        {
            m_currentReferenceKinds.Push(m_codex.FunctionScopedVariable);
            base.VisitFunctionDeclaration(node);
            m_currentReferenceKinds.Pop();
        }

        /// <inheritdoc />
        public override void VisitParameterDeclaration(IParameterDeclaration node)
        {
            RegisterLocalSymbolDefinition(node);

            base.VisitParameterDeclaration(node);
        }

        /// <inheritdoc />
        public override void VisitVariableDeclaration(IVariableDeclaration node)
        {
            if (m_currentReferenceKinds.Peek().Id == m_codex.FunctionScopedVariable.Id)
            {
                RegisterLocalSymbolDefinition(node);
            }

            base.VisitVariableDeclaration(node);
        }

        /// <inheritdoc />
        public override void VisitPropertyAccessExpression(IPropertyAccessExpression node)
        {
            CreateAndRegisterReferenceSpan(node.Name, node, m_codex.Property, m_codex.Read);
            base.VisitPropertyAccessExpression(node);
        }

        /// <inheritdoc />
        public override void VisitIdentifier(IIdentifier node)
        {
            // Need to ignore '@@public'.
            if (node.OriginalKeywordKind != TypeScript.Net.Types.SyntaxKind.PublicKeyword)
            {
                CreateAndRegisterReferenceSpan(node, node);
            }
            base.VisitIdentifier(node);
        }

        /// <inheritdoc />
        public override void VisitTypeReference(ITypeReferenceNode node)
        {
            // Type references like 'const x: MyNs.MyT' should not be registered explicitely.
            CreateAndRegisterReferenceSpan(node, node.TypeName, throwOnUnknownSpan: false);

            base.VisitTypeReference(node);
        }

        private void RegisterLocalSymbolDefinition(IDeclaration bindableNode, CodexId<CodexClassification> classification = default)
        {
            if (bindableNode.IsInjectedForDScript())
            {
                return;
            }

            if (m_definitionVisitor.TryGetCodexSpanFor(bindableNode.Name, out var span) && !span.LocalId.IsValid)
            {
                var localId = new CodexId<object>() {Id = m_localIdCursor++};
                m_codex.RegisterLocalSymbolDefinition(m_file.Path, bindableNode.GetNodeStartPositionWithoutTrivia(), localId);
                span.LocalId = localId;
                if (!span.Classification.IsValid)
                {
                    span.Classification = classification.IsValid ? classification : m_codex.Identifier;
                }
            }
            else
            {
                Contract.Assert(false, $"TODO: add message");
            }
        }

        private void CreateAndRegisterReferenceSpan(INode node, INode bindableNode, CodexId<CodexClassification> classification = default, CodexId<CodexRefKind> referenceKind = default, bool throwOnUnknownSpan = true)
        {
            // In 'a.b' node is 'b' and bindable node is 'a.b'.
            if (node == null || node.IsInjectedForDScript())
            {
                return;
            }

            if (!m_definitionVisitor.TryGetCodexSpanFor(node, out var span))
            {
                if (throwOnUnknownSpan)
                {
                    throw Contract.AssertFailure($"Attempting to mark a span which is not a token. Node: '{node.ToDisplayString()}' at file {node.SourceFile.Path}, position {node.Pos}");
                }

                return;
            }

            if (classification.IsValid && !span.Classification.IsValid)
            {
                span.Classification = classification;
            }

            referenceKind = referenceKind.IsValid ? referenceKind : m_currentReferenceKinds.Peek();

            if (referenceKind.IsValid && !span.ReferenceKind.IsValid)
            {
                span.ReferenceKind = referenceKind;
            }

            m_codex.RegisterSymbolReference(span, bindableNode);
        }
    }
}

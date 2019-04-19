// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using TypeScript.Net.Api;
using TypeScript.Net.Reformatter;
using TypeScript.Net.Types;
using TypeScript.Net.Types.Nodes;

namespace BuildXL.FrontEnd.Script.Analyzer.Documentation
{
    /// <nodoc />
    public class DocumentationVisitor : DfsVisitor
    {
        private readonly Module m_module;

        private readonly AbsolutePath m_path;

        private readonly Stack<DocNode> m_declarations = new Stack<DocNode>();

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="module">Module to document.</param>
        /// <param name="path">Absolute path for <paramref name="module"/>.</param>
        public DocumentationVisitor(Module module, AbsolutePath path)
        {
            m_module = module;
            m_path = path;
        }

        /// <inheritdoc />
        public override void VisitSourceFile(ISourceFile node)
        {
            // Push null as outer parent
            m_declarations.Push(null);
            base.VisitSourceFile(node);
            m_declarations.Pop();
        }

        /// <inheritdoc />
        public override void VisitModuleDeclaration(IModuleDeclaration node)
        {
            Register(node, node.Flags, DocNodeType.Namespace, node.Name.Text, base.VisitModuleDeclaration);
        }

        /// <inheritdoc />
        public override void VisitInterfaceDeclaration(IInterfaceDeclaration node)
        {
            Register(node, node.Flags, DocNodeType.Interface, node.Name.Text, base.VisitInterfaceDeclaration);
        }

        /// <inheritdoc />
        public override void VisitCallSignatureDeclaration(ICallSignatureDeclaration node)
        {
            Register(node, node.Flags, DocNodeType.InstanceFunction, node.Name.Text, base.VisitCallSignatureDeclaration);
        }

        /// <inheritdoc />
        public override void VisitMethodSignature(IMethodSignature node)
        {
            Register(node, node.Flags, DocNodeType.InstanceFunction, node.Name.Text, base.VisitMethodSignature);
        }

        /// <inheritdoc />
        public override void VisitPropertySignature(IPropertySignature node)
        {
            Register(node, node.Flags, DocNodeType.Property, node.Name.Text, base.VisitPropertySignature);
        }

        /// <inheritdoc />
        public override void VisitFunctionDeclaration(IFunctionDeclaration node)
        {
            Register(node, node.Flags, DocNodeType.Function, node.Name.Text, base.VisitFunctionDeclaration);
        }

        /// <inheritdoc />
        public override void VisitVariableStatement(IVariableStatement node)
        {
            Register(node, node.Flags, DocNodeType.Value, node.DeclarationList.Declarations[0].Name.GetText(), base.VisitVariableStatement);
        }

        /// <inheritdoc />
        public override void VisitEnumDeclaration(IEnumDeclaration node)
        {
            Register(node, node.Flags, DocNodeType.Enum, node.Name.GetText(), base.VisitEnumDeclaration);
        }

        /// <inheritdoc />
        public override void VisitEnumMember(IEnumMember node)
        {
            Register(node, node.Flags, DocNodeType.EnumMember, node.Name.GetText(), base.VisitEnumMember);
        }

        /// <inheritdoc />
        public override void VisitTypeAliasDeclaration(ITypeAliasDeclaration node)
        {
            Register(node, node.Flags, DocNodeType.Type, node.Name.GetText(), base.VisitTypeAliasDeclaration);
        }

        private void Register<T>(T node, NodeFlags flags, DocNodeType type, string name, Action<T> visit)
            where T : INode
        {
            var injected = NodeExtensions.IsInjectedForDScript(node);
            if (injected)
            {
                return;
            }

            var visibility = DocNodeVisibility.Private;
            if ((flags & NodeFlags.Export) != 0)
            {
                visibility = DocNodeVisibility.Internal;
            }

            if ((flags & NodeFlags.ScriptPublic) != 0 || type == DocNodeType.Namespace)
            {
                visibility = DocNodeVisibility.Public;
            }

            List<string> triviaContent = new List<string>();

            AddComments(node, triviaContent);

            if (type == DocNodeType.EnumMember || type == DocNodeType.Value || type == DocNodeType.Property)
            {
                // For these edge nodes, we can safely visit their children to seek out other documentation.
                // The documentation chunks may be in the wrong order due to search order, but the results will be complete.
                node.ForEachChildRecursively<TypeScript.Net.Types.Node>(
                    n =>
                    {
                        AddComments(n, triviaContent);

                        return null;
                    },
                    recurseThroughIdentifiers: true);
            }
            else
            {
                if (node.Decorators?.Length > 0)
                {
                    foreach (var decorator in node.Decorators.Elements)
                    {
                        AddComments(decorator, triviaContent);
                    }
                }
            }

            string appendix = string.Empty;

            // Handle Tool.option attribute
            IPropertyAccessExpression propertyAccessExpression;
            ICallExpression callExpression;
            IIdentifier propertyIdentifier;
            ILiteralExpression literal;
            if (node.Decorators?.Count > 0
                && (callExpression = node.Decorators.Elements[0].Expression.As<ICallExpression>()) != null
                && (propertyAccessExpression = callExpression.Expression.As<IPropertyAccessExpression>()) != null
                && (propertyIdentifier = propertyAccessExpression.Expression.As<IIdentifier>()) != null
                && propertyIdentifier.Text == "Tool"
                && propertyAccessExpression.Name.Text == "option"
                && callExpression.Arguments.Count > 0
                && (literal = callExpression.Arguments[0].As<ILiteralExpression>()) != null)
            {
                appendix = $"Tool option {literal.Text}";
            }

            DocNode newNode;
            var parent = m_declarations.Peek();
            if (parent != null)
            {
                newNode = parent.GetOrAdd(type, visibility, m_path, name, triviaContent, appendix);
            }
            else
            {
                newNode = m_module.GetOrAdd(type, visibility, m_path, name, triviaContent, appendix);
            }

            m_declarations.Push(newNode);
            visit(node);
            m_declarations.Pop();
        }

        private static void AddComments(INode node, List<string> triviaContent)
        {
            if (node.GetSourceFile().PerNodeTrivia.TryGetValue(node, out Trivia decoratorTrivia)
                && decoratorTrivia.LeadingComments?.Length > 0)
            {
                triviaContent.AddRange(decoratorTrivia.LeadingComments.SelectMany(c => c.Content.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)));
            }
        }
    }
}

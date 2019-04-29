// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Analyzer.Utilities;
using BuildXL.FrontEnd.Workspaces;
using Codex.Analysis.External;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;
using TypeScript.Net.Types.Nodes;

namespace BuildXL.FrontEnd.Script.Analyzer.Codex
{
    /// <summary>
    /// Ast visitor for processing definitions.
    /// </summary>
    public sealed class CodexDefinitionVisitor
    {
        private readonly CodexFile m_file;
        private readonly Dictionary<(int start, int length), CodexSpan> m_tokenSpans = new Dictionary<(int, int), CodexSpan>();

        private readonly CodexContext m_codex;

        private readonly Dictionary<INode, string> m_nodeNames = new Dictionary<INode, string>();

        private readonly ISemanticModel m_semanticModel;
        private readonly ISourceFile m_sourceFile;
        private readonly string m_moduleName;
        private readonly CodexId<CodexProject> m_project;

        internal CodexDefinitionVisitor(CodexContext codex, CodexFile file, CodexId<CodexProject> project, string moduleName, ISemanticModel semanticModel, ISourceFile sourceFile)
        {
            m_codex = codex;
            m_file = file;
            m_semanticModel = semanticModel;
            m_sourceFile = sourceFile;
            m_project = project;
            m_moduleName = moduleName;
        }

        /// <summary>
        /// Returns a span for a given node.
        /// </summary>
        public bool TryGetCodexSpanFor(INode node, out CodexSpan span)
        {
            return m_tokenSpans.TryGetValue((node.GetNodeStartPositionWithoutTrivia(m_sourceFile), node.GetNodeWidth(m_sourceFile)), out span);
        }

        private void CreateSpansAndAssociateDefinitions()
        {
            var identifierVisitor = new IdentifierVisitor(m_sourceFile);

            foreach (var (start, end, tokenKind) in ScannerHelpers.GetTokens(m_sourceFile, preserveTrivia: true, preserveComments: true))
            {
                var length = end - start;
                if (length != 0)
                {
                    var finalTokenKind = tokenKind;
                    if (tokenKind != TypeScript.Net.Types.SyntaxKind.Identifier && identifierVisitor.IsIdentifier(start))
                    {
                        finalTokenKind = TypeScript.Net.Types.SyntaxKind.Identifier;
                    }

                    var classification = m_codex.GetClassification(finalTokenKind);
                    if (classification.IsValid || finalTokenKind == TypeScript.Net.Types.SyntaxKind.Identifier)
                    {
                        var span = new CodexSpan()
                                         {
                                             Classification = classification,
                                             Start = start,
                                             Length = length,
                                             Symbol = m_codex.GetDefinitionId(m_sourceFile.Path.AbsolutePath, start)
                                         };

                        if (span.Symbol.IsValid)
                        {
                            span.ReferenceKind = m_codex.Definition;
                        }

                        m_file.Spans.Add(span);
                        m_tokenSpans[(start, length)] = span;
                    }
                }
            }
        }

        /// <summary>
        /// Process a file that was passed via the constructor.
        /// </summary>
        public void VisitSourceFile()
        {
            // Need to register definitions first, and only after that to create all spans.
            RegisterDefinitionLocations();
            CreateSpansAndAssociateDefinitions();
        }

        private void RegisterDefinitionLocations()
        {
            NodeWalker.TraverseDepthFirstAndSelfInOrder(
                m_sourceFile,
                node =>
                {
                    // Do not need to filter out injected nodes.
                    switch (node.Kind)
                    {
                        case TypeScript.Net.Types.SyntaxKind.PropertyAssignment:
                        case TypeScript.Net.Types.SyntaxKind.ShorthandPropertyAssignment:
                            m_nodeNames[node] = RegisterDefinitionName(GetNamedParent(node), node, node.Cast<IObjectLiteralElement>().Name.Text);
                            break;
                        case TypeScript.Net.Types.SyntaxKind.FunctionDeclaration:
                        case TypeScript.Net.Types.SyntaxKind.ModuleDeclaration:
                        case TypeScript.Net.Types.SyntaxKind.IntersectionType:
                        case TypeScript.Net.Types.SyntaxKind.UnionType:
                        case TypeScript.Net.Types.SyntaxKind.TypeAliasDeclaration:

                        case TypeScript.Net.Types.SyntaxKind.VariableDeclaration:
                        case TypeScript.Net.Types.SyntaxKind.EnumDeclaration:
                        case TypeScript.Net.Types.SyntaxKind.EnumMember:
                        case TypeScript.Net.Types.SyntaxKind.PropertyDeclaration:
                        case TypeScript.Net.Types.SyntaxKind.PropertySignature:
                        case TypeScript.Net.Types.SyntaxKind.MethodSignature:
                        case TypeScript.Net.Types.SyntaxKind.MethodDeclaration:
                        case TypeScript.Net.Types.SyntaxKind.InterfaceDeclaration:
                            m_nodeNames[node] = RegisterDefinitionName(node, node.As<IDeclarationStatement>()?.Name ?? node);
                            break;

                        // Need to explicitely register export {name}; and import * as FooBar from 'blah'.
                        case TypeScript.Net.Types.SyntaxKind.NamespaceImport:
                        {
                            var name = node.Cast<IDeclaration>()?.Name;
                            
                            if (name != null)
                            {
                                m_nodeNames[node] = RegisterDefinitionName(name, name, name.Text);
                            }
                        }
                            break;
                        case TypeScript.Net.Types.SyntaxKind.ImportSpecifier:
                            break;
                        case TypeScript.Net.Types.SyntaxKind.ExportSpecifier:
                            m_nodeNames[node] = RegisterDefinitionName(node, node);
                            break;
                    }

                    switch (node.Kind)
                    {
                        case TypeScript.Net.Types.SyntaxKind.ObjectLiteralExpression:
                            return !HasExplicitType(node.Cast<IObjectLiteralExpression>());
                        case TypeScript.Net.Types.SyntaxKind.SourceFile:
                        case TypeScript.Net.Types.SyntaxKind.ModuleDeclaration:
                        case TypeScript.Net.Types.SyntaxKind.VariableDeclaration:
                        case TypeScript.Net.Types.SyntaxKind.EnumDeclaration:
                        case TypeScript.Net.Types.SyntaxKind.InterfaceDeclaration:
                        case TypeScript.Net.Types.SyntaxKind.ModuleBlock:
                        case TypeScript.Net.Types.SyntaxKind.VariableDeclarationList:
                        case TypeScript.Net.Types.SyntaxKind.VariableStatement:
                        
                        case TypeScript.Net.Types.SyntaxKind.PropertyAssignment:

                        case TypeScript.Net.Types.SyntaxKind.ExportDeclaration:
                        case TypeScript.Net.Types.SyntaxKind.NamedExports:
                        case TypeScript.Net.Types.SyntaxKind.NamedImports:
                            return true;
                        case TypeScript.Net.Types.SyntaxKind.ImportDeclaration:
                        case TypeScript.Net.Types.SyntaxKind.ImportClause:
                            return true;
                        default:
                            return false;
                    }
                },
                default);
        }

        private bool HasExplicitType(IObjectLiteralExpression node)
        {
            // returns true if an object literal's type is explicit:
            // 1. const x:Type = {member: value};
            // 2. const x = <Type>{member: value};

            return m_semanticModel.TypeChecker.GetContextualType(node)?.Symbol != null;
        }

        private INode GetNamedParent(INode propertyAssignment)
        {
            var parent = propertyAssignment.Parent;
            while (parent != null)
            {
                if (m_nodeNames.TryGetValue(parent, out var name) && name != null)
                {
                    return parent;
                }

                parent = parent.Parent;
            }

            throw Contract.AssertFailure("Could not find named parent");
        }

        private string RegisterDefinitionName(INode node, INode targetNode, string suffix = null)
        {
            Contract.Requires(node.IsParentFor(targetNode));

            if (node != null && GetFullName(node, out var fullName))
            {
                if (suffix != null)
                {
                    fullName += "." + suffix;
                }

                string moduleBasedFullyQualifiedName = ModuleBasedFullName(fullName);

                var symbol = new CodexSymbol()
                {
                    // TODO: Comments for symbol declaration for tooltip in web site?
                    UniqueId = $"{m_sourceFile.Path.AbsolutePath}|{moduleBasedFullyQualifiedName}",
                    Attributes = string.Empty,
                    Project = m_project.Id,
                    Kind = GetSymbolKind(node),
                    ShortName = suffix ?? node.Cast<IDeclaration>().Name.Text,
                    DisplayName = moduleBasedFullyQualifiedName,
                    SymbolDepth = GetSymbolDepth(fullName) + (suffix != null ? 1 : 0),
                    ContainerQualifiedName = suffix != null ? fullName : GetContainerName(fullName)
                };

                m_codex.RegisterDefinitionSymbol(node, targetNode, symbol);
                return fullName;
            }

            return null;
        }

        private int GetSymbolDepth(string fullName)
        {
            return fullName.Count(c => c == '.');
        }

        private string GetContainerName(string fullName)
        {
            var lastDotIndex = fullName.LastIndexOf('.');
            if (lastDotIndex > 0)
            {
                return fullName.Substring(0, lastDotIndex);
            }

            return string.Empty;
        }

        private string GetSymbolKind(INode node)
        {
            // TODO: Actually compute  NOTE: This may be the variable declaration in the case of a property assignment
            return "symbol";
        }

        private string ModuleBasedFullName(string name) => $"{m_moduleName}::{name}";

        private bool GetFullName(INode node, out string fullyQualifiedName)
        {
            if (m_nodeNames.TryGetValue(node, out fullyQualifiedName))
            {
                return true;
            }

            var symbol = node.Symbol ?? node.ResolvedSymbol ?? m_semanticModel.GetSymbolAtLocation(node);
            if (symbol == null)
            {
                return false;
            }

            var id = m_semanticModel.GetFullyQualifiedName(symbol);

            // TODO: Handle all whitespace or fix properly
            fullyQualifiedName = id.Replace(" ", string.Empty).Replace("\r", string.Empty).Replace("\n", string.Empty);
            return true;
        }

        private class IdentifierVisitor : DfsVisitor
        {
            private readonly BitArray m_identifierByStart;

            public IdentifierVisitor(ISourceFile sourceFile)
            {
                m_identifierByStart = new BitArray(sourceFile.Text.Length);
                VisitSourceFile(sourceFile);
            }

            public bool IsIdentifier(int start)
            {
                return m_identifierByStart[start];
            }

            public override void VisitIdentifier(IIdentifier node)
            {
                m_identifierByStart[node.Pos] = true;
                base.VisitIdentifier(node);
            }
        }
    }
}

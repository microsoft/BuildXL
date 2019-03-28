// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using Codex.Analysis.External;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using TypeScript.Net.DScript;
using TypeScript.Net.Types;
using ISymbol = TypeScript.Net.Types.ISymbol;

namespace BuildXL.FrontEnd.Script.Analyzer.Codex
{
    internal sealed class CodexContext
    {
        public CodexId<CodexClassification> Keyword { get; }
        public CodexId<CodexClassification> Identifier { get; }
        public CodexId<CodexClassification> InterfaceName { get; }
        public CodexId<CodexClassification> EnumName { get; }
        public CodexId<CodexClassification> TypeParameterName { get; }
        public CodexId<CodexClassification> Function { get; }
        public CodexId<CodexClassification> Property { get; }
        public CodexId<CodexClassification> Parameter { get; }
        
        public CodexId<CodexRefKind> Definition { get; }
        public CodexId<CodexRefKind> InterfaceInheritance { get; }
        public CodexId<CodexRefKind> Reference { get; }
        public CodexId<CodexRefKind> FunctionScopedVariable { get; }
        public CodexId<CodexRefKind> Read { get; }
        public CodexId<CodexRefKind> Write { get; }

        public CodexSemanticStore Store { get; }

        private readonly Dictionary<CodexSpan, (string path, int position)> m_spanToDefinitionMap = new Dictionary<CodexSpan, (string path, int position)>();
        private readonly Dictionary<(string path, int position), CodexSymbol> m_definitionSymbolMap = new Dictionary<(string path, int position), CodexSymbol>();
        private readonly Dictionary<(string path, int position), CodexId<object>> m_localIdMap = new Dictionary<(string path, int position), CodexId<object>>();

        /// <summary>
        /// The map from (path, node position) to 'actual position'. Useful when the target position is different then a resolved node's position.
        /// For instance, function declaration's position is not what we need. We need a position for the name.
        /// </summary>
        private readonly Dictionary<(string path, int position), (int targetNodePosition, int targetNodeLength)> m_definitionAdjustmentMap =
            new Dictionary<(string path, int position), (int targetNodePosition, int targetNodeLength)>();

        private readonly CodexId<CodexClassification>[] m_classificationsBySyntaxKind;

        private readonly GotoDefinitionHelper m_gotoDefinitionHelper;

        public CodexContext(CodexSemanticStore store, Workspace workspace)
        {
            Store = store;

            m_classificationsBySyntaxKind = GetClassificationsBySyntaxKind();
            m_gotoDefinitionHelper = new GotoDefinitionHelper(workspace.GetSemanticModel().TypeChecker);

            Keyword = store.Classifications.Add(new CodexClassification() { Name = "keyword" });
            Identifier = store.Classifications.Add(new CodexClassification() { Name = "identifier" });
            InterfaceName = store.Classifications.Add(new CodexClassification() { Name = "interface name" });
            EnumName = store.Classifications.Add(new CodexClassification() { Name = "enum name" });
            TypeParameterName = store.Classifications.Add(new CodexClassification() { Name = "type parameter name" });
            Function = store.Classifications.Add(new CodexClassification() { Name = "method name" });
            Property = store.Classifications.Add(new CodexClassification() { Name = "property name" });
            Parameter = store.Classifications.Add(new CodexClassification() { Name = "parameter name" });

            Definition = store.GetWellKnownId(WellKnownReferenceKinds.Definition);
            InterfaceInheritance = store.GetWellKnownId(WellKnownReferenceKinds.InterfaceInheritance);
            Reference = store.GetWellKnownId(WellKnownReferenceKinds.Reference);
            FunctionScopedVariable = store.ReferenceKinds.Add(new CodexRefKind() {Name = "FunctionScoped"});
            Read = store.GetWellKnownId(WellKnownReferenceKinds.Read);
            Write = store.GetWellKnownId(WellKnownReferenceKinds.Write);
        }

        public CodexId<CodexClassification> GetClassification(TypeScript.Net.Types.SyntaxKind kind)
        {
            return m_classificationsBySyntaxKind[(int)kind];
        }

        public void RegisterDefinitionSymbol(INode node, INode targetNode, CodexSymbol symbol)
        {
            ISourceFile sourceFile = node.GetSourceFile();
            var nodeStartPosition = node.GetNodeStartPositionWithoutTrivia(sourceFile);
            var targetNodeStartPosition = targetNode.GetNodeStartPositionWithoutTrivia(sourceFile);

            string path = sourceFile.Path.AbsolutePath;

            if (nodeStartPosition != targetNodeStartPosition)
            {
                m_definitionAdjustmentMap[(path, nodeStartPosition)] = (targetNodeStartPosition, targetNode.GetNodeWidth(sourceFile));
            }

            m_definitionSymbolMap[(path, targetNodeStartPosition)] = symbol;
        }

        public CodexId<CodexSymbol> GetDefinitionId(string path, int position)
        {
            if (m_definitionSymbolMap.TryGetValue((path, position), out var symbol))
            {
                return Store.Symbols.Add(symbol);
            }

            return default;
        }

        public void RegisterLocalSymbolDefinition(string path, int position, CodexId<object> localId)
        {
            m_localIdMap[(path, position)] = localId;
        }

        public CodexId<CodexClassification> GetClassificationForSymbol(ISymbol symbol)
        {
            if (IsInterface(symbol) || IsTypeAlias(symbol))
            {
                return InterfaceName;
            }

            if (IsTypeParameter(symbol))
            {
                return TypeParameterName;
            }

            if (IsEnum(symbol))
            {
                return EnumName;
            }

            return Identifier;
        }

        public bool RegisterSymbolReference(CodexSpan referencingSpan, INode node)
        {
            if (m_spanToDefinitionMap.ContainsKey(referencingSpan))
            {
                // Contract.AssertFailure($"Can't find span for referencing node '{node.ToDisplayString()}'");
                return false;
            }

            var locations = m_gotoDefinitionHelper.GetDefinitionAtPosition(node);
            if (locations.Succeeded && locations.Result.Count != 0)
            {
                var location = locations.Result[0];
                if (location.Symbol != null && !referencingSpan.Classification.IsValid)
                {
                    var classification = GetClassificationForSymbol(location.Symbol);
                    referencingSpan.Classification = classification;
                }

                m_spanToDefinitionMap[referencingSpan] = (path: location.Path, location.Range.Start);
                return true;
            }

            return false;
        }

        public void LinkSpans()
        {
            foreach (var spanAndDefinitionLocation in m_spanToDefinitionMap)
            {
                var span = spanAndDefinitionLocation.Key;

                var definitionLocation = AdjustPosition(spanAndDefinitionLocation.Value.path, spanAndDefinitionLocation.Value.position);

                if (m_localIdMap.TryGetValue(definitionLocation, out var localId))
                {
                    span.LocalId = localId;
                }
                else if (m_definitionSymbolMap.TryGetValue(definitionLocation, out var symbol))
                {
                    if (!span.Symbol.IsValid)
                    {
                        span.Symbol = Store.Symbols.Add(symbol);
                    }

                    if (!span.ReferenceKind.IsValid)
                    {
                        span.ReferenceKind = Reference;
                    }
                }
                else
                {
                    Console.WriteLine($"Span defined at '{definitionLocation.path}:{definitionLocation.position}' is not resolved.");
                }
            }
        }

        private (string path, int position) AdjustPosition(string path, int position)
        {
            if (m_definitionAdjustmentMap.TryGetValue((path, position), out var adjustment))
            {
                position = adjustment.targetNodePosition;
            }

            return (path, position);
        }

        private static string GetClassificationName(TypeScript.Net.Types.SyntaxKind kind)
        {
            if (kind.ToString().ToLowerInvariant().Contains("keyword"))
            {
                return "keyword";
            }
            else if (kind == TypeScript.Net.Types.SyntaxKind.StringLiteral || IsTemplateSpan(kind))
            {
                return "string";
            }
            else if (kind == TypeScript.Net.Types.SyntaxKind.SingleLineCommentTrivia || kind == TypeScript.Net.Types.SyntaxKind.MultiLineCommentTrivia)
            {
                return "comment";
            }
            else if (kind == TypeScript.Net.Types.SyntaxKind.Decorator)
            {
                return "preprocessor keyword";
            }
            else
            {
                return string.Empty;
            }
        }

        private static bool IsTemplateSpan(TypeScript.Net.Types.SyntaxKind kind)
        {
            return kind == TypeScript.Net.Types.SyntaxKind.TaggedTemplateExpression || kind == TypeScript.Net.Types.SyntaxKind.TemplateExpression ||
                   kind == TypeScript.Net.Types.SyntaxKind.NoSubstitutionTemplateLiteral || kind == TypeScript.Net.Types.SyntaxKind.FirstTemplateToken ||
                   kind == TypeScript.Net.Types.SyntaxKind.LastTemplateToken || kind == TypeScript.Net.Types.SyntaxKind.TemplateHead ||
                   kind == TypeScript.Net.Types.SyntaxKind.TemplateMiddle;
        }

        private CodexId<CodexClassification>[] GetClassificationsBySyntaxKind()
        {
            var classes = new CodexId<CodexClassification>[(int)TypeScript.Net.Types.SyntaxKind.Count];
            foreach (var kind in Enum.GetValues(typeof(TypeScript.Net.Types.SyntaxKind)).OfType<TypeScript.Net.Types.SyntaxKind>().Where(kind => kind < TypeScript.Net.Types.SyntaxKind.Count))
            {
                classes[(int)kind] = GetId(GetClassificationName(kind));
            }

            return classes;
        }

        private CodexId<CodexClassification> GetId(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return CodexId<CodexClassification>.Invalid;
            }

            return Store.Classifications.Add(new CodexClassification() { Name = name });
        }

        private bool IsTypeParameter(ISymbol symbol)
        {
            return (symbol.Flags & SymbolFlags.TypeParameter) == SymbolFlags.TypeParameter;
        }

        private bool IsInterface(ISymbol symbol)
        {
            return (symbol.Flags & SymbolFlags.Interface) == SymbolFlags.Interface;
        }

        private bool IsTypeLiteral(ISymbol symbol)
        {
            return (symbol.Flags & SymbolFlags.TypeLiteral) == SymbolFlags.TypeLiteral;
        }

        private bool IsTypeAlias(ISymbol symbol)
        {
            return (symbol.Flags & SymbolFlags.TypeAlias) == SymbolFlags.TypeAlias;
        }

        private bool IsEnum(ISymbol symbol)
        {
            return (symbol.Flags & SymbolFlags.Enum) == SymbolFlags.Enum;
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.FrontEnd.Workspaces.Core;
using Codex.Analysis.External;
using TypeScript.Net.DScript;
using TypeScript.Net.Parsing;
using TypeScript.Net.Scanning;
using TypeScript.Net.Types;

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
        private readonly Workspace m_workspace;
        private readonly BuildXL.Utilities.Core.PathTable m_pathTable;

        public CodexContext(CodexSemanticStore store, Workspace workspace, BuildXL.Utilities.Core.PathTable pathTable)
        {
            Store = store;

            m_workspace = workspace;
            m_pathTable = pathTable;
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

            if (!TryGetGoToDefinition(node, out var definitionLocation))
            {
                return false;
            }
            
            if (definitionLocation.symbolLocation != null && !referencingSpan.Classification.IsValid)
            {
                var classification = GetClassificationForSymbol(definitionLocation.symbolLocation.Symbol);
                referencingSpan.Classification = classification;
            }

            m_spanToDefinitionMap[referencingSpan] = (definitionLocation.path, definitionLocation.position);
            return true;
        }

        public bool TryGetGoToDefinition(INode node, out (string path, int position, SymbolLocation symbolLocation) definitionLocation)
        {
            // The go-to-definition helper does not handle the module specifier for import declarations (i.e. import {blah} from "module specified")
            // Let's make it point to the module definition file
            if (node.Parent != null &&
                 node.Parent.Kind == TypeScript.Net.Types.SyntaxKind.ImportDeclaration &&
                 node.Parent.As<IImportDeclaration>().ModuleSpecifier == node)
            {
                if (TryGetModuleSourceFile(node, out var moduleSourceFile))
                {
                    definitionLocation = (path: moduleSourceFile.Path.AbsolutePath, position: 0, symbolLocation: GotoDefinitionHelper.GetLocationFromNode(moduleSourceFile, moduleSourceFile.Symbol));
                    return true;
                }
            }

            // Similarly to import declarations, importFrom is not handled by the go-to-definition helper. Treat that explicitly here
            if (node.IsImportFrom())
            {
                var module = m_workspace.Modules.FirstOrDefault(module => module.Descriptor.Name == node.GetSpecifierInImportFrom().Text);
                if (module != null)
                {
                    var moduleConfigFile = m_workspace.GetEffectiveModuleConfigPath(module, m_pathTable);
                    // Some generated module configuration files are not really part of the workspace (e.g. what the nuget resolver generates),
                    // so let's make sure
                    if (m_workspace.ConfigurationModule.Specs.TryGetValue(moduleConfigFile, out var moduleSourceFile))
                    {
                        definitionLocation = (path: moduleSourceFile.Path.AbsolutePath, position: 0, symbolLocation: GotoDefinitionHelper.GetLocationFromNode(moduleSourceFile, moduleSourceFile.Symbol));
                        return true;
                    }
                }
            }

            var locations = m_gotoDefinitionHelper.GetDefinitionAtPosition(node);
            if (locations.Succeeded && locations.Result.Count != 0)
            {
                // If any of the node go-to locations points to the node itself, this is the indication of anonymous literals
                // that are not actually definitions. This triggers a bunch of spurious unresolved spans and the definition is not
                // really useful anyway
                if (locations.Result.Any(location => location.Path == node.GetSourceFile().Path.AbsolutePath &&
                    location.Range.Start == node.GetNodeStartPositionWithoutTrivia()))
                {
                    definitionLocation = default;
                    return false;
                }

                var location = locations.Result[0];

                definitionLocation = (path: location.Path, location.Range.Start, symbolLocation: location);
                return true;
            }

            definitionLocation = default;
            return false;
        }

        private bool TryGetModuleSourceFile(INode node, out ISourceFile moduleSourceFile)
        {
            var symbol = m_workspace.GetSemanticModel()?.GetSymbolAtLocation(node) ?? node.Symbol ?? node.ResolvedSymbol;
            if (symbol != null && symbol.IsBuildXLModule)
            {
                // Let's find the module and retrieve its spec
                var module = m_workspace.Modules.FirstOrDefault(module => module.Descriptor.Name == symbol.Name);
                if (module != null)
                {
                    var moduleConfigFile = m_workspace.GetEffectiveModuleConfigPath(module, m_pathTable);
                    // Some generated module configuration files are not really part of the workspace (e.g. what the nuget resolver generates),
                    // so let's make sure
                    if (m_workspace.ConfigurationModule.Specs.TryGetValue(moduleConfigFile, out moduleSourceFile))
                    {
                        return true;
                    }
                }
            }

            moduleSourceFile = default;
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
                    var sourceFile = m_workspace.GetSourceFile(BuildXL.Utilities.Core.AbsolutePath.Create(m_pathTable, spanAndDefinitionLocation.Value.path));
                    var lineAndColumn = Scanner.GetLineAndCharacterOfPosition(sourceFile, spanAndDefinitionLocation.Value.position);
                    Console.WriteLine($"Span defined at '{definitionLocation.path} ({lineAndColumn.Line}, {lineAndColumn.Character})[Pos:{definitionLocation.position}]' is not resolved.");
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

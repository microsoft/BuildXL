// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.Utilities;
using JetBrains.Annotations;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.Types;
using ISymbol = TypeScript.Net.Types.ISymbol;

namespace BuildXL.FrontEnd.Workspaces
{
    /// <summary>
    /// Semantic model for BuildXL Script specifications.
    /// </summary>
    public sealed class SemanticModel : ISemanticModel
    {
        [CanBeNull]
        private HashSet<ISourceFile> m_filteredOutSpecs;

        /// <nodoc/>
        public SemanticModel(ITypeChecker typeChecker)
        {
            Contract.Requires(typeChecker != null);

            TypeChecker = typeChecker;
        }

        /// <inheritdoc/>
        /// TODO: We should remove this member and force everyone to go through the semantic model
        public ITypeChecker TypeChecker { get; }

        /// <inheritdoc/>
        public IEnumerable<Diagnostic> GetAllSemanticDiagnostics()
        {
            return TypeChecker.GetGlobalDiagnostics().Union(TypeChecker.GetDiagnostics());
        }

        /// <inheritdoc/>
        public IEnumerable<Diagnostic> GetTypeCheckingDiagnosticsForFile(ISourceFile file)
        {
            Contract.Requires(file != null);
            return TypeChecker.GetDiagnostics(sourceFile: file);
        }

        /// <inheritdoc/>
        public string TryGetResolvedModulePath(ISourceFile sourceFile, string referencedModuleName)
        {
            Contract.Requires(sourceFile != null);
            Contract.Requires(referencedModuleName != null);
            return TypeChecker.TryGetResolvedModulePath(sourceFile, referencedModuleName, m_filteredOutSpecs);
        }

        /// <inheritdoc/>
        public RoaringBitSet GetFileDependentFilesOf(ISourceFile sourceFile)
        {
            Contract.Requires(sourceFile != null);
            return TypeChecker.GetFileDependentsOf(sourceFile);
        }

        /// <inheritdoc/>
        public RoaringBitSet GetFileDependenciesOf(ISourceFile sourceFile)
        {
            Contract.Requires(sourceFile != null);
            return TypeChecker.GetFileDependenciesOf(sourceFile);
        }

        /// <inheritdoc/>
        public HashSet<string> GetModuleDependentsOf(ISourceFile sourceFile)
        {
            Contract.Requires(sourceFile != null);
            return TypeChecker.GetModuleDependenciesOf(sourceFile);
        }

        /// <inheritdoc/>
        public IType GetCurrentQualifierType(INode currentNode)
        {
            Contract.Requires(currentNode != null, "currentNode != null");

            var declaration = GetCurrentQualifierDeclaration(currentNode);
            if (declaration == null)
            {
                return null;
            }

            var type = TypeChecker.GetTypeAtLocation(declaration);
            return type;
        }

        /// <inheritdoc/>
        public INode GetCurrentQualifierDeclaration(INode currentNode)
        {
            Contract.Requires(currentNode != null, "currentNode != null");

            var symbol = TypeChecker.ResolveEntryByName(currentNode, Names.CurrentQualifier, SymbolFlags.BlockScopedVariable);

            // In the production code this should never happen. But it happens in tests.
            // TODO: remove the check.
            if (symbol == null)
            {
                return null;
            }

            return symbol.DeclarationList.FirstOrDefault();
        }

        /// <inheritdoc/>
        public ISymbol GetTemplateAtLocation(INode node)
        {
            Contract.Requires(node != null, "node != null");
            var symbol = TypeChecker.ResolveEntryByName(node, Names.Template, SymbolFlags.BlockScopedVariable);
            return symbol;
        }

        /// <inheritdoc/>
        public bool IsNamespaceType(INode currentNode)
        {
            Contract.Requires(currentNode != null, "currentNode != null");
            var type = TypeChecker.GetTypeAtLocation(currentNode);

            return IsNamespaceType(type);
        }

        /// <inheritdoc/>
        public bool IsNamespaceType(ISymbol thisSymbol)
        {
            Contract.Requires(thisSymbol != null, "symbol != null");
            if ((thisSymbol.Flags & SymbolFlags.ValueModule) == SymbolFlags.ValueModule)
            {
                return true;
            }

            var typeOfNode = TypeChecker.GetTypeOfSymbolAtLocation(thisSymbol, thisSymbol.DeclarationList.First());

            return IsNamespaceType(typeOfNode);
        }

        private static bool IsNamespaceType(IType type)
        {
            return (type.Flags & TypeFlags.Anonymous) == TypeFlags.Anonymous &&
                    (type.Symbol?.Flags & SymbolFlags.ValueModule) == SymbolFlags.ValueModule;
        }

        /// <inheritdoc/>
        public string GetFullyQualifiedName(ISymbol symbol)
        {
            Contract.Requires(symbol != null, "symbol != null");
            return TypeChecker.GetFullyQualifiedName(symbol);
        }

        /// <inheritdoc/>
        public ISymbol GetAliasedSymbol(ISymbol symbol, bool resolveAliasRecursively = true)
        {
            Contract.Requires(symbol != null, "symbol != null");
            return TypeChecker.GetAliasedSymbol(symbol, resolveAliasRecursively);
        }

        /// <inheritdoc/>
        public ISymbol GetShorthandAssignmentValueSymbol(INode location)
        {
            Contract.Requires(location != null, "location != null");
            return TypeChecker.GetShorthandAssignmentValueSymbol(location);
        }

        /// <inheritdoc/>
        public ISymbol GetSymbolAtLocation(INode node)
        {
            Contract.Requires(node != null, "node != null");
            return TypeChecker.GetSymbolAtLocation(node);
        }

        /// <inheritdoc/>
        public IType GetTypeAtLocation(INode node)
        {
            Contract.Requires(node != null, "node != null");
            return TypeChecker.GetTypeAtLocation(node);
        }

        /// <inheritdoc/>
        public bool TryPrintReturnTypeOfSignature(
            ISignatureDeclaration signatureDeclaration,
            out string result,
            INode enclosingDeclaration = null,
            TypeFormatFlags flags = TypeFormatFlags.None)
        {
            Contract.Requires(signatureDeclaration != null, "signatureDeclaration != null");

            var signature = TypeChecker.GetSignatureFromDeclaration(signatureDeclaration);
            var type = TypeChecker.GetReturnTypeOfSignature(signature);

            return TryPrintType(type, out result, enclosingDeclaration, flags);
        }

        /// <inheritdoc/>
        public bool TryPrintType(IType type, out string result, INode enclosingDeclaration = null, TypeFormatFlags flags = TypeFormatFlags.None)
        {
            Contract.Requires(type != null, "type != null");

            var writer = SymbolWriterPool.GetSingleLineStringWriter(TypeChecker);

            result = TypeChecker.TypeToString(type, enclosingDeclaration, flags, writer);

            SymbolWriterPool.ReleaseStringWriter(writer);

            return !writer.IsInaccessibleErrorReported();
        }

        /// <inheritdoc />
        public void FilterWasApplied(HashSet<ISourceFile> filteredOutSpecs)
        {
            Contract.Requires(filteredOutSpecs != null, "filteredOutSpecs != null");
            m_filteredOutSpecs = filteredOutSpecs;
        }

        /// <inheritdoc />
        public IDeclaration GetFirstNotFilteredDeclarationOrDefault(ISymbol resolvedSymbol)
        {
            if (resolvedSymbol == null)
            {
                return null;
            }

            // Not using LINQ intentionally to avoid allocations.
            // This is a hot path.
            if (m_filteredOutSpecs == null)
            {
                return resolvedSymbol.GetFirstDeclarationOrDefault();
            }

            foreach (var declaration in resolvedSymbol.DeclarationList)
            {
                if (declaration != null && !m_filteredOutSpecs.Contains(declaration.GetSourceFile()))
                {
                    return declaration;
                }
            }

            return null;
        }
    }
}

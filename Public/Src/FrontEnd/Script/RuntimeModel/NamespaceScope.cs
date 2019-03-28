// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.RuntimeModel
{
    using SyntaxKind = TypeScript.Net.Types.SyntaxKind;
    
    /// <summary>
    ///     Scope for namespace-level values or top level file values.
    /// </summary>
    /// <remarks>
    /// This class name isn't perfect, but we can't call it 'global scope' because BuildXL Script would have a new feature for
    /// implicit visibility inside the module. And there global scope would have a different meaning.
    /// </remarks>
    public sealed class NamespaceScope
    {
        private readonly ISourceFile m_sourceFile;
        private readonly StringTable m_stringTable;
        private readonly PathTable m_pathTable;
        private readonly NamespaceScope m_parent;

        /// <summary>
        ///     Mapping from variable name to its definition.
        /// </summary>
        private readonly Dictionary<SymbolAtom, VariableDefinition> m_scope = new Dictionary<SymbolAtom, VariableDefinition>();

        /// <nodoc />
        public NamespaceScope(List<SymbolAtom> fullName, ISymbolTable symbolTable, ISourceFile sourceFile, PathTable pathTable, StringTable stringTable, NamespaceScope parent = null)
        {
            Contract.Requires(symbolTable != null);
            Contract.Requires(sourceFile != null);
            Contract.Requires(stringTable != null);

            m_sourceFile = sourceFile;
            m_stringTable = stringTable;
            m_parent = parent;
            m_pathTable = pathTable;
            FullName = fullName;

            PopulateFromSymbolTable(symbolTable);
        }

        /// <summary>
        /// Full name of the namespace. Can be empty.
        /// </summary>
        public List<SymbolAtom> FullName { get; }

        /// <summary>
        /// Creates new <see cref="FunctionScope"/> for with a given <paramref name="symbolTable"/>.
        /// </summary>
        public FunctionScope NewFunctionScope(ISymbolTable symbolTable)
        {
            Contract.Requires(symbolTable != null);

            var scope = new FunctionScope(m_pathTable, m_stringTable, m_sourceFile, this);

            scope.PopulateFromSymbolTable(symbolTable);

            return scope;
        }

        /// <summary>
        ///     Resolves a variable name by following the parent chain.
        /// </summary>
        public bool TryResolveRecursively(SymbolAtom name, out VariableDefinition definition)
        {
            definition = default(VariableDefinition);
            var current = this;

            while (current != null)
            {
                if (current.m_scope.TryGetValue(name, out definition))
                {
                    return true;
                }

                current = current.m_parent;
            }

            return false;
        }

        /// <summary>
        ///     Resolves a variable name by following the parent chain.
        /// </summary>
        public VariableDefinition? ResolveRecursively(SymbolAtom name)
        {
            if (TryResolveRecursively(name, out VariableDefinition result))
            {
                return result;
            }

            return null;
        }

        /// <summary>
        ///     Adds global variable declaration.
        /// </summary>
        private bool TryAdd(SymbolAtom name, in UniversalLocation location)
        {
            if (m_scope.ContainsKey(name))
            {
                return false;
            }

            m_scope.Add(name, VariableDefinition.CreateGlobal(name, location));
            return true;
        }

        private void PopulateFromSymbolTable(ISymbolTable symbolTable)
        {
            var specPath = m_sourceFile.GetAbsolutePath(m_pathTable);
            foreach (var kvp in symbolTable)
            {
                var symbol = kvp.Value;
                var declaration = symbol.DeclarationList.First();
                if (declaration.Kind != SyntaxKind.VariableDeclaration)
                {
                    // There is no top-level parameters, so variables are the only values that
                    // could be added into the storage.
                    continue;
                }

                var atom = SymbolAtom.Create(m_stringTable, declaration.Name.Text);

                var location = declaration.Location(m_sourceFile, specPath, m_pathTable);

                bool wasAdded = TryAdd(atom, location);
                if (!wasAdded)
                {
                    Contract.Assume(
                        false,
                        I($"Found duplicate variable '{declaration.Name.Text}' in a source file. This should never happen because binder should fail on it!"));
                }
            }
        }
    }
}

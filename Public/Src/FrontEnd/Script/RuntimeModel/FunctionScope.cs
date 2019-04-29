// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.Utilities;
using JetBrains.Annotations;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.RuntimeModel
{
    /// <summary>
    /// Scope for local variables.
    /// </summary>
    /// <remarks>
    /// Current design has following scopes:
    /// - Namespace-level scope (<see cref="NamespaceScope"/>).
    /// - Function scope (or rather 'evaluation' scope) (<see cref="FunctionScope"/>)
    /// - Block scope
    ///
    /// Namespace-level is very similar to function scope, but it can hold only values from the namespace or file-level.
    ///
    /// Function scope represents locals for a function and its parameters.
    /// Functions could be nested in DScript/TypeScript and to build efficient run-time representation
    /// for evaluation, Function scope tracks indices that spans nested functions.
    /// DScript evaluation engine uses one stack frame for nested functions.
    /// It means that if function1 has local function2, then all locals from both of them
    /// would be represented in one continuous array of arguments in memory.
    ///
    /// Functions also could have blocks that introduces new lexical scopes. So <see cref="BlockScope"/> models that concept.
    ///
    /// </remarks>
    public sealed class FunctionScope
    {
        /// <summary>
        ///     Parent scope.
        /// </summary>
        [CanBeNull]
        private readonly FunctionScope m_parent;

        /// <summary>
        ///     Stack of block scoped variables.
        /// </summary>
        private readonly Stack<BlockScope> m_scopedVarsStack;

        /// <summary>
        ///     Next available index.
        /// </summary>
        /// <remarks>
        ///     This field is shared by all scoped variables so that the evaluator later
        ///     does not have to allocate a new object array just for open scope.
        /// </remarks>
        private int m_nextIndex;

        [CanBeNull]
        private readonly StringTable m_stringTable;

        [CanBeNull]
        private readonly PathTable m_pathTable;

        private readonly AbsolutePath m_sourceFilePath;

        [CanBeNull]
        private readonly ISourceFile m_sourceFile;

        [CanBeNull]
        private readonly NamespaceScope m_namespaceScope;

        /// <summary>
        /// Default constructor.
        /// </summary>
        /// <remarks>
        /// Used only by an empty scope and by the debugger.
        /// In this case, string table is null and any attempts to create a nested scope
        /// by a symbol table will fail.
        /// </remarks>
        public FunctionScope()
        {
            m_scopedVarsStack = new Stack<BlockScope>();
            m_scopedVarsStack.Push(new BlockScope(this));
        }

        /// <summary>
        /// Constructs a function scope with a parent <paramref name="namespaceScope"/>.
        /// </summary>
        internal FunctionScope(PathTable pathTable, StringTable stringTable, ISourceFile sourceFile, NamespaceScope namespaceScope)
            : this()
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(stringTable != null);
            Contract.Requires(sourceFile != null);

            m_pathTable = pathTable;
            m_stringTable = stringTable;
            m_sourceFile = sourceFile;
            m_namespaceScope = namespaceScope;
            m_sourceFilePath = sourceFile.GetAbsolutePath(pathTable);
        }

        private FunctionScope(PathTable pathTable, StringTable stringTable, ISourceFile sourceFile, FunctionScope parentScope)
        {
            Contract.Requires(stringTable != null);
            Contract.Requires(sourceFile != null);

            m_stringTable = stringTable;
            m_sourceFile = sourceFile;
            m_pathTable = pathTable;

            m_nextIndex = parentScope.m_nextIndex;
            Captures = m_nextIndex;
            m_scopedVarsStack = new Stack<BlockScope>();
            m_scopedVarsStack.Push(new BlockScope(this));
            m_parent = parentScope;
            m_namespaceScope = null;
            m_sourceFilePath = sourceFile.GetAbsolutePath(pathTable);
        }

        /// <summary>
        /// Creates a nested function scope with a given <paramref name="symbolTable"/>.
        /// </summary>
        public FunctionScope CreateNestedScope(ISymbolTable symbolTable)
        {
            Contract.Requires(symbolTable != null);

            Contract.Assert(m_stringTable != null, "Are you tring to create a nested scope from an empty function scope?");
            Contract.Assert(m_sourceFile != null, "Are you tring to create a nested scope from an empty function scope?");

            var result = new FunctionScope(m_pathTable, m_stringTable, m_sourceFile, this);
            result.PopulateFromSymbolTable(symbolTable);

            return result;
        }

        internal static FunctionScope Empty() => new FunctionScope();

        /// <summary>
        /// Populates current scope with a given <paramref name="symbolTable"/>.
        /// </summary>
        internal void PopulateFromSymbolTable(ISymbolTable symbolTable)
        {
            foreach (var kvp in symbolTable)
            {
                var symbol = kvp.Value;
                var declaration = symbol.DeclarationList.First();
                if (declaration.Kind != TypeScript.Net.Types.SyntaxKind.VariableDeclaration && declaration.Kind != TypeScript.Net.Types.SyntaxKind.Parameter)
                {
                    // Need to register only variable declarations!
                    continue;
                }

                // TODO: next statment will fail even for valid names.
                // For instance, $ is not a valid character for SymbolAtom but it is a valid identifier.
                // This logic needs to be revisit.
                var atom = SymbolAtom.Create(m_stringTable, declaration.Name.Text);

                var location = declaration.Location(m_sourceFile, m_sourceFilePath, m_pathTable);

                bool isConstant = NodeUtilities.IsConst(declaration);

                var index = AddVariable(atom, location, isConstant);
                Contract.Assume(
                    index != null,
                    I($"Found duplicate variable '{declaration.Name.Text}' in a source file. This should never happen because binder should fail on it!"));
            }
        }

        private BlockScope Top
        {
            get
            {
                Contract.Ensures(Contract.Result<BlockScope>() != null);

                return m_scopedVarsStack.Peek();
            }
        }

        /// <summary>
        ///     Number of captured variables.
        /// </summary>
        public int Captures { get; }

        /// <summary>
        ///     Number of locals.
        /// </summary>
        public int Locals => m_nextIndex - Captures;

        /// <summary>
        ///     Number of scoped variable set.
        /// </summary>
        public int ScopeCount => m_scopedVarsStack.Count;

        /// <summary>
        ///     Gets the nearest global scope.
        /// </summary>
        public NamespaceScope GetGlobalScope()
        {
            var current = this;
            while (current != null)
            {
                if (current.m_namespaceScope != null)
                {
                    return current.m_namespaceScope;
                }

                current = current.m_parent;
            }

            return null;
        }

        /// <summary>
        ///     Adds a variable local to the function with location and returns its assigned index.
        /// </summary>
        /// <remarks>
        /// This function is used only by the debugger. TODO: reconsider the design to avoid exposing this function.
        /// </remarks>
        public int? AddVariable(SymbolAtom name, in UniversalLocation location, bool isConstant)
        {
            Contract.Requires(name.IsValid);

            return Top.AddVariable(name, location, isConstant);
        }

        /// <summary>
        ///     Gets variable definition by specified name from current scope.
        /// </summary>
        public bool TryResolveFromCurrentFunctionScope(SymbolAtom name, out VariableDefinition result)
        {
            result = default(VariableDefinition);

            foreach (var scopedVars in m_scopedVarsStack)
            {
                if (scopedVars.TryGetVariableByName(name, out result))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        ///     Gets variable definition by specified name from current scope.
        /// </summary>
        public VariableDefinition? ResolveFromCurrentFunctionScope(SymbolAtom name)
        {
            if (TryResolveFromCurrentFunctionScope(name, out VariableDefinition result))
            {
                return result;
            }

            return null;
        }

        /// <summary>
        ///     Gets variable definition by specified name from current or one of the parent scopes.
        /// </summary>
        /// <remarks>
        /// This function won't look at the global scope!
        /// </remarks>
        public bool TryResolveFromFunctionScopeRecursively(SymbolAtom name, out VariableDefinition result)
        {
            result = default(VariableDefinition);

            foreach (var scopedVars in m_scopedVarsStack)
            {
                if (scopedVars.TryGetVariableByName(name, out result))
                {
                    return true;
                }
            }

            return m_parent?.TryResolveFromFunctionScopeRecursively(name, out result) == true;
        }

        /// <summary>
        ///     Gets variable definition by specified name from current or one of the parent scopes.
        /// </summary>
        /// <remarks>
        /// This function won't look at the global scope!
        /// </remarks>
        public VariableDefinition? ResolveFromFunctionScopeRecursivelyIncludingGlobalScope(SymbolAtom name)
        {
            if (TryResolveFromFunctionScopeRecursively(name, out VariableDefinition result))
            {
                return result;
            }

            return GetGlobalScope().ResolveRecursively(name);
        }

        /// <summary>
        ///     Pushes a new scope, i.e., a new block.
        /// </summary>
        public void PushBlockScope(ISymbolTable symbolTable)
        {
            Contract.Requires(symbolTable != null);

            m_scopedVarsStack.Push(new BlockScope(this));
            PopulateFromSymbolTable(symbolTable);
        }

        /// <summary>
        ///     Pops the top scope.
        /// </summary>
        public void PopBlockScope()
        {
            Contract.Requires(ScopeCount > 0);
            m_scopedVarsStack.Pop();
        }

        /// <summary>
        ///     Table with local variables for the current block scope.
        /// </summary>
        private sealed class BlockScope
        {
            private readonly FunctionScope m_owner;

            private readonly Dictionary<SymbolAtom, VariableDefinition> m_symbols =
                new Dictionary<SymbolAtom, VariableDefinition>();

            public BlockScope(FunctionScope owner)
            {
                m_owner = owner;
            }

            /// <summary>
            ///     Adds a variable identified by its name.
            /// </summary>
            /// <return>
            ///     Retuns index of the local variable in the table, or null if the variable with
            ///     provided name already exists.
            /// </return>
            public int? AddVariable(SymbolAtom name, in UniversalLocation location, bool isConstant)
            {
                Contract.Requires(name.IsValid);

                if (m_symbols.ContainsKey(name))
                {
                    return null;
                }

                m_symbols[name] = VariableDefinition.CreateLocal(name, m_owner.m_nextIndex, location, isConstant);
                return m_owner.m_nextIndex++;
            }

            /// <summary>
            ///     Tries get the index of a name.
            /// </summary>
            public bool TryGetVariableByName(SymbolAtom name, out VariableDefinition variableDefinition)
            {
                Contract.Requires(name.IsValid);
                return m_symbols.TryGetValue(name, out variableDefinition);
            }
        }
    }
}

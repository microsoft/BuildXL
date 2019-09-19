// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using JetBrains.Annotations;
using TypeScript.Net.Core;
using TypeScript.Net.Diagnostics;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;
using static TypeScript.Net.Binding.ReachabilityExtensions;
using static TypeScript.Net.Extensions.CollectionExtensions;
using static TypeScript.Net.Extensions.NodeArrayExtensions;
using static TypeScript.Net.Types.NodeUtilities;

namespace TypeScript.Net.Binding
{
    internal enum ElementKind
    {
        Property = 1,
        Accessor = 2,
    }

    /// <summary>
    /// Part of the TypeScript compiler responsible for populating symbol tables to assist type-checking.
    /// </summary>
    public sealed class Binder
    {
        // TODO:SQ: Be explicit about string comparer
        private readonly Map<string> m_classifiableNames = new Map<string>();
        private readonly Func</*flags*/ SymbolFlags, /*name*/ string, Symbol> m_symbolCreator = (flags, name) => Symbol.Create(flags, name);
        private INode m_blockScopeContainer;
        private INode m_container;
        private Reachability m_currentReachabilityState;
        private ISourceFile m_file;

        // state used by reachability checks
        private bool m_hasExplicitReturn;

        // TODO:SQ: Consider switching to proper stack type
        private List<int> m_implicitLabels;

        // If this file is an external module, then it is automatically in strict-mode according to
        // ES6.  If it is not an external module, then we'll determine if it is in strict mode or
        // not depending on if we see "use strict" in certain places (or if we hit a class/namespace).
        private bool m_inStrictMode;
        private Map<int> m_labelIndexMap;

        // TODO:SQ: Consider switching to proper stack type
        private List<Reachability> m_labelStack;
        private INode m_lastContainer;
        private ICompilerOptions m_options;
        private INode m_parent;
        private bool m_seenThisKeyword;

        private int m_symbolCount;

        /// <nodoc />
        public static void Bind(ISourceFile sourceFile, ICompilerOptions compilerOptions)
        {
            if (sourceFile.State == SourceFileState.Parsed)
            {
                new Binder().BindSourceFile(sourceFile, compilerOptions);
            }
        }

        /// <nodoc />
        public void BindSourceFile(ISourceFile sourceFile, ICompilerOptions compilerOptions)
        {
            if (sourceFile.State == SourceFileState.Parsed)
            {
                BindSourceFileWorker(sourceFile, compilerOptions);
                sourceFile.State = SourceFileState.Bound;
            }
        }

        /// <nodoc />
        public static ModuleInstanceState GetModuleInstanceState(INode node)
        {
            // A module is uninstantiated if it contains only
            // 1. interface declarations, type alias declarations
            if (node.Kind == SyntaxKind.InterfaceDeclaration ||
                node.Kind == SyntaxKind.TypeAliasDeclaration)
            {
                return ModuleInstanceState.NonInstantiated;
            }

            // 2. const enum declarations
            if (node.IsConstEnumDeclaration())
            {
                return ModuleInstanceState.ConstEnumOnly;
            }

            // 3. non-exported import declarations
            if ((node.Kind == SyntaxKind.ImportDeclaration || node.Kind == SyntaxKind.ImportEqualsDeclaration) &&
                (node.Flags & NodeFlags.Export) == NodeFlags.None)
            {
                return ModuleInstanceState.NonInstantiated;
            }

            // 4. other uninstantiated module declarations.
            if (node.Kind == SyntaxKind.ModuleBlock)
            {
                // Using a separate "context" object to avoid closure allocation in the following method.
                // This is useful because this method is on the hot path of the binding process.
                using (var contextPool = ObjectPools.ModuleInstanceStateContextPool.GetInstance())
                {
                    var context = contextPool.Instance;
                    context.State = ModuleInstanceState.NonInstantiated;
                    NodeWalker.ForEachChild(
                        node,
                        context,
                        (n, ctx) =>
                        {
                            switch (GetModuleInstanceState(n))
                            {
                                case ModuleInstanceState.NonInstantiated:
                                    // child is non-instantiated - continue searching
                                    return false;

                                case ModuleInstanceState.ConstEnumOnly:
                                    // child is var enum only - record state and continue searching
                                    ctx.State = ModuleInstanceState.ConstEnumOnly;
                                    return false;

                                case ModuleInstanceState.Instantiated:
                                    // child is instantiated - record state and stop
                                    ctx.State = ModuleInstanceState.Instantiated;
                                    return true;
                            }

                            return false;
                        });

                    return context.State;
                }
            }

            if (node.Kind == SyntaxKind.ModuleDeclaration)
            {
                return GetModuleInstanceState(node.Cast<IModuleDeclaration>().Body);
            }

            return ModuleInstanceState.Instantiated;
        }

        internal sealed class ModuleInstanceStateContext
        {
            public ModuleInstanceState State;
        }

        private void BindSourceFileWorker(ISourceFile sourceFile, ICompilerOptions compilerOptions)
        {
            m_file = sourceFile;
            m_options = compilerOptions;

            // TODO:SQ: if externalModuleIndicator == null, shouldn't inStrictMode be false?!
            // DScript files should not be considered as a strict mode.
            m_inStrictMode = (m_file.ExternalModuleIndicator != null) && !sourceFile.IsScriptFile();
            m_classifiableNames.Clear();

            if (m_file.Locals == null)
            {
                Bind(m_file);
                m_file.SymbolCount = m_symbolCount;
                m_file.ClassifiableNames = m_classifiableNames;
            }

            // TODO:SQ: Consider moving these to a ResetState method
            m_file = null;
            m_options = null;
            m_parent = null;
            m_container = null;
            m_blockScopeContainer = null;
            m_lastContainer = null;
            m_seenThisKeyword = false;
            m_hasExplicitReturn = false;
            m_labelStack = null;
            m_labelIndexMap = null;
            m_implicitLabels = null;
        }

        private Symbol CreateSymbol(SymbolFlags flags, string name)
        {
            m_symbolCount++;
            return m_symbolCreator(flags, name);
        }

        private static void AddDeclarationToSymbol(ISymbol symbol, IDeclaration node, SymbolFlags symbolFlags)
        {
            symbol.SetDeclaration(symbolFlags, node);
            node.Symbol = symbol;
        }

        // Should not be called on a declaration with a computed property name,
        // unless it is a well known Symbol.
        private static string GetDeclarationName(IDeclaration node)
        {
            var name = node.Name;
            if (name != null)
            {
                if (node.Kind == SyntaxKind.ModuleDeclaration && name.Kind == SyntaxKind.StringLiteral)
                {
                    return I($"\"{name.Cast<ILiteralExpression>().Text}\"");
                }

                if (name.Kind == SyntaxKind.ComputedPropertyName)
                {
                    var nameExpression = name.Cast<IComputedPropertyName>().Expression;

                    // treat computed property names where expression is string/numeric literal as just string/numeric literal
                    if (IsStringOrNumericLiteral(nameExpression.Kind))
                    {
                        return nameExpression.Cast<ILiteralExpression>().Text;
                    }

                    Contract.Assert(IsWellKnownSymbolSyntactically(nameExpression));
                    return GetPropertyNameForKnownSymbolName(nameExpression.Cast<IPropertyAccessExpression>().Name.Text);
                }

                return GetIdentifierOrLiteralExpressionText(name);
            }

            switch (node.Kind)
            {
                case SyntaxKind.Constructor:
                    return "__constructor";

                case SyntaxKind.FunctionType:
                case SyntaxKind.CallSignature:
                    return "__call";

                case SyntaxKind.ConstructorType:
                case SyntaxKind.ConstructSignature:
                    return "__new";

                case SyntaxKind.IndexSignature:
                    return "__index";

                case SyntaxKind.ExportDeclaration:
                    return "__export";

                case SyntaxKind.ExportAssignment:
                    return node.Cast<IExportAssignment>().IsExportEquals.ValueOrDefault ? "export=" : "default";

                case SyntaxKind.BinaryExpression:
                    switch (GetSpecialPropertyAssignmentKind(node))
                    {
                        case SpecialPropertyAssignmentKind.ModuleExports:
                            // module.exports = ...
                            return "export=";

                        case SpecialPropertyAssignmentKind.ExportsProperty:
                        case SpecialPropertyAssignmentKind.ThisProperty:
                            // exports.x = ... or this.y = ...
                            return node.Cast<IBinaryExpression>().Left.Cast<IPropertyAccessExpression>().Name.Text;

                        case SpecialPropertyAssignmentKind.PrototypeProperty:
                            // className.prototype.methodName = ...
                            return
                                node.Cast<IBinaryExpression>()
                                    .Left.Cast<IPropertyAccessExpression>()
                                    .Expression.Cast<IPropertyAccessExpression>()
                                    .Name.Text;
                    }

                    Contract.Assert(false, "Unknown binary declaration kind");
                    break;

                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.ClassDeclaration:
                    return (node.Flags & NodeFlags.Default) != NodeFlags.None ? "default" : null;
            }

            return null;
        }

        private static string GetDisplayName(IDeclaration node)
        {
            return node.Name != null ? DeclarationNameToString(node.Name) : GetDeclarationName(node);
        }

        /// <summary>
        /// Declares a Symbol for the node and adds it to symbols. Reports errors for conflicting identifier names.
        /// </summary>
        /// <param name="symbolTable">The symbol table which node will be added to.</param>
        /// <param name="parent">Node's parent declaration.</param>
        /// <param name="node">The declaration to be added to the symbol table</param>
        /// <param name="includes">The SymbolFlags that node has in addition to its declaration type (export eg, ambient, etc.)</param>
        /// <param name="excludes">The flags which node cannot be declared alongside in a symbol table. Used to report forbidden declarations.</param>
        private ISymbol DeclareSymbol([JetBrains.Annotations.NotNull]ISymbolTable symbolTable, ISymbol parent, IDeclaration node, SymbolFlags includes, SymbolFlags excludes)
        {
            // Disable relatively heavy-weight check in release mode
            Contract.AssertDebug(!HasDynamicName(node));

            var isDefaultExport = (node.Flags & NodeFlags.Default) != NodeFlags.None;

            // The exported symbol for an export default function/class node is always named "default"
            var name = isDefaultExport && parent != null ? "default" : GetDeclarationName(node);

            ISymbol symbol = null;
            if (!string.IsNullOrEmpty(name))
            {
                // Check and see if the symbol table already has a symbol with this name.  If not,
                // create a new symbol with this name and add it to the table.  Note that we don't
                // give the new symbol any flags *yet*.  This ensures that it will not conflict
                // with the 'excludes' flags we pass in.
                //
                // If we do get an existing symbol, see if it conflicts with the new symbol we're
                // creating.  For example, a 'var' symbol and a 'class' symbol will conflict within
                // the same symbol table.  If we have a conflict, report the issue on each
                // declaration we have for this symbol, and then create a new symbol for this
                // declaration.
                //
                // If we created a new symbol, either because we didn't have a symbol with this name
                // in the symbol table, or we conflicted with an existing symbol, then just add this
                // node as the sole declaration of the new symbol.
                //
                // Otherwise, we'll be merging into a compatible existing symbol (for example when
                // you have multiple 'vars' with the same name in the same container).  In this case
                // just add this node into the declarations list of the symbol.
                symbol = symbolTable[name] ?? (symbolTable[name] = CreateSymbol(SymbolFlags.None, name));

                if ((includes & SymbolFlagsHelper.Classifiable()) != SymbolFlags.None)
                {
                    m_classifiableNames[name] = name;
                }

                if ((symbol.Flags & excludes) != SymbolFlags.None)
                {
                    if (node.Name != null)
                    {
                        node.Name.Parent = node;
                    }

                    // Report errors every position with duplicate declaration
                    // Report errors on previous encountered declarations
                    var message = (symbol.Flags & SymbolFlags.BlockScopedVariable) != SymbolFlags.None
                        ? Errors.Cannot_redeclare_block_scoped_variable_0
                        : Errors.Duplicate_identifier_0;

                    foreach (var declaration in symbol.DeclarationList)
                    {
                        if ((declaration.Flags & NodeFlags.Default) != NodeFlags.None)
                        {
                            message = Errors.A_module_cannot_have_multiple_default_exports;

                            // TypeScript implementation of this does not break here, but it makes sense to
                            break;
                        }
                    }

                    foreach (var declaration in symbol.DeclarationList)
                    {
                        m_file.BindDiagnostics.Add(
                            Diagnostic.CreateDiagnosticForNode(
                                declaration.Name?.Cast<INode>() ?? declaration,
                                message,
                                GetDisplayName(declaration)));
                    }

                    m_file.BindDiagnostics.Add(
                        Diagnostic.CreateDiagnosticForNode(
                            node.Name?.Cast<INode>() ?? node,
                            message,
                            GetDisplayName(node)));

                    symbol = CreateSymbol(SymbolFlags.None, name);
                }
            }
            else
            {
                symbol = CreateSymbol(SymbolFlags.None, "__missing");
            }

            AddDeclarationToSymbol(symbol, node, includes);
            symbol.Parent = parent;

            return symbol;
        }

        private ISymbol DeclareModuleMember(IDeclaration node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
        {
            var combinedFlags = GetCombinedNodeFlags(node);
            var hasExportModifier = (combinedFlags & NodeFlags.Export) != NodeFlags.None;

            if ((symbolFlags & SymbolFlags.Alias) != SymbolFlags.None)
            {
                if ((node.Kind == SyntaxKind.ExportSpecifier) ||
                    (node.Kind == SyntaxKind.ImportEqualsDeclaration && hasExportModifier))
                {
                    // DScript-specific. If this is an export specifier, check if there is a @@public decorator and reflect it in
                    // the symbol flags.
                    if (node.Kind == SyntaxKind.ExportSpecifier)
                    {
                        // In two hops we go up from the export specifier to the export declaration
                        var exportFlags = node.Parent.Parent.Cast<IExportDeclaration>().Flags;

                        if ((exportFlags & NodeFlags.ScriptPublic) != NodeFlags.None)
                        {
                            symbolFlags |= SymbolFlags.ScriptPublic;
                        }
                    }

                    return DeclareSymbol(m_container.Symbol.Exports, m_container.Symbol, node, symbolFlags, symbolExcludes);
                }

                return DeclareSymbol(m_container.Locals, null, node, symbolFlags, symbolExcludes);
            }

            // Exported module members are given 2 symbols: A local symbol that is classified with an ExportValue,
            // ExportType, or ExportContainer flag, and an associated export symbol with all the correct flags set
            // on it. There are 2 main reasons:
            //
            //   1. We treat locals and exports of the same name as mutually exclusive within a container.
            //      That means the binder will issue a Duplicate Identifier error if you mix locals and exports
            //      with the same name in the same container.
            //      TODO: Make this a more specific error and decouple it from the exclusion logic.
            //   2. When we checkIdentifier in the checker, we set its resolved symbol to the local symbol,
            //      but return the export symbol (by calling getExportSymbolOfValueSymbolIfExported). That way
            //      when the emitter comes back to it, it knows not to qualify the name if it was found in a containing scope.
            if (hasExportModifier || (m_container.Flags & NodeFlags.ExportContext) != NodeFlags.None)
            {
                var exportKind =
                    ((symbolFlags & SymbolFlags.Value) != SymbolFlags.None ? SymbolFlags.ExportValue : SymbolFlags.None) |
                    ((symbolFlags & SymbolFlags.Type) != SymbolFlags.None ? SymbolFlags.ExportType : SymbolFlags.None) |
                    ((symbolFlags & SymbolFlags.Namespace) != SymbolFlags.None ? SymbolFlags.ExportNamespace : SymbolFlags.None);

                var local = DeclareSymbol(m_container.Locals, null, node, exportKind, symbolExcludes);

                // DScript-specific. If the node is flagged with @@public, we propagate the information to the corresponding symbol export
                // This information is used later by the checker to decide what is
                // the public surface of a DScript implicit-reference module.
                if ((combinedFlags & NodeFlags.ScriptPublic) != NodeFlags.None)
                {
                    symbolFlags |= SymbolFlags.ScriptPublic;
                }

                local.ExportSymbol = DeclareSymbol(m_container.Symbol.Exports, m_container.Symbol, node, symbolFlags, symbolExcludes);

                node.LocalSymbol = local;
                return local;
            }

            return DeclareSymbol(m_container.Locals, null, node, symbolFlags, symbolExcludes);
        }

        // All container nodes are kept on a linked list in declaration order. This list is used by
        // the getLocalNameOfContainer function in the type checker to validate that the local name
        // used for a container is unique.
        private void BindChildren(INode node)
        {
            // Before we recurse into a node's chilren, we first save the existing parent, container
            // and block-container.  Then after we pop out of processing the children, we restore
            // these saved values.
            var saveParent = m_parent;
            var saveContainer = m_container;
            var savedBlockScopeContainer = m_blockScopeContainer;

            // This node will now be set as the parent of all of its children as we recurse into them.
            m_parent = node;

            // Depending on what kind of node this is, we may have to adjust the current container
            // and block-container.   If the current node is a container, then it is automatically
            // considered the current block-container as well.  Also, for containers that we know
            // may contain locals, we proactively initialize the .locals field. We do this because
            // it's highly likely that the .locals will be needed to place some child in (for example,
            // a parameter, or variable declaration).
            //
            // However, we do not proactively create the .locals for block-containers because it's
            // totally normal and common for block-containers to never actually have a block-scoped
            // variable in them.  We don't want to end up allocating an object for every 'block' we
            // run into when most of them won't be necessary.
            //
            // Finally, if this is a block-container, then we clear out any existing .locals object
            // it may contain within it.  This happens in incremental scenarios.  Because we can be
            // reusing a node from a previous compilation, that node may have had 'locals' created
            // for it.  We must clear this so we don't accidently move any stale data forward from
            // a previous compilation.
            var containerFlags = GetContainerFlags(node);
            if ((containerFlags & ContainerFlags.IsContainer) == ContainerFlags.IsContainer)
            {
                m_container = m_blockScopeContainer = node;

                if ((containerFlags & ContainerFlags.HasLocals) != ContainerFlags.None)
                {
                    m_container.Locals = SymbolTable.Create();
                }

                AddToContainerChain(m_container);
            }
            else if ((containerFlags & ContainerFlags.IsBlockScopedContainer) == ContainerFlags.IsBlockScopedContainer)
            {
                m_blockScopeContainer = node;
                m_blockScopeContainer.Locals = null;
            }

            Reachability savedReachabilityState = Reachability.Unintialized;
            List<Reachability> savedLabelStack = null;
            Map<int> savedLabels = null;
            List<int> savedImplicitLabels = null;
            bool savedHasExplicitReturn = false;

            var kind = node.Kind;
            var flags = node.Flags;

            // reset all reachability check related flags on node (for incremental scenarios)
            flags &= ~NodeFlags.ReachabilityCheckFlags;

            if (kind == SyntaxKind.InterfaceDeclaration)
            {
                m_seenThisKeyword = false;
            }

            var saveState = (kind == SyntaxKind.SourceFile) || (kind == SyntaxKind.ModuleBlock) || IsFunctionLikeKind(kind);
            if (saveState)
            {
                savedReachabilityState = m_currentReachabilityState;
                savedLabelStack = m_labelStack;
                savedLabels = m_labelIndexMap;
                savedImplicitLabels = m_implicitLabels;
                savedHasExplicitReturn = m_hasExplicitReturn;

                m_currentReachabilityState = Reachability.Reachable;
                m_hasExplicitReturn = false;
                m_labelStack = null;
                m_labelIndexMap = null;
                m_implicitLabels = null;
            }

            BindReachableStatement(node);

            if (m_currentReachabilityState == Reachability.Reachable &&
                IsFunctionLikeKind(kind) &&
                NodeIsPresent(node.Cast<IFunctionLikeDeclaration>().Body))
            {
                // TODO:SQ: HasImplicitReturn flag affects string output (see DScriptNodeUtilities.ToDisplayString())!
                //          This causes problems for arrow functions during roundtrip parsing
                // flags |= NodeFlags.HasImplicitReturn;
                if (m_hasExplicitReturn)
                {
                    flags |= NodeFlags.HasExplicitReturn;
                }
            }

            if (kind == SyntaxKind.InterfaceDeclaration)
            {
                flags = m_seenThisKeyword ? flags | NodeFlags.ContainsThis : flags & ~NodeFlags.ContainsThis;
            }

            node.Flags = flags;

            if (saveState)
            {
                m_hasExplicitReturn = savedHasExplicitReturn;
                m_currentReachabilityState = savedReachabilityState;
                m_labelStack = savedLabelStack;
                m_labelIndexMap = savedLabels;
                m_implicitLabels = savedImplicitLabels;
            }

            m_container = saveContainer;
            m_parent = saveParent;
            m_blockScopeContainer = savedBlockScopeContainer;
        }

        /// <summary>
        /// Returns true if node and its subnodes were successfully traversed.
        /// Returning false means that node was not examined and caller needs to dive into the node itself.
        /// </summary>
        /// <remarks>
        /// TODO:SQ: This comment is a lie. Fixup/investigate
        /// </remarks>
        private void BindReachableStatement(INode node)
        {
            if (CheckUnreachable(node))
            {
                // TODO:SQ: Need to call bind(n) on each child in the node, but there is no
                //          way to call a void function on the child given implementation of ForEachChild.
                //          Investigate if this ugly solution is correct and good enough.
                NodeWalker.ForEachChild(node, this, (n, @this) =>
                {
                    @this.Bind(n);
                });
                return;
            }

            switch (node.Kind)
            {
                case SyntaxKind.WhileStatement:
                    BindWhileStatement(node.Cast<IWhileStatement>());
                    break;

                case SyntaxKind.DoStatement:
                    BindDoStatement(node.Cast<IDoStatement>());
                    break;

                case SyntaxKind.ForStatement:
                    BindForStatement(node.Cast<IForStatement>());
                    break;

                case SyntaxKind.ForInStatement:
                case SyntaxKind.ForOfStatement:
                    BindForInOrForOfStatement(node.Cast<IIterationStatement>());
                    break;

                case SyntaxKind.IfStatement:
                    BindIfStatement(node.Cast<IIfStatement>());
                    break;

                case SyntaxKind.ReturnStatement:
                case SyntaxKind.ThrowStatement:
                    BindReturnOrThrow(/*IReturnStatement | IThrowStatement*/ node);
                    break;

                case SyntaxKind.BreakStatement:
                case SyntaxKind.ContinueStatement:
                    BindBreakOrContinueStatement(node.Cast<IBreakOrContinueStatement>());
                    break;

                case SyntaxKind.TryStatement:
                    BindTryStatement(node.Cast<ITryStatement>());
                    break;

                case SyntaxKind.SwitchStatement:
                    BindSwitchStatement(node.Cast<ISwitchStatement>());
                    break;

                case SyntaxKind.CaseBlock:
                    BindCaseBlock(node.Cast<ICaseBlock>());
                    break;

                case SyntaxKind.LabeledStatement:
                    BindLabeledStatement(node.Cast<ILabeledStatement>());
                    break;

                default:
                    // TODO:SQ: Need to call bind(n) on each child in the node, but there is no
                    //          way to call a void function on the child given implementation of ForEachChild.
                    //          Investigate if this ugly solution is correct and good enough.
                    NodeWalker.ForEachChild(node, this, (n, @this) =>
                    {
                        @this.Bind(n);
                    });
                    break;
            }
        }

        private void BindWhileStatement(IWhileStatement n)
        {
            var preWhileState =
                n.Expression.Kind == SyntaxKind.FalseKeyword ? Reachability.Unreachable : m_currentReachabilityState;

            var postWhileState =
                n.Expression.Kind == SyntaxKind.TrueKeyword ? Reachability.Unreachable : m_currentReachabilityState;

            // bind expressions (don't affect reachability)
            Bind(n.Expression);

            m_currentReachabilityState = preWhileState;
            var postWhileLabel = PushImplicitLabel();
            Bind(n.Statement);
            PopImplicitLabel(postWhileLabel, postWhileState);
        }

        private void BindDoStatement(IDoStatement n)
        {
            var preDoState = m_currentReachabilityState;

            var postDoLabel = PushImplicitLabel();
            Bind(n.Statement);
            var postDoState = n.Expression.Kind == SyntaxKind.TrueKeyword ? Reachability.Unreachable : preDoState;
            PopImplicitLabel(postDoLabel, postDoState);

            // bind expressions (don't affect reachability)
            Bind(n.Expression);
        }

        private void BindForStatement(IForStatement n)
        {
            var preForState = m_currentReachabilityState;
            var postForLabel = PushImplicitLabel();

            // bind expressions (don't affect reachability)
            Bind(n.Initializer);
            Bind(n.Condition);
            Bind(n.Incrementor);

            Bind(n.Statement);

            // for statement is considered infinite when it condition is either omitted or is true keyword
            // - for(..;;..)
            // - for(..;true;..)
            var isInfiniteLoop = n.Condition == null || n.Condition.Kind == SyntaxKind.TrueKeyword;
            var postForState = isInfiniteLoop ? Reachability.Unreachable : preForState;
            PopImplicitLabel(postForLabel, postForState);
        }

        private void BindForInOrForOfStatement(/*IForInStatement | IForOfStatement*/ IIterationStatement n)
        {
            var preStatementState = m_currentReachabilityState;
            var postStatementLabel = PushImplicitLabel();

            // bind expressions (don't affect reachability)
            Bind(n.Kind == SyntaxKind.ForInStatement
                ? n.Cast<IForInStatement>().Initializer
                : n.Cast<IForOfStatement>().Initializer);
            Bind(n.Kind == SyntaxKind.ForInStatement ? n.Cast<IForInStatement>().Expression : n.Cast<IForOfStatement>().Expression);

            Bind(n.Statement);
            PopImplicitLabel(postStatementLabel, preStatementState);
        }

        private void BindIfStatement(IIfStatement n)
        {
            // denotes reachability state when entering 'thenStatement' part of the if statement:
            // i.e., if condition is false then thenStatement is unreachable
            var ifTrueState = n.Expression.Kind == SyntaxKind.FalseKeyword ? Reachability.Unreachable : m_currentReachabilityState;

            // denotes reachability state when entering 'elseStatement':
            // i.e., if condition is true then elseStatement is unreachable
            var ifFalseState = n.Expression.Kind == SyntaxKind.TrueKeyword ? Reachability.Unreachable : m_currentReachabilityState;

            m_currentReachabilityState = ifTrueState;

            // bind expression (don't affect reachability)
            Bind(n.Expression);
            Bind(n.ThenStatement);

            if (n.ElseStatement)
            {
                var preElseState = m_currentReachabilityState;
                m_currentReachabilityState = ifFalseState;

                Bind(n.ElseStatement.Value);
                m_currentReachabilityState = Or(m_currentReachabilityState, preElseState);
            }
            else
            {
                m_currentReachabilityState = Or(m_currentReachabilityState, ifFalseState);
            }
        }

        private void BindReturnOrThrow(/*IReturnStatement | IThrowStatement*/ INode n)
        {
            // bind expression (don't affect reachability)
            Bind(n.Kind == SyntaxKind.ReturnStatement
                ? n.Cast<IReturnStatement>().Expression
                : n.Cast<IThrowStatement>().Expression);

            if (n.Kind == SyntaxKind.ReturnStatement)
            {
                m_hasExplicitReturn = true;
            }

            m_currentReachabilityState = Reachability.Unreachable;
        }

        private void BindBreakOrContinueStatement(IBreakOrContinueStatement n)
        {
            // call bind on label (don't affect reachability)
            Bind(n.Label);

            // for continue case touch label so it will be marked as used
            var isValidJump = JumpToLabel(
                n.Label,
                n.Kind == SyntaxKind.BreakStatement ? m_currentReachabilityState : Reachability.Unreachable);
            if (isValidJump)
            {
                m_currentReachabilityState = Reachability.Unreachable;
            }
        }

        private void BindTryStatement(ITryStatement n)
        {
            // catch\finally blocks has the same reachability as try block
            var preTryState = m_currentReachabilityState;
            Bind(n.TryBlock);
            var postTryState = m_currentReachabilityState;

            m_currentReachabilityState = preTryState;
            Bind(n.CatchClause);
            var postCatchState = m_currentReachabilityState;

            m_currentReachabilityState = preTryState;
            Bind(n.FinallyBlock);

            // post catch/finally state is reachable if
            // - post try state is reachable - control flow can fall out of try block
            // - post catch state is reachable - control flow can fall out of catch block
            m_currentReachabilityState = Or(postTryState, postCatchState);
        }

        private void BindSwitchStatement(ISwitchStatement n)
        {
            var preSwitchState = m_currentReachabilityState;
            var postSwitchLabel = PushImplicitLabel();

            // bind expression (don't affect reachability)
            Bind(n.Expression);

            Bind(n.CaseBlock);

            var hasDefault = Any(n.CaseBlock.Clauses, c => { return c.Kind == SyntaxKind.DefaultClause; });

            // post switch state is unreachable if switch is exaustive (has a default case ) and does not have fallthrough from the last case
            var postSwitchState = hasDefault && (m_currentReachabilityState != Reachability.Reachable)
                ? Reachability.Unreachable
                : preSwitchState;

            PopImplicitLabel(postSwitchLabel, postSwitchState);
        }

        private void BindCaseBlock(ICaseBlock n)
        {
            var startState = m_currentReachabilityState;

            foreach (var clause in n.Clauses)
            {
                m_currentReachabilityState = startState;
                Bind(clause);

                if ((clause.Statements.Count != 0) &&
                    (m_currentReachabilityState == Reachability.Reachable) &&
                    m_options.NoFallthroughCasesInSwitch.HasValue &&
                    m_options.NoFallthroughCasesInSwitch.Value)
                {
                    ErrorOnFirstToken(clause, Errors.Fallthrough_case_in_switch);
                }
            }
        }

        private void BindLabeledStatement(ILabeledStatement n)
        {
            // call bind on label (don't affect reachability)
            Bind(n.Label);

            var ok = PushNamedLabel(n.Label);
            Bind(n.Statement);
            if (ok)
            {
                PopNamedLabel(n.Label, m_currentReachabilityState);
            }
        }

        private static ContainerFlags GetContainerFlags(INode node)
        {
            switch (node.Kind)
            {
                case SyntaxKind.ClassExpression:
                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.InterfaceDeclaration:
                case SyntaxKind.EnumDeclaration:
                case SyntaxKind.TypeLiteral:
                case SyntaxKind.ObjectLiteralExpression:
                    return ContainerFlags.IsContainer;

                case SyntaxKind.CallSignature:
                case SyntaxKind.ConstructSignature:
                case SyntaxKind.IndexSignature:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.Constructor:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.FunctionType:
                case SyntaxKind.ConstructorType:
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.ArrowFunction:
                case SyntaxKind.ModuleDeclaration:
                case SyntaxKind.SourceFile:
                case SyntaxKind.TypeAliasDeclaration:
                    return ContainerFlags.IsContainerWithLocals;

                case SyntaxKind.CatchClause:
                case SyntaxKind.ForStatement:
                case SyntaxKind.ForInStatement:
                case SyntaxKind.ForOfStatement:
                case SyntaxKind.WhileStatement:
                case SyntaxKind.CaseBlock:
                    return ContainerFlags.IsBlockScopedContainer;

                case SyntaxKind.Block:
                    // do not treat blocks directly inside a function as a block-scoped-container.
                    // Locals that reside in this block should go to the function locals. Othewise 'x'
                    // would not appear to be a redeclaration of a block scoped local in the following
                    // example:
                    //
                    //      funtion foo()
                    //      {
                    //          var x;
                    //          let x;
                    //      }
                    //
                    // If we placed 'var x' into the function locals and 'let x' into the locals of
                    // the block, then there would be no collision.
                    //
                    // By not creating a new block-scoped-container here, we ensure that both 'var x'
                    // and 'let x' go into the Function-container's locals, and we do get a collision
                    // conflict.
                    return IsFunctionLike(node.Parent) != null ? ContainerFlags.None : ContainerFlags.IsBlockScopedContainer;
            }

            return ContainerFlags.None;
        }

        private void AddToContainerChain(INode next)
        {
            if (m_lastContainer != null)
            {
                // NextContainer is not used on DScript.
                // m_lastContainer.NextContainer = Optional.Create(next);
            }

            m_lastContainer = next;
        }

        private void DeclareSymbolAndAddToSymbolTable(IDeclaration node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
        {
            // Just call this directly so that the return type of this function stays "void".
            DeclareSymbolAndAddToSymbolTableWorker(node, symbolFlags, symbolExcludes);
        }

        private ISymbol DeclareSymbolAndAddToSymbolTableWorker(IDeclaration node, SymbolFlags symbolFlags,
            SymbolFlags symbolExcludes)
        {
            switch (m_container.Kind)
            {
                // Modules, source files, and classes need specialized handling for how their
                // members are declared (for example, a member of a class will go into a specific
                // symbol table depending on if it is static or not). We defer to specialized
                // handlers to take care of declaring these child members.
                case SyntaxKind.ModuleDeclaration:
                    return DeclareModuleMember(node, symbolFlags, symbolExcludes);

                case SyntaxKind.SourceFile:
                    return DeclareSourceFileMember(node, symbolFlags, symbolExcludes);

                case SyntaxKind.ClassExpression:
                case SyntaxKind.ClassDeclaration:
                    return DeclareClassMember(node, symbolFlags, symbolExcludes);

                case SyntaxKind.EnumDeclaration:
                    return DeclareSymbol(m_container.Symbol.Exports, m_container.Symbol, node, symbolFlags,
                        symbolExcludes);

                case SyntaxKind.TypeLiteral:
                case SyntaxKind.ObjectLiteralExpression:
                case SyntaxKind.InterfaceDeclaration:
                    // Interface/Object-types always have their children added to the 'members' of
                    // their container. They are only accessible through an instance of their
                    // container, and are never in scope otherwise (even inside the body of the
                    // object / type / interface declaring them). An exception is type parameters,
                    // which are in scope without qualification (similar to 'locals').
                    return DeclareSymbol(m_container.Symbol.Members, m_container.Symbol, node, symbolFlags,
                        symbolExcludes);

                case SyntaxKind.FunctionType:
                case SyntaxKind.ConstructorType:
                case SyntaxKind.CallSignature:
                case SyntaxKind.ConstructSignature:
                case SyntaxKind.IndexSignature:
                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                case SyntaxKind.Constructor:
                case SyntaxKind.GetAccessor:
                case SyntaxKind.SetAccessor:
                case SyntaxKind.FunctionDeclaration:
                case SyntaxKind.FunctionExpression:
                case SyntaxKind.ArrowFunction:
                case SyntaxKind.TypeAliasDeclaration:
                    // All the children of these container types are never visible through another
                    // symbol (i.e., through another symbol's 'exports' or 'members').  Instead,
                    // they're only accessed 'lexically' (i.e., from code that exists underneath
                    // their container in the tree.  To accomplish this, we simply add their declared
                    // symbol to the 'locals' of the container.  These symbols can then be found as
                    // the type checker walks up the containers, checking them for matching names.
                    return DeclareSymbol(m_container.Locals, null, node, symbolFlags, symbolExcludes);

                default:
                    Contract.Assert(false, "unexpected container kind");
                    return null;
            }
        }

        private ISymbol DeclareClassMember(IDeclaration node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
        {
            return (node.Flags & NodeFlags.Static) != NodeFlags.None
                ? DeclareSymbol(m_container.Symbol.Exports, m_container.Symbol, node, symbolFlags,
                    symbolExcludes)
                : DeclareSymbol(m_container.Symbol.Members, m_container.Symbol, node, symbolFlags,
                    symbolExcludes);
        }

        private ISymbol DeclareSourceFileMember(IDeclaration node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
        {
            return SourceFileExtensions.IsExternalModule(m_file)
                ? DeclareModuleMember(node, symbolFlags, symbolExcludes)
                : DeclareSymbol(m_file.Locals, null, node, symbolFlags, symbolExcludes);
        }

        private static bool HasExportDeclarations(/*IModuleDeclaration | ISourceFile*/ INode node)
        {
            INode body = node.Kind == SyntaxKind.SourceFile ? node : node.Cast<IModuleDeclaration>().Body;

            if (body.Kind == SyntaxKind.SourceFile || body.Kind == SyntaxKind.ModuleBlock)
            {
                NodeArray<IStatement> statements = body.Kind == SyntaxKind.SourceFile
                    ? body.Cast<ISourceFile>().Statements
                    : body.Cast<IModuleBlock>().Statements;

                foreach (var stat in statements)
                {
                    if (stat.Kind == SyntaxKind.ExportDeclaration || stat.Kind == SyntaxKind.ExportAssignment)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static void SetExportContextFlag(/*IModuleDeclaration | ISourceFile*/ INode node)
        {
            // A declaration source file or ambient module declaration that contains no export declarations (but possibly regular
            // declarations with export modifiers) is an export context in which declarations are implicitly exported.
            if (IsInAmbientContext(node) && !HasExportDeclarations(node))
            {
                node.Flags |= NodeFlags.ExportContext;
            }
            else
            {
                node.Flags &= ~NodeFlags.ExportContext;
            }
        }

        private void BindModuleDeclaration(IModuleDeclaration node)
        {
            SetExportContextFlag(node);

            if (node.Name.Kind == SyntaxKind.StringLiteral)
            {
                DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.ValueModule, SymbolFlags.ValueModuleExcludes);
            }
            else
            {
                var state = GetModuleInstanceState(node);
                if (state == ModuleInstanceState.NonInstantiated)
                {
                    DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.NamespaceModule, SymbolFlags.NamespaceModuleExcludes);
                }
                else
                {
                    DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.ValueModule, SymbolFlags.ValueModuleExcludes);
                    if ((node.Symbol.Flags & (SymbolFlags.Function | SymbolFlags.Class | SymbolFlags.RegularEnum)) !=
                        SymbolFlags.None)
                    {
                        // if module was already merged with some function, class or non-const enum
                        // treat is a non-const-enum-only
                        // Const enums are not supported.
                        // node.Symbol.ConstEnumOnlyModule = false;
                    }
                    else
                    {
                        // Const enum modules are not supported.
                        // var currentModuleIsConstEnumOnly = state == ModuleInstanceState.ConstEnumOnly;
                        // if (!node.Symbol.ConstEnumOnlyModule.HasValue)
                        // {
                        //    // non-merged case - use the current state
                        //    node.Symbol.ConstEnumOnlyModule = currentModuleIsConstEnumOnly;
                        // }
                        // else
                        // {
                        //    // merged module case is const enum only if all its pieces are non-instantiated or const enum
                        //    node.Symbol.ConstEnumOnlyModule = node.Symbol.ConstEnumOnlyModule.Value &&
                        //                                            currentModuleIsConstEnumOnly;
                        // }
                    }
                }
            }
        }

        private void BindFunctionOrConstructorType(ISignatureDeclaration node)
        {
            // For a given function symbol "<...>(...) => T" we want to generate a symbol identical
            // to the one we would get for: { <...>(...): T }
            //
            // We do that by making an anonymous type literal symbol, and then setting the function
            // symbol as its sole member. To the rest of the system, this symbol will be  indistinguishable
            // from an actual type literal symbol you would have gotten had you used the long form.
            var symbol = CreateSymbol(SymbolFlags.Signature, GetDeclarationName(node));
            AddDeclarationToSymbol(symbol, node, SymbolFlags.Signature);

            var typeLiteralSymbol = CreateSymbol(SymbolFlags.TypeLiteral, "__type");
            AddDeclarationToSymbol(typeLiteralSymbol, node, SymbolFlags.TypeLiteral);

            typeLiteralSymbol.GetMembers()[symbol.Name] = symbol;
        }

        private void BindObjectLiteralExpression(IObjectLiteralExpression node)
        {
            if (m_inStrictMode)
            {
                Map<ElementKind> seen = new Map<ElementKind>();

                foreach (var prop in node.Properties)
                {
                    if (prop.Name.Kind != SyntaxKind.Identifier)
                    {
                        continue;
                    }

                    var identifier = prop.Name.Cast<IIdentifier>();

                    // ECMA-262 11.1.5 Object Initialiser
                    // If previous is not undefined then throw a SyntaxError exception if any of the following conditions are true
                    // a.This production is contained in strict code and IsDataDescriptor(previous) is true and
                    // IsDataDescriptor(propId.descriptor) is true.
                    //    b.IsDataDescriptor(previous) is true and IsAccessorDescriptor(propId.descriptor) is true.
                    //    c.IsAccessorDescriptor(previous) is true and IsDataDescriptor(propId.descriptor) is true.
                    //    d.IsAccessorDescriptor(previous) is true and IsAccessorDescriptor(propId.descriptor) is true
                    // and either both previous and propId.descriptor have[[Get]] fields or both previous and propId.descriptor have[[Set]] fields
                    var currentKind = (prop.Kind == SyntaxKind.PropertyAssignment) ||
                                      (prop.Kind == SyntaxKind.ShorthandPropertyAssignment) ||
                                      (prop.Kind == SyntaxKind.MethodDeclaration)
                        ? ElementKind.Property
                        : ElementKind.Accessor;

                    ElementKind existingKind;
                    if (!seen.TryGetValue(identifier.Text, out existingKind))
                    {
                        seen[identifier.Text] = currentKind;
                        continue;
                    }

                    if (currentKind == ElementKind.Property && existingKind == ElementKind.Property)
                    {
                        var span = DiagnosticUtilities.GetErrorSpanForNode(m_file, identifier);
                        m_file.BindDiagnostics.Add(
                            Diagnostic.CreateFileDiagnostic(m_file, span.Start, span.Length,
                                Errors.An_object_literal_cannot_have_multiple_properties_with_the_same_name_in_strict_mode));
                    }
                }
            }

            // TODO:SQ - return Unit type
            BindAnonymousDeclaration(node, SymbolFlags.ObjectLiteral, "__object");
        }

        private void BindAnonymousDeclaration(IDeclaration node, SymbolFlags symbolFlags, string name)
        {
            var symbol = CreateSymbol(symbolFlags, name);
            AddDeclarationToSymbol(symbol, node, symbolFlags);
        }

        private void BindBlockScopedDeclaration(IDeclaration node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
        {
            switch (m_blockScopeContainer.Kind)
            {
                case SyntaxKind.ModuleDeclaration:
                    DeclareModuleMember(node, symbolFlags, symbolExcludes);
                    return;

                case SyntaxKind.SourceFile:
                    if (SourceFileExtensions.IsExternalModule(m_container.Cast<ISourceFile>()))
                    {
                        DeclareModuleMember(node, symbolFlags, symbolExcludes);
                        return;
                    }

                    break;
            }

            if (m_blockScopeContainer.Locals == null)
            {
                m_blockScopeContainer.Locals = SymbolTable.Create();
                AddToContainerChain(m_blockScopeContainer);
            }

            DeclareSymbol(m_blockScopeContainer.Locals, null, node, symbolFlags, symbolExcludes);
        }

        private void BindBlockScopedVariableDeclaration(IDeclaration node)
        {
            BindBlockScopedDeclaration(node, SymbolFlags.BlockScopedVariable, SymbolFlags.BlockScopedVariableExcludes);
        }

        // The binder visits every node in the syntax tree so it is a convenient place to perform a single localized
        // check for reserved words used as identifiers in strict mode code.
        private void CheckStrictModeIdentifier(IIdentifier node)
        {
            if (m_inStrictMode &&
                node.OriginalKeywordKind >= SyntaxKind.FirstFutureReservedWord &&
                node.OriginalKeywordKind <= SyntaxKind.LastFutureReservedWord &&
                !IsIdentifierName(node))
            {
                // Report error only if there are no parse errors in file
                if (m_file.ParseDiagnostics.Count == 0)
                {
                    m_file.BindDiagnostics.Add(
                        Diagnostic.CreateDiagnosticForNode(node, GetStrictModeIdentifierMessage(node),
                            DeclarationNameToString(node)));
                }
            }
        }

        private IDiagnosticMessage GetStrictModeIdentifierMessage(INode node)
        {
            // Provide specialized messages to help the user understand why we think they're in
            // strict mode.
            if (GetContainingClass(node) != null)
            {
                return
                    Errors
                        .Identifier_expected_0_is_a_reserved_word_in_strict_mode_Class_definitions_are_automatically_in_strict_mode;
            }

            if (m_file.ExternalModuleIndicator != null)
            {
                return Errors.Identifier_expected_0_is_a_reserved_word_in_strict_mode_Modules_are_automatically_in_strict_mode;
            }

            return Errors.Identifier_expected_0_is_a_reserved_word_in_strict_mode;
        }

        private void CheckStrictModeBinaryExpression(IBinaryExpression node)
        {
            if (m_inStrictMode && IsLeftHandSideExpression(node.Left) &&
                node.OperatorToken.Kind.IsAssignmentOperator())
            {
                // ECMA 262 (Annex C) The identifier eval or arguments may not appear as the LeftHandSideExpression of an
                // Assignment operator(11.13) or of a PostfixExpression(11.3)
                CheckStrictModeEvalOrArguments(node, node.Left);
            }
        }

        private void CheckStrictModeCatchClause(ICatchClause node)
        {
            // It is a SyntaxError if a TryStatement with a Catch occurs within strict code and the Identifier of the
            // Catch production is eval or arguments
            if (m_inStrictMode && node.VariableDeclaration != null)
            {
                CheckStrictModeEvalOrArguments(node, node.VariableDeclaration.Name);
            }
        }

        private void CheckStrictModeDeleteExpression(IDeleteExpression node)
        {
            // Grammar checking
            if (m_inStrictMode && (node.Expression.Kind == SyntaxKind.Identifier))
            {
                // When a delete operator occurs within strict mode code, a SyntaxError is thrown if its
                // UnaryExpression is a direct reference to a variable, function argument, or function name
                var span = DiagnosticUtilities.GetErrorSpanForNode(m_file, node.Expression);
                m_file.BindDiagnostics.Add(
                    Diagnostic.CreateFileDiagnostic(m_file, span.Start, span.Length,
                        Errors.Delete_cannot_be_called_on_an_identifier_in_strict_mode));
            }
        }

        private static bool IsEvalOrArgumentsIdentifier(INode node)
        {
            return (node.Kind == SyntaxKind.Identifier) &&
                   (node.Cast<IIdentifier>().Text?.Equals("eval") == true ||
                    node.Cast<IIdentifier>().Text?.Equals("arguments") == true);
        }

        private void CheckStrictModeEvalOrArguments(INode contextNode, INode name)
        {
            if (name?.Kind == SyntaxKind.Identifier)
            {
                var identifier = name.Cast<IIdentifier>();

                if (IsEvalOrArgumentsIdentifier(identifier))
                {
                    // We check first if the name is inside class declaration or class expression; if so give explicit message
                    // otherwise report generic error message.
                    var span = DiagnosticUtilities.GetErrorSpanForNode(m_file, name);
                    m_file.BindDiagnostics.Add(
                        Diagnostic.CreateFileDiagnostic(m_file, span.Start, span.Length,
                            GetStrictModeEvalOrArgumentsMessage(contextNode), identifier.Text));
                }
            }
        }

        private IDiagnosticMessage GetStrictModeEvalOrArgumentsMessage(INode node)
        {
            // Provide specialized messages to help the user understand why we think they're in
            // strict mode.
            if (GetContainingClass(node) != null)
            {
                return Errors.Invalid_use_of_0_Class_definitions_are_automatically_in_strict_mode;
            }

            if (m_file.ExternalModuleIndicator != null)
            {
                return Errors.Invalid_use_of_0_Modules_are_automatically_in_strict_mode;
            }

            return Errors.Invalid_use_of_0_in_strict_mode;
        }

        private void CheckStrictModeFunctionName(/*IFunctionLikeDeclaration*/ ISignatureDeclaration node)
        {
            if (m_inStrictMode)
            {
                // It is a SyntaxError if the identifier eval or arguments appears within a FormalParameterList of a strict mode FunctionDeclaration or FunctionExpression (13.1))
                CheckStrictModeEvalOrArguments(node, node.Name);
            }
        }

        private void CheckStrictModeNumericLiteral(ILiteralExpression node)
        {
            if (m_inStrictMode && (node.Flags & NodeFlags.OctalLiteral) != NodeFlags.None)
            {
                m_file.BindDiagnostics.Add(
                    Diagnostic.CreateDiagnosticForNode(node, Errors.Octal_literals_are_not_allowed_in_strict_mode));
            }
        }

        private void CheckStrictModePostfixUnaryExpression(IPostfixUnaryExpression node)
        {
            // Grammar checking
            // The identifier eval or arguments may not appear as the LeftHandSideExpression of an
            // Assignment operator(11.13) or of a PostfixExpression(11.3) or as the UnaryExpression
            // operated upon by a Prefix Increment(11.4.4) or a Prefix Decrement(11.4.5) operator.
            if (m_inStrictMode)
            {
                CheckStrictModeEvalOrArguments(node, node.Operand);
            }
        }

        private void CheckStrictModePrefixUnaryExpression(IPrefixUnaryExpression node)
        {
            // Grammar checking
            if (m_inStrictMode)
            {
                if (node.Operator == SyntaxKind.PlusPlusToken || node.Operator == SyntaxKind.MinusMinusToken)
                {
                    CheckStrictModeEvalOrArguments(node, node.Operand);
                }
            }
        }

        private void CheckStrictModeWithStatement(IWithStatement node)
        {
            // Grammar checking for withStatement
            if (m_inStrictMode)
            {
                ErrorOnFirstToken(node, Errors.With_statements_are_not_allowed_in_strict_mode);
            }
        }

        private void ErrorOnFirstToken(INode node, IDiagnosticMessage message, params object[] args)
        {
            var span = DiagnosticUtilities.GetSpanOfTokenAtPosition(m_file, node.Pos);

            m_file.BindDiagnostics.Add(
                Diagnostic.CreateFileDiagnostic(m_file, span.Start, span.Length, message, args));
        }

        // TODO:SQ: Type should be IParameterDeclaration, not IDeclaration
        private static string GetDestructuringParameterName(IParameterDeclaration node)
        {
            // TODO:SQ: This will not work! IParameterDeclaration.Equals is not implemented
            return "__" + IndexOf(node.Parent.Cast<ISignatureDeclaration>().Parameters, node);
        }

        private void Bind(INode node)
        {
            if (node == null)
            {
                return;
            }

            node.Parent = m_parent;

            var savedInStrictMode = m_inStrictMode;
            if (!savedInStrictMode)
            {
                UpdateStrictMode(node);
            }

            // First we bind declaration nodes to a symbol if possible.  We'll both create a symbol
            // and then potentially add the symbol to an appropriate symbol table. Possible
            // destination symbol tables are:
            //
            //  1) The 'exports' table of the current container's symbol.
            //  2) The 'members' table of the current container's symbol.
            //  3) The 'locals' table of the current container.
            //
            // However, not all symbols will end up in any of these tables.  'Anonymous' symbols
            // (like TypeLiterals for example) will not be put in any table.
            BindWorker(node);

            // Then we recurse into the children of the node to bind them as well.  For certain
            // symbols we do specialized work when we recurse.  For example, we'll keep track of
            // the current 'container' node when it changes.  This helps us know which symbol table
            // a local should go into for example.
            BindChildren(node);

            m_inStrictMode = savedInStrictMode;
        }

        private void UpdateStrictMode(INode node)
        {
            switch (node.Kind)
            {
                case SyntaxKind.SourceFile:
                case SyntaxKind.ModuleBlock:
                    UpdateStrictModeStatementList(GetSourceFileOrModuleBlockStatements(node));
                    return;

                case SyntaxKind.Block:
                    if (IsFunctionLike(node.Parent) != null)
                    {
                        UpdateStrictModeStatementList(node.Cast<IBlock>().Statements);
                    }

                    return;

                case SyntaxKind.ClassDeclaration:
                case SyntaxKind.ClassExpression:
                    // All classes are automatically in strict mode in ES6.
                    m_inStrictMode = true;
                    return;
            }
        }

        private static NodeArray<IStatement> GetSourceFileOrModuleBlockStatements(INode node)
        {
            // TODO: this is not a typescript way! Maybe we can extract common interface for ISourceFile and IModuleBlock (that will hold Statements)!
            // TODO:SQ: Use node.Kind?
            var sourceFile = node.As<ISourceFile>();
            if (sourceFile != null)
            {
                return sourceFile.Statements;
            }

            var moduleBlock = node.Cast<IModuleBlock>();
            return moduleBlock.Statements;
        }

        private static string GetIdentifierOrLiteralExpressionText(DeclarationName name)
        {
            // TODO:SQ: Use name.Kind?
            var identifier = name.As<IIdentifier>();
            if (identifier != null)
            {
                return identifier.Text;
            }

            var literalExpression = name.Cast<ILiteralExpression>();
            return literalExpression.Text;
        }

        private static Optional<INode> GetPropertyDeclarationOrPropertySignatureQuestionToken(INode node)
        {
            return node.Kind == SyntaxKind.PropertyDeclaration
                ? node.Cast<IPropertyDeclaration>().QuestionToken
                : node.Cast<IPropertySignature>().QuestionToken;
        }

        private static Optional<INode> GetMethodDeclarationOrMethodSignatureQuestionToken(INode node)
        {
            return node.Kind == SyntaxKind.MethodDeclaration
                ? node.Cast<IMethodDeclaration>().QuestionToken
                : node.Cast<IMethodSignature>().Cast<ITypeElement>().QuestionToken;
        }

        private void UpdateStrictModeStatementList(NodeArray<IStatement> statements)
        {
            foreach (var statement in statements)
            {
                if (!IsPrologueDirective(statement))
                {
                    return;
                }

                if (IsUseStrictPrologueDirective(statement.Cast<IExpressionStatement>()))
                {
                    m_inStrictMode = true;
                    return;
                }
            }
        }

        // Should be called only on prologue directives (DScriptNodeUtilities.isPrologueDirective(node) should be true)
        private static bool IsUseStrictPrologueDirective(IExpressionStatement node)
        {
            if (node.Expression.As<IStringLiteral>() == null)
            {
                return false;
            }

            // In DScript there is no distinction between strict and non-strict mode.
            var nodeText = node.Expression.ToDisplayString();

            // Note: the node text must be exactly "use strict" or 'use strict'.  It is not ok for the
            // string to contain unicode escapes (as per ES5).
            return nodeText.Equals("\"use strict\"") ||
                   nodeText.Equals("'use strict'");
        }

        // TODO:SQ: Return Unit type. i.e., some no-op type that represents void.
        //          In TypeScript it is possible to return void, so to keep implementation
        //          consistent with binder.ts, need to return Unit type.
        private void BindWorker(INode node)
        {
            switch (node.Kind)
            {
                /* Strict mode checks */
                case SyntaxKind.Identifier:
                    CheckStrictModeIdentifier(node.Cast<IIdentifier>());
                    return;

                case SyntaxKind.BinaryExpression:
                    if (node.IsJavaScriptFile())
                    {
                        var specialKind = GetSpecialPropertyAssignmentKind(node);
                        switch (specialKind)
                        {
                            case SpecialPropertyAssignmentKind.ExportsProperty:
                                BindExportsPropertyAssignment(node.Cast<IBinaryExpression>());
                                break;

                            case SpecialPropertyAssignmentKind.ModuleExports:
                                BindModuleExportsAssignment(node.Cast<IBinaryExpression>());
                                break;

                            case SpecialPropertyAssignmentKind.PrototypeProperty:
                                BindPrototypePropertyAssignment(node.Cast<IBinaryExpression>());
                                break;

                            case SpecialPropertyAssignmentKind.ThisProperty:
                                BindThisPropertyAssignment(node.Cast<IBinaryExpression>());
                                break;

                            case SpecialPropertyAssignmentKind.None:
                                // Nothing to do
                                break;

                            default:
                                Contract.Assert(false, "Unknown special property assignment kind");
                                break;
                        }
                    }

                    CheckStrictModeBinaryExpression(node.Cast<IBinaryExpression>());
                    return;

                case SyntaxKind.CatchClause:
                    CheckStrictModeCatchClause(node.Cast<ICatchClause>());
                    return;

                case SyntaxKind.DeleteExpression:
                    CheckStrictModeDeleteExpression(node.Cast<IDeleteExpression>());
                    return;

                case SyntaxKind.NumericLiteral:
                    CheckStrictModeNumericLiteral(node.Cast<ILiteralExpression>());
                    return;

                case SyntaxKind.PostfixUnaryExpression:
                    CheckStrictModePostfixUnaryExpression(node.Cast<IPostfixUnaryExpression>());
                    return;

                case SyntaxKind.PrefixUnaryExpression:
                    CheckStrictModePrefixUnaryExpression(node.Cast<IPrefixUnaryExpression>());
                    return;

                case SyntaxKind.WithStatement:
                    CheckStrictModeWithStatement(node.Cast<IWithStatement>());
                    return;

                case SyntaxKind.ThisType:
                    m_seenThisKeyword = true;
                    return;

                case SyntaxKind.TypePredicate:
                    CheckTypePredicate(node.Cast<ITypePredicateNode>());
                    return;

                case SyntaxKind.TypeParameter:
                    DeclareSymbolAndAddToSymbolTable(node.Cast<IDeclaration>(), SymbolFlags.TypeParameter,
                        SymbolFlags.TypeParameterExcludes);
                    return;

                case SyntaxKind.Parameter:
                    BindParameter(node.Cast<IParameterDeclaration>());
                    return;

                case SyntaxKind.VariableDeclaration:
                case SyntaxKind.BindingElement:
                    BindVariableDeclarationOrBindingElement(/*<VariableDeclaration | BindingElement>*/ node.Cast<IDeclaration>());
                    return;

                case SyntaxKind.PropertyDeclaration:
                case SyntaxKind.PropertySignature:
                    BindPropertyOrMethodOrAccessor(
                        node.Cast<IDeclaration>(),
                        SymbolFlags.Property |
                        (GetPropertyDeclarationOrPropertySignatureQuestionToken(node) ? SymbolFlags.Optional : SymbolFlags.None),
                        SymbolFlags.PropertyExcludes);
                    return;

                case SyntaxKind.PropertyAssignment:
                case SyntaxKind.ShorthandPropertyAssignment:
                    BindPropertyOrMethodOrAccessor(node.Cast<IDeclaration>(), SymbolFlags.Property, SymbolFlags.PropertyExcludes);
                    return;

                case SyntaxKind.EnumMember:
                    BindPropertyOrMethodOrAccessor(node.Cast<IDeclaration>(), SymbolFlags.EnumMember,
                        SymbolFlags.EnumMemberExcludes);
                    return;

                case SyntaxKind.CallSignature:
                case SyntaxKind.ConstructSignature:
                case SyntaxKind.IndexSignature:
                    DeclareSymbolAndAddToSymbolTable(node.Cast<IDeclaration>(), SymbolFlags.Signature, SymbolFlags.None);
                    return;

                case SyntaxKind.MethodDeclaration:
                case SyntaxKind.MethodSignature:
                    // If this is an ObjectLiteralExpression method, then it sits in the same space
                    // as other properties in the object literal.  So we use SymbolFlags.PropertyExcludes
                    // so that it will conflict with any other object literal members with the same
                    // name.
                    BindPropertyOrMethodOrAccessor(
                        node.Cast<IDeclaration>(),
                        SymbolFlags.Method |
                        (GetMethodDeclarationOrMethodSignatureQuestionToken(node) ? SymbolFlags.Optional : SymbolFlags.None),
                        IsObjectLiteralMethod(node) != null ? SymbolFlags.PropertyExcludes : SymbolFlags.MethodExcludes);
                    return;

                case SyntaxKind.FunctionDeclaration:
                    CheckStrictModeFunctionName(node.Cast<IFunctionDeclaration>());
                    DeclareSymbolAndAddToSymbolTable(node.Cast<IDeclaration>(), SymbolFlags.Function, SymbolFlags.FunctionExcludes);
                    return;

                case SyntaxKind.Constructor:
                    DeclareSymbolAndAddToSymbolTable(node.Cast<IDeclaration>(), SymbolFlags.Constructor, SymbolFlags.None);
                    return;

                case SyntaxKind.GetAccessor:
                    BindPropertyOrMethodOrAccessor(node.Cast<IDeclaration>(), SymbolFlags.GetAccessor,
                        SymbolFlags.GetAccessorExcludes);
                    return;

                case SyntaxKind.SetAccessor:
                    BindPropertyOrMethodOrAccessor(node.Cast<IDeclaration>(), SymbolFlags.SetAccessor,
                        SymbolFlags.SetAccessorExcludes);
                    return;

                case SyntaxKind.FunctionType:
                case SyntaxKind.ConstructorType:
                    BindFunctionOrConstructorType(node.Cast<ISignatureDeclaration>());
                    return;

                case SyntaxKind.TypeLiteral:
                    BindAnonymousDeclaration(node.Cast<ITypeLiteralNode>(), SymbolFlags.TypeLiteral, "__type");
                    return;

                case SyntaxKind.ObjectLiteralExpression:
                    BindObjectLiteralExpression(node.Cast<IObjectLiteralExpression>());
                    return;

                case SyntaxKind.FunctionExpression:
                case SyntaxKind.ArrowFunction:
                    CheckStrictModeFunctionName(node.Cast<ISignatureDeclaration>());

                    // TODO:SQ: Find a better way to represent this!
                    // const bindingName = (<FunctionExpression>node).name ? (<FunctionExpression>node).name.text : "__function";
                    // Problem is that ArrowFunction will never have the name field, so it will always default to "__function"
                    string bindingName = node.Kind == SyntaxKind.FunctionExpression
                        ? node.Cast<IFunctionExpression>().Name?.Text
                        : "__function";
                    bindingName = bindingName ?? "__function";

                    BindAnonymousDeclaration(node.Cast<IDeclaration>(), SymbolFlags.Function, bindingName);
                    return;

                case SyntaxKind.CallExpression:
                    if (node.IsJavaScriptFile())
                    {
                        BindCallExpression(node.Cast<ICallExpression>());
                    }

                    break;

                // Members of classes, interfaces, and modules
                case SyntaxKind.ClassExpression:
                case SyntaxKind.ClassDeclaration:
                    BindClassLikeDeclaration(node.Cast<IClassLikeDeclaration>());
                    return;

                case SyntaxKind.InterfaceDeclaration:
                    BindBlockScopedDeclaration(node.Cast<IDeclaration>(), SymbolFlags.Interface, SymbolFlags.InterfaceExcludes);
                    return;

                case SyntaxKind.TypeAliasDeclaration:
                    BindBlockScopedDeclaration(node.Cast<IDeclaration>(), SymbolFlags.TypeAlias, SymbolFlags.TypeAliasExcludes);
                    return;

                case SyntaxKind.EnumDeclaration:
                    BindEnumDeclaration(node.Cast<IEnumDeclaration>());
                    return;

                case SyntaxKind.ModuleDeclaration:
                    BindModuleDeclaration(node.Cast<IModuleDeclaration>());
                    return;

                // Imports and exports
                case SyntaxKind.ImportEqualsDeclaration:
                case SyntaxKind.NamespaceImport:
                case SyntaxKind.ImportSpecifier:
                case SyntaxKind.ExportSpecifier:
                    DeclareSymbolAndAddToSymbolTable(node.Cast<IDeclaration>(), SymbolFlags.Alias, SymbolFlags.AliasExcludes);
                    return;

                case SyntaxKind.ImportClause:
                    BindImportClause(node.Cast<IImportClause>());
                    return;

                case SyntaxKind.ExportDeclaration:
                    BindExportDeclaration(node.Cast<IExportDeclaration>());
                    return;

                case SyntaxKind.ExportAssignment:
                    BindExportAssignment(node.Cast<IExportAssignment>());
                    return;

                case SyntaxKind.SourceFile:
                    BindSourceFileIfExternalModule();
                    return;
            }
        }

        private void CheckTypePredicate(ITypePredicateNode node)
        {
            if (node.ParameterName?.Kind == SyntaxKind.Identifier)
            {
                CheckStrictModeIdentifier(node.ParameterName.Cast<IIdentifier>());
            }

            if (node.ParameterName?.Kind == SyntaxKind.ThisType)
            {
                m_seenThisKeyword = true;
            }

            Bind(node.Type);
        }

        private void BindSourceFileIfExternalModule()
        {
            SetExportContextFlag(m_file);

            if (SourceFileExtensions.IsExternalModule(m_file))
            {
                BindSourceFileAsExternalModule();
            }
        }

        private void BindSourceFileAsExternalModule()
        {
            BindAnonymousDeclaration(m_file, SymbolFlags.ValueModule, string.Concat("\"", Path.RemoveFileExtension(m_file.FileName), "\""));
        }

        // TODO:SQ - Figure out type union for input param, if necessary
        private void BindExportAssignment(/*IExportAssignment | IBinaryExpression*/ IDeclaration node)
        {
            var boundExpression = node.Kind == SyntaxKind.ExportAssignment
                ? node.Cast<IExportAssignment>().Expression
                : node.Cast<IBinaryExpression>().Right;

            if (m_container.Symbol == null || m_container.Symbol.Exports == null)
            {
                // Export assignment in some sort of block construct
                BindAnonymousDeclaration(node, SymbolFlags.Alias, GetDeclarationName(node));
            }
            else if (boundExpression.Kind == SyntaxKind.Identifier)
            {
                // An export default clause with an identifier exports all meanings of that identifier
                DeclareSymbol(m_container.Symbol.Exports, m_container.Symbol, node, SymbolFlags.Alias,
                    SymbolFlags.PropertyExcludes | SymbolFlags.AliasExcludes);
            }
            else
            {
                // An export default clause with an expression exports a value
                DeclareSymbol(m_container.Symbol.Exports, m_container.Symbol, node, SymbolFlags.Property,
                    SymbolFlags.PropertyExcludes | SymbolFlags.AliasExcludes);
            }
        }

        private void BindExportDeclaration(IExportDeclaration node)
        {
            // DScript-specific.
            // An export * may have a @@public decorator, so we reflect that in the symbol flags.
            var flags = SymbolFlags.ExportStar;
            if ((node.Flags & NodeFlags.ScriptPublic) != NodeFlags.None)
            {
                flags |= SymbolFlags.ScriptPublic;
            }

            if (m_container.Symbol == null || m_container.Symbol.Exports == null)
            {
                // Export * in some sort of block construct
                BindAnonymousDeclaration(node, flags, GetDeclarationName(node));
            }
            else if (node.ExportClause == null)
            {
                // All export * declarations are collected in an __export symbol
                DeclareSymbol(m_container.Symbol.Exports, m_container.Symbol, node, flags,
                    SymbolFlags.None);
            }
        }

        private void BindImportClause(IImportClause node)
        {
            // TODO:SQ: IImportClause.Name should be Optional<> value?
            if (node.Name != null)
            {
                DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.Alias, SymbolFlags.AliasExcludes);
            }
        }

        private void SetCommonJsModuleIndicator(INode node)
        {
            if (m_file.CommonJsModuleIndicator == null)
            {
                m_file.CommonJsModuleIndicator = node;
                BindSourceFileAsExternalModule();
            }
        }

        private void BindExportsPropertyAssignment(IBinaryExpression node)
        {
            // When we create a property via 'exports.foo = bar', the 'exports.foo' property access
            // expression is the declaration
            SetCommonJsModuleIndicator(node);
            DeclareSymbol(m_file.Symbol.Exports, m_file.Symbol, node.Left.Cast<IPropertyAccessExpression>(),
                SymbolFlags.Property | SymbolFlagsHelper.Export(), SymbolFlags.None);
        }

        private void BindModuleExportsAssignment(IBinaryExpression node)
        {
            // 'module.exports = expr' assignment
            SetCommonJsModuleIndicator(node);
            BindExportAssignment(node);
        }

        private void BindThisPropertyAssignment(IBinaryExpression node)
        {
            // Declare a 'member' in case it turns out the container was an ES5 class
            if ((m_container.Kind == SyntaxKind.FunctionExpression) ||
                (m_container.Kind == SyntaxKind.FunctionDeclaration))
            {
                DeclareSymbol(m_container.Symbol.GetMembers(), m_container.Symbol, node, SymbolFlags.Property,
                    SymbolFlags.PropertyExcludes);
            }
        }

        private void BindPrototypePropertyAssignment(IBinaryExpression node)
        {
            // We saw a node of the form 'x.prototype.y = z'. Declare a 'member' y on x if x was a function.

            // Look up the function in the local scope, since prototype assignments should
            // follow the function declaration

            // TODO:SQ: Check casts - we are casting up (IIdentifier)!
            var classId =
                node.Left.Cast<IPropertyAccessExpression>()
                    .Expression.Cast<IPropertyAccessExpression>()
                    .Expression.Cast<IIdentifier>();

            var funcSymbol = m_container.Locals[classId.Text];
            if ((funcSymbol == null) || (funcSymbol.Flags & SymbolFlags.Function) == SymbolFlags.None)
            {
                return;
            }

            // Declare the method/property
            DeclareSymbol(funcSymbol.GetMembers(), funcSymbol, node.Left.Cast<IPropertyAccessExpression>(), SymbolFlags.Property,
                SymbolFlags.PropertyExcludes);
        }

        private void BindCallExpression(ICallExpression node)
        {
            // We're only inspecting call expressions to detect CommonJS modules, so we can skip
            // this check if we've already seen the module indicator
            if ((m_file.CommonJsModuleIndicator == null) && (IsRequireCall(node) != null))
            {
                SetCommonJsModuleIndicator(node);
            }
        }

        private void BindClassLikeDeclaration(IClassLikeDeclaration node)
        {
            if (node.Kind == SyntaxKind.ClassDeclaration)
            {
                BindBlockScopedDeclaration(node, SymbolFlags.Class, SymbolFlags.ClassExcludes);
            }
            else
            {
                var bindingName = node.Name != null ? node.Name.Text : "__class";
                BindAnonymousDeclaration(node, SymbolFlags.Class, bindingName);

                // Add name of class expression into the map for semantic classifier
                if (node.Name != null)
                {
                    m_classifiableNames[node.Name.Text] = node.Name.Text;
                }
            }

            var symbol = node.Symbol;

            // TypeScript 1.0 spec (April 2014): 8.4
            // Every class automatically contains a static property member named 'prototype', the
            // type of which is an instantiation of the class type with type Any supplied as a type
            // argument for each type parameter. It is an error to explicitly declare a static
            // property member with the name 'prototype'.
            //
            // Note: we check for this here because this class may be merging into a module.  The
            // module might have an exported variable called 'prototype'.  We can't allow that as
            // that would clash with the built-in 'prototype' for the class.
            var prototypeSymbol = CreateSymbol(SymbolFlags.Property | SymbolFlags.Prototype, "prototype");

            if (symbol.Exports[prototypeSymbol.Name] != null)
            {
                if (node.Name != null)
                {
                    node.Name.Parent = node;
                }

                m_file.BindDiagnostics.Add(
                    Diagnostic.CreateDiagnosticForNode(
                        symbol.Exports[prototypeSymbol.Name].DeclarationList[0],
                        Errors.Duplicate_identifier_0, prototypeSymbol.Name));
            }

            symbol.Exports[prototypeSymbol.Name] = prototypeSymbol;
            prototypeSymbol.Parent = symbol;
        }

        private void BindEnumDeclaration(IEnumDeclaration node)
        {
            // TODO:saqadri - return Unit type
            if (IsConst(node))
            {
                BindBlockScopedDeclaration(node, SymbolFlags.ConstEnum, SymbolFlags.ConstEnumExcludes);
            }
            else
            {
                BindBlockScopedDeclaration(node, SymbolFlags.RegularEnum, SymbolFlags.RegularEnumExcludes);
            }
        }

        private void BindVariableDeclarationOrBindingElement(/*IVariableDeclaration | IBindingElement*/ IDeclaration node)
        {
            IdentifierOrBindingPattern name = node.Kind == SyntaxKind.VariableDeclaration
                ? node.Cast<IVariableDeclaration>().Name
                : node.Cast<IBindingElement>().Name;

            if (m_inStrictMode)
            {
                CheckStrictModeEvalOrArguments(node, name);
            }

            if (IsBindingPattern(name) == null)
            {
                if (IsBlockOrCatchScoped(node))
                {
                    BindBlockScopedVariableDeclaration(node);
                }
                else if (IsParameterDeclaration(node))
                {
                    // It is safe to walk up parent chain to find whether the node is a destructing parameter declaration
                    // because its parent chain has already been set up, since parents are set before descending into children.
                    //
                    // If node is a binding element in parameter declaration, we need to use ParameterExcludes.
                    // Using ParameterExcludes flag allows the compiler to report an error on duplicate identifiers in Parameter Declaration
                    // For example:
                    //      function foo([a,a]) { } // Duplicate Identifier error
                    //      function bar(a,a) { }   // Duplicate Identifier error, parameter declaration in this case is handled in bindParameter
                    //                      // which correctly set excluded symbols
                    DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.FunctionScopedVariable, SymbolFlags.ParameterExcludes);
                }
                else
                {
                    DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.FunctionScopedVariable,
                        SymbolFlags.FunctionScopedVariableExcludes);
                }
            }
        }

        private void BindParameter(IParameterDeclaration node)
        {
            if (m_inStrictMode)
            {
                // It is a SyntaxError if the identifier eval or arguments appears within a FormalParameterList of a
                // strict mode FunctionLikeDeclaration or FunctionExpression(13.1)
                CheckStrictModeEvalOrArguments(node, node.Name);
            }

            if (IsBindingPattern(node.Name) != null)
            {
                BindAnonymousDeclaration(node, SymbolFlags.FunctionScopedVariable, GetDestructuringParameterName(node));
            }
            else
            {
                DeclareSymbolAndAddToSymbolTable(node, SymbolFlags.FunctionScopedVariable, SymbolFlags.ParameterExcludes);
            }

            // If this is a property-parameter, then also declare the property symbol into the
            // containing class.
            if (IsParameterPropertyDeclaration(node))
            {
                var classDeclaration = node.Parent.Parent.Cast<IClassLikeDeclaration>();
                DeclareSymbol(classDeclaration.Symbol.Members, classDeclaration.Symbol, node,
                    SymbolFlags.Property, SymbolFlags.PropertyExcludes);
            }
        }

        private void BindPropertyOrMethodOrAccessor(IDeclaration node, SymbolFlags symbolFlags, SymbolFlags symbolExcludes)
        {
            // TODO:SQ: Return Unit type
            if (HasDynamicName(node))
            {
                BindAnonymousDeclaration(node, symbolFlags, "__computed");
            }
            else
            {
                DeclareSymbolAndAddToSymbolTable(node, symbolFlags, symbolExcludes);
            }
        }

        // reachability checks
        private bool PushNamedLabel(IIdentifier name)
        {
            InitializeReachabilityStateIfNecessary();

            if (m_labelIndexMap.ContainsKey(name.Text))
            {
                return false;
            }

            m_labelStack.Add(Reachability.Unintialized);
            m_labelIndexMap[name.Text] = m_labelStack.Count - 1;

            return true;
        }

        private int PushImplicitLabel()
        {
            InitializeReachabilityStateIfNecessary();

            m_labelStack.Add(Reachability.Unintialized);
            var index = m_labelStack.Count - 1;

            m_implicitLabels.Add(index);

            return index;
        }

        private void PopNamedLabel(IIdentifier label, Reachability outerState)
        {
            Contract.Assert(m_labelIndexMap.ContainsKey(label.Text));

            var index = m_labelIndexMap[label.Text];
            Contract.Assert(m_labelStack.Count == index + 1);

            m_labelIndexMap.Remove(label.Text);

            // labelStack.pop()
            Reachability labelReachability = m_labelStack[index];
            m_labelStack.RemoveAt(index);

            SetCurrentStateAtLabel(labelReachability, outerState, label);
        }

        private void PopImplicitLabel(int implicitLabelIndex, Reachability outerState)
        {
            if (m_labelStack.Count != implicitLabelIndex + 1)
            {
                Contract.Assert(false, I($"Label stack: {m_labelStack.Count}, index:{implicitLabelIndex}"));
            }

            // implicitLabels.pop()
            var i = m_implicitLabels[m_implicitLabels.Count - 1];
            m_implicitLabels.RemoveAt(m_implicitLabels.Count - 1);

            if (implicitLabelIndex != i)
            {
                Contract.Assert(false, I($"i: {i}, index: {implicitLabelIndex}"));
            }

            // labelStack.pop()
            Reachability labelReachability = m_labelStack[implicitLabelIndex];
            m_labelStack.RemoveAt(implicitLabelIndex);

            SetCurrentStateAtLabel(labelReachability, outerState, /*name*/ null);
        }

        private void SetCurrentStateAtLabel(Reachability innerMergedState, Reachability outerState, IIdentifier label)
        {
            if (innerMergedState == Reachability.Unintialized)
            {
                if (label != null &&
                    (!m_options.AllowUnusedLabels.HasValue || !m_options.AllowUnusedLabels.Value))
                {
                    m_file.BindDiagnostics.Add(
                        Diagnostic.CreateDiagnosticForNode(label, Errors.Unused_label));
                }

                m_currentReachabilityState = outerState;
            }
            else
            {
                m_currentReachabilityState = Or(innerMergedState, outerState);
            }
        }

        private bool JumpToLabel(IIdentifier label, Reachability outerState)
        {
            InitializeReachabilityStateIfNecessary();

            // const index = label ? labelIndexMap[label.Text] : lastOrUndefined(implicitLabels);
            int index = -1;
            if (label != null)
            {
                int labeledIndex;
                if (m_labelIndexMap.TryGetValue(label.Text, out labeledIndex))
                {
                    index = labeledIndex;
                }
            }
            else if (m_implicitLabels.Count > 0)
            {
                index = m_implicitLabels[m_implicitLabels.Count - 1];
            }

            if (index == -1)
            {
                // reference to unknown label or
                // break/continue used outside of loops
                return false;
            }

            var stateAtLabel = m_labelStack[index];
            m_labelStack[index] = stateAtLabel == Reachability.Unintialized ? outerState : Or(stateAtLabel, outerState);

            return true;
        }

        private bool CheckUnreachable(INode node)
        {
            switch (m_currentReachabilityState)
            {
                case Reachability.Unreachable:
                    var reportError =

                        // report error on all statements except empty ones
                        (IsStatement(node) && node.Kind != SyntaxKind.EmptyStatement) ||

                        // report error on class declarations
                        node.Kind == SyntaxKind.ClassDeclaration ||

                        // report error on instantiated modules or const-enums only modules if preserveConstEnums is set
                        (node.Kind == SyntaxKind.ModuleDeclaration &&
                         ShouldReportErrorOnModuleDeclaration(node.Cast<IModuleDeclaration>())) ||

                        // report error on regular enums and const enums if preserveConstEnums is set
                        (node.Kind == SyntaxKind.EnumDeclaration &&
                         (!node.IsConstEnumDeclaration() ||
                          (m_options.PreserveConstEnums.HasValue && m_options.PreserveConstEnums.Value)));

                    if (reportError)
                    {
                        m_currentReachabilityState = Reachability.ReportedUnreachable;

                        // unreachable code is reported if
                        // - user has explicitly asked about it AND
                        // - statement is in not ambient context (statements in ambient context is already an error
                        //   so we should not report extras) AND
                        //   - node is not variable statement OR
                        //   - node is block scoped variable statement OR
                        //   - node is not block scoped variable statement and at least one variable declaration has initializer
                        //   Rationale: we don't want to report errors on non-initialized var's since they are hoisted
                        //   On the other side we do want to report errors on non-initialized 'lets' because of TDZ (Temporal Dead Zone)
                        var reportUnreachableCode =
                            (!m_options.AllowUnreachableCode.HasValue || !m_options.AllowUnreachableCode.Value)
                            && !IsInAmbientContext(node)
                            && (node.Kind != SyntaxKind.VariableStatement
                                || (GetCombinedNodeFlags(node.Cast<IVariableStatement>().DeclarationList) & NodeFlags.BlockScoped) != NodeFlags.None
                                || Any(node.Cast<IVariableStatement>().DeclarationList.Declarations, d => d.Initializer != null));

                        if (reportUnreachableCode)
                        {
                            ErrorOnFirstToken(node, Errors.Unreachable_code_detected);
                        }
                    }

                    return true;

                case Reachability.ReportedUnreachable:
                    return true;

                default:
                    return false;
            }
        }

        private bool ShouldReportErrorOnModuleDeclaration(IModuleDeclaration node)
        {
            var instanceState = GetModuleInstanceState(node);
            return (instanceState == ModuleInstanceState.Instantiated) ||
                   (instanceState == ModuleInstanceState.ConstEnumOnly && m_options.PreserveConstEnums.HasValue &&
                    m_options.PreserveConstEnums.Value);
        }

        // TODO:SQ: This can be replaced with field initializers on the class
        private void InitializeReachabilityStateIfNecessary()
        {
            if (m_labelIndexMap != null)
            {
                return;
            }

            m_currentReachabilityState = Reachability.Reachable;
            m_labelIndexMap = new Map<int>();
            m_labelStack = new List<Reachability>();
            m_implicitLabels = new List<int>();
        }
    }
}

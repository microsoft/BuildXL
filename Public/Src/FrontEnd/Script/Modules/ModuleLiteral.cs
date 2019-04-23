// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Script.Declarations;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.Utilities;
using BuildXL.Utilities.Qualifier;
using BuildXL.Utilities.Tasks;
using JetBrains.Annotations;
using TypeScript.Net.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using BindingDictionary = System.Collections.Generic.Dictionary<BuildXL.Utilities.SymbolAtom, BuildXL.FrontEnd.Script.Values.ModuleBinding>;
using LineInfo = TypeScript.Net.Utilities.LineInfo;
using NsBindingDictionary = System.Collections.Generic.Dictionary<BuildXL.Utilities.FullSymbol, BuildXL.FrontEnd.Script.Values.ModuleBinding>;

// Enable below code for testing specialized dictionaries.
// using BindingDictionary = BuildXL.FrontEnd.Script.Util.DsSymbolDictionary<BuildXL.FrontEnd.Script.Types.Value.ModuleBinding>;
// using NsBindingDictionary = BuildXL.FrontEnd.Script.Util.DsFullSymbolDictionary<BuildXL.FrontEnd.Script.Types.Value.ModuleBinding>;
namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Base class for DScript module like files or named entries like interface, enum or namespace.
    /// </summary>
    /// <remarks>
    /// Currently, there are 4 different kinds of 'module literals':
    /// - File (regular file literal for v1 and resolved file literal for v2)
    /// - Globals
    /// - Type or namespace (enum, interface, or namespace)
    ///
    /// 'Modules' forms a hierarchical structure:
    /// Globals -> File -> Types or Namespaces
    ///
    /// Type or namespace in TypeScript/DScript forms hierarchy: one namespace could have nested namespace that can hold types etc.
    /// For performance reasons, namespaces are not nested per se. Instead of that, all names are stored in a flat list in file modules
    /// (that's why there is cache for partial symbols).
    ///
    /// The name is unfortunate, but left for historical reasons.
    /// </remarks>
    public abstract class ModuleLiteral : ModuleOrObjectBase
    {
        /// <summary>
        /// Qualifier instance for the current instance.
        /// </summary>
        protected QualifierValue m_qualifier;

        // Synchronization primitive for this instance.
        // Currently the same instance is used to get exclusive access for two fields: m_bindings and m_nsBidings.
        // This should not be a problem in a real world, because it is unlikely that two threads will try to modify both of them.
        // If this will become a problem, different sync roots should be added.
        private readonly object m_syncRoot = new object();

        // Single array to use in case of errors when evaluating multiple values.
        private static readonly object[] s_errorValueAsObjectArray = new[] { (object)ErrorValue.Instance };

        /// <summary>
        /// Top-level bindings like top level variable declarations.
        /// </summary>
        /// <remarks>
        /// These bindings are the non-module or non-namespace bindings. These bindings are shared among the module
        /// instances (or module values) and the uninstantiated module.
        ///
        /// Visibility should be internal, because File module reads/writes this field.
        /// </remarks>
        [CanBeNull]
        [SuppressMessage("Microsoft.StyleCop.CSharp.NamingRules", "SA1307:AccessibleFieldsMustBeginWithUpperCaseLetter")]
        protected internal BindingDictionary m_bindings;

        /// <summary>
        /// Namespace bindings that represents nested namespaces for a current "module".
        /// </summary>
        /// <remarks>
        /// These bindings are the bindings for namespaces.
        /// Valid only for file modules and globals only. Shared between the uninstantiated module and its module instance.
        /// </remarks>
        [CanBeNull]
        protected NsBindingDictionary m_nsBindings;

        /// <summary>
        /// Module identifier.
        /// Could be invalid for global module.
        /// </summary>
        public ModuleLiteralId Id { get; }

        /// <summary>
        /// Returns path of the current module.
        /// Valid if instance is a <see cref="FileModuleLiteral"/>.
        /// </summary>
        public AbsolutePath Path => Id.Path;

        /// <summary>
        /// Name of the module.
        /// For type or namespace this will return a name of the type or namespace. For all other cases, this would be <see cref="FullSymbol.Invalid"/>.
        /// </summary>
        public FullSymbol Name => Id.Name;

        /// <summary>
        /// Checks if this module is a file module.
        /// </summary>
        public bool IsFileModule => Kind == SyntaxKind.FileModuleLiteral;

        /// <summary>
        /// Checks if this module is a file or globals.
        /// </summary>
        /// <remarks>
        /// This separation is required because some logic is applicable for both - files and globals only.
        /// </remarks>
        protected internal bool IsFileOrGlobal => Kind != SyntaxKind.TypeOrNamespaceModuleLiteral;

        /// <summary>
        /// Outer scope.
        /// </summary>
        /// <remarks>
        /// For types and namespaces this property returns owning file module, for file module - globals, and null for globals.
        /// </remarks>
        [CanBeNull]
        public ModuleLiteral OuterScope { get; }

        /// <summary>
        /// Returns file module for this module instance.
        /// Null for globals, 'this' for file module and owning file for type or namespace.
        /// </summary>
        [CanBeNull]
        public abstract FileModuleLiteral CurrentFileModule { get; }

        /// <summary>
        /// Qualifier.
        /// </summary>
        /// <remarks>
        /// Qualifier should be the Unqualified for all uninstantiated modules, and all non-top-level modules.
        /// </remarks>
        [NotNull]
        public QualifierValue Qualifier => m_qualifier;

        /// <summary>
        /// Package that owns this file module.
        /// </summary>
        /// <remarks>
        /// Null for <see cref="GlobalModuleLiteral"/> or not-null for other kind of module literals.
        /// </remarks>
        [CanBeNull]
        public abstract Package Package { get; }

        /// <summary>
        /// Checks if this module literal is empty.
        /// </summary>
        public virtual bool IsEmpty => (m_bindings == null || m_bindings.Count == 0)
                               && (m_nsBindings == null || m_nsBindings.Count == 0);

        #region Constructors and Factory Methods

        /// <summary>
        /// Instantiates the module literal with the provided qualifier
        /// </summary>
        public abstract ModuleLiteral Instantiate(ModuleRegistry moduleRegistry, QualifierValue qualifier);

        /// <nodoc/>
        protected ModuleLiteral(ModuleLiteralId id, QualifierValue qualifier, ModuleLiteral outerScope, LineInfo location)
            : base(location)
        {
            Id = id;
            m_qualifier = qualifier;
            OuterScope = outerScope;
        }

        /// <summary>
        /// Constructs module literal for namespace.
        /// </summary>
        protected static TypeOrNamespaceModuleLiteral CreateTypeOrNamespaceModule(FullSymbol namespaceName, ModuleLiteral outerScope, LineInfo location)
        {
            Contract.Requires(namespaceName.IsValid);
            Contract.Requires(outerScope != null);
            Contract.Requires(outerScope.IsFileOrGlobal);

            ModuleLiteralId moduleId = outerScope.Id.WithName(namespaceName);

            return new TypeOrNamespaceModuleLiteral(moduleId, qualifier: QualifierValue.Unqualified,
                outerScope: outerScope, location: location);
        }

        /// <summary>
        /// Constructs an uninstantiated file module denoted by a path.
        /// </summary>
        /// <remarks>
        /// This factory should only be used during parsing.
        /// </remarks>
        public static FileModuleLiteral CreateFileModule(AbsolutePath path, IModuleRegistry moduleRegistry, Package package, LineMap lineMap)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(moduleRegistry != null);
            Contract.Requires(package != null);
            Contract.Requires(lineMap != null);

            return CreateInstantiatedFileModule(path, QualifierValue.Unqualified, ((ModuleRegistry)moduleRegistry).GlobalLiteral, package, (ModuleRegistry)moduleRegistry, lineMap: lineMap);
        }


        /// <summary>
        /// Constructs an uninstantiated file module denoted by a path.
        /// </summary>
        /// <remarks>
        /// This factory should only be used during parsing.
        /// </remarks>
        public static FileModuleLiteral CreateFileModule(AbsolutePath path, GlobalModuleLiteral globalScope, Package package, ModuleRegistry moduleRegistry, LineMap lineMap)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(globalScope != null);
            Contract.Requires(package != null);
            Contract.Requires(lineMap != null);

            return CreateInstantiatedFileModule(path, QualifierValue.Unqualified, globalScope, package, moduleRegistry, lineMap: lineMap);
        }

        /// <summary>
        /// Constructs an instantiated file module denoted by a path.
        /// </summary>
        /// <remarks>
        /// The outer scope is typically the global module.
        /// </remarks>
        protected static FileModuleLiteral CreateInstantiatedFileModule(AbsolutePath path, QualifierValue qualifier, GlobalModuleLiteral globalScope, Package package, ModuleRegistry moduleRegistry, LineMap lineMap)
        {
            return new FileModuleLiteral(path, qualifier, globalScope, package, moduleRegistry, lineMap);
        }

        #endregion Constructors and FactoryMethods

        // Following set of methods are used in V2 name resolution based on a semantic information from the checker.

        /// <summary>
        /// Stores a given function at a given location.
        /// </summary>
        public virtual void AddResolvedEntry(FilePosition location, FunctionLikeExpression lambda)
        {
            CurrentFileModule?.AddResolvedEntry(location, lambda);
        }

        /// <summary>
        /// Stores a given resolved entry at a given location.
        /// </summary>
        public virtual void AddResolvedEntry(FilePosition location, ResolvedEntry entry)
        {
            CurrentFileModule?.AddResolvedEntry(location, entry);
        }

        /// <summary>
        /// Stores a given resolved entry by a given name.
        /// </summary>
        public virtual void AddResolvedEntry(FullSymbol fullName, ResolvedEntry entry)
        {
            CurrentFileModule?.AddResolvedEntry(fullName, entry);
        }

        /// <inheritdoc />
        public sealed override void Accept(Visitor visitor)
        {
            // The current visitor pattern visit the node and possibly all of its children.
            // Visiting module literals means visiting all of the declarations in them.
            // Most of declarations are "let x = expr", where expression are typically thunks wrapped
            // in a so-called binding. Currently, there is no need for visiting module literals.
            // Thus, we doing nothing.
        }

        /// <inheritdoc />
        protected override EvaluationResult DoEval(Context context, ModuleLiteral env, EvaluationStackFrame frame)
        {
            return EvaluationResult.Create(this);
        }

        /// <summary>
        /// Returns resolved entry by position.
        /// </summary>
        /// <remarks>
        /// Implemented in <see cref="FileModuleLiteral"/>.
        /// </remarks>
        public virtual bool TryGetResolvedEntry(ModuleRegistry moduleRegistry, FilePosition location, out ResolvedEntry resolvedEntry, out FileModuleLiteral resolvedModule)
        {
            resolvedEntry = default(ResolvedEntry);
            resolvedModule = null;
            return false;
        }

        /// <summary>
        /// Returns resolved entry by the full name.
        /// </summary>
        /// <remarks>
        /// Implemented in <see cref="FileModuleLiteral"/>.
        /// </remarks>
        protected virtual bool TryGetResolvedEntryByFullName(FullSymbol fullName, out ResolvedEntry resolvedEntry)
        {
            resolvedEntry = default(ResolvedEntry);
            return false;
        }

        /// <nodoc />
        public sealed override EvaluationResult GetOrEvalField(Context context, SymbolAtom name, bool recurs, ModuleLiteral origin, LineInfo location)
        {
            Contract.Requires(name.IsValid);

            return GetOrEvalField(context, name, this, recurs: recurs, origin: origin, location: location);
        }

        /// <summary>
        /// Evaluates a member using by resolving a symbol by a full name.
        /// </summary>
        /// <remarks>
        /// DScript V2 feature.
        /// </remarks>
        internal EvaluationResult EvaluateEntryByFullName(Context context, FullSymbol fullName, LineInfo location)
        {
            ResolvedEntry resolvedEntry = default(ResolvedEntry);
            if (CurrentFileModule?.TryGetResolvedEntryByFullName(fullName, out resolvedEntry) == true)
            {
                return EvaluateResolvedSymbol(context, this, location, resolvedEntry);
            }

            // This is an assertion but not a graceful error, because resolution may fail only if something went wrong.
            string message = I($"Can't find resolved symbol by a full name '{fullName.ToString(context.FrontEndContext.SymbolTable)}'");
            Contract.Assert(false, message);

            return EvaluationResult.Undefined;
        }

        /// <summary>
        /// Evaluates a member by resolving a symbol at a given location.
        /// </summary>
        /// <remarks>
        /// DScript V2 feature.
        /// </remarks>
        internal EvaluationResult EvaluateByLocation(Context context, FilePosition filePosition, FullSymbol nameForDebuggingPurposes, LineInfo location)
        {
            if (TryResolveEntryByLocation(context, filePosition, nameForDebuggingPurposes, out var resolvedEntry, out var owningFileModule))
            {
                return EvaluateResolvedSymbol(context, owningFileModule, location, resolvedEntry);
            }

            return EvaluationResult.Undefined;
        }

        /// <summary>
        /// Evaluates a resolved entry that is not a Thunk
        /// </summary>
        internal bool TryResolveFunction(Context context, FilePosition filePosition, FullSymbol nameForDebuggingPurposes, out FunctionLikeExpression lambda, out FileModuleLiteral file)
        {
            if (TryResolveEntryByLocation(context, filePosition, nameForDebuggingPurposes, out var resolvedEntry, out file))
            {
                lambda = resolvedEntry.Function;
                return true;
            }

            lambda = null;
            return false;
        }

        private bool TryResolveEntryByLocation(Context context, FilePosition filePosition, FullSymbol nameForDebuggingPurposes, out ResolvedEntry resolvedEntry, out FileModuleLiteral owningFileModule)
        {
            owningFileModule = default(FileModuleLiteral);
            resolvedEntry = default(ResolvedEntry);

            if (CurrentFileModule?.TryGetResolvedEntry(context.ModuleRegistry, filePosition, out resolvedEntry, out owningFileModule) == true)
            {
                return true;
            }

            // This is an assertion but not a graceful error, because resolution may fail only if something went wrong.
            string message =
                I($"Can't find resolved symbol '{nameForDebuggingPurposes.ToString(context.FrontEndContext.SymbolTable)}' at position '{filePosition.Position}' from source file '{filePosition.Path.ToString(context.PathTable)}'");

            Contract.Assert(false, message);
            return false;
        }

        private EvaluationResult EvaluateResolvedSymbol(Context context, ModuleLiteral module, LineInfo location, ResolvedEntry resolvedEntry)
        {
            Contract.Requires(module != null);

            if (resolvedEntry.Thunk != null)
            {
                var thunk = resolvedEntry.Thunk;

                return thunk.EvaluateWithNewNamedContext(context, module, resolvedEntry.ThunkContextName, location);
            }

            return EvaluateNonThunkedResolvedSymbol(context, module, resolvedEntry);
        }

        /// <summary>
        /// Evaluates a resolved entry that is not a Thunk
        /// </summary>
        internal EvaluationResult EvaluateNonThunkedResolvedSymbol(Context context, ModuleLiteral module, ResolvedEntry resolvedEntry)
        {
            Contract.Assert(resolvedEntry.Thunk == null);

            if (resolvedEntry.ConstantExpression != null)
            {
                return EvaluationResult.Create(resolvedEntry.ConstantExpression.Value);
            }

            using (var empty = EvaluationStackFrame.Empty())
            {
                if (resolvedEntry.Expression != null)
                {
                    if (resolvedEntry.Expression is TypeOrNamespaceModuleLiteral namespaceExpression)
                    {
                        // If the resolved entry already evaluated to a namespace and the namespace
                        // is already qualified, then there is no reason for instantiating it twice. Just reusing it.
                        if (namespaceExpression.Qualifier.IsQualified())
                        {
                            return EvaluationResult.Create(namespaceExpression);
                        }

                        return EvaluationResult.Create(namespaceExpression.Instantiate(context.ModuleRegistry, GetFileQualifier()));
                    }

                    return resolvedEntry.Expression.Eval(context, module, empty);
                }

                if (resolvedEntry.ResolverCallback != null)
                {
                    return resolvedEntry.ResolverCallback(context, module, empty).GetAwaiter().GetResult();
                }

                Contract.Assert(resolvedEntry.Function != null);
                return resolvedEntry.Function.Eval(context, module, empty);
            }
        }

        /// <inheritdoc />
        public sealed override bool TryProject(Context context, SymbolAtom name, ModuleLiteral origin, out EvaluationResult result, LineInfo location)
        {
            result = GetOrEvalField(context, name, recurs: false, origin: origin, location: Location);
            return true;
        }

        /// <summary>
        /// Gets the field body if exists.
        /// </summary>
        /// <remarks>
        /// This method does not evaluate the body of the field if exists, and moreover, does not recurs to the parent.
        /// </remarks>
        public bool TryGetField(SymbolAtom name, out object body)
        {
            ModuleBinding binding = null;

            // TODO:ST: sounds reasonable to add "warning" if name was resolved but it is not exposed!
            if (m_bindings?.TryGetValue(name, out binding) == true && binding.IsExported)
            {
                body = binding.Body;
                return true;
            }

            body = null;
            return false;
        }

        /// <summary>
        /// Gets module or namespace based on module id.
        /// </summary>
        public EvaluationResult GetNamespace(ImmutableContextBase context, FullSymbol fullName, bool recurs, ModuleLiteral origin, LineInfo location)
        {
            ModuleBinding binding = GetNamespaceBinding(context, fullName, recurs);

            if (binding == null)
            {
                context.Errors.ReportMissingNamespace(origin ?? this, fullName, this, location);
                return EvaluationResult.Error;
            }

            return EvaluationResult.Create(binding.Body);
        }

        /// <summary>
        /// Evaluates all.
        /// </summary>
        public Task<bool> EvaluateAllAsync(ImmutableContextBase context, VisitedModuleTracker moduleTracker, ModuleEvaluationMode mode = ModuleEvaluationMode.None)
        {
            if (mode == ModuleEvaluationMode.None)
            {
                moduleTracker.Track(this, context);
                return EvaluateAllNamedValuesAsync(context);
            }

            return EvaluateAllTransitiveClosureAsync(context, mode, moduleTracker);
        }

        /// <summary>
        /// Gets the qualifier associated with the file module.
        /// </summary>
        public abstract QualifierValue GetFileQualifier();

        /// <summary>
        /// Adds a type with a given name.
        /// qualifierSpaceId is null for V1 and not null for v2.
        /// This is needed for a semantic-based evaluation and helps to filter nested variable declarations from the evaluation.
        /// </summary>
        internal virtual void AddType(FullSymbol name, UniversalLocation location, QualifierSpaceId? qualifierSpaceId, out TypeOrNamespaceModuleLiteral module)
        {
            AddNamespace(name, location, qualifierSpaceId, out module);
        }

        /// <summary>
        /// Adds a namespace with a given name.
        /// </summary>
        internal virtual void AddNamespace(FullSymbol name, UniversalLocation location, QualifierSpaceId? qualifierSpaceId, out TypeOrNamespaceModuleLiteral module)
        {
            Contract.Requires(name.IsValid);
            Contract.Requires(Qualifier == QualifierValue.Unqualified);
            Contract.Assert(IsFileOrGlobal, "Current instance should be a file module or global ambient module");

            lock (m_syncRoot)
            {
                m_nsBindings = m_nsBindings ?? new NsBindingDictionary();

                if (m_nsBindings.ContainsKey(name))
                {
                    var moduleBinding = m_nsBindings[name];
                    Contract.Assert(moduleBinding.Body is TypeOrNamespaceModuleLiteral);

                    module = (TypeOrNamespaceModuleLiteral)moduleBinding.Body;
                    return;
                }

                module = CreateTypeOrNamespaceModule(name, outerScope: this, location: location.AsLineInfo());

                // Module is always exported.
                m_nsBindings.Add(name, new ModuleBinding(module, Declaration.DeclarationFlags.Export, location.AsLineInfo()));
            }
        }

        /// <summary>
        /// Adds a binding.
        /// </summary>
        public bool AddBinding(SymbolAtom name, object body, Declaration.DeclarationFlags modifier, LineInfo location)
        {
            Contract.Requires(name.IsValid);
            Contract.Requires(Qualifier == QualifierValue.Unqualified);

            return AddBinding(name, new ModuleBinding(body, modifier, location));
        }

        /// <summary>
        /// Adds a binding.
        /// </summary>
        public bool AddBinding(SymbolAtom name, ModuleBinding binding)
        {
            Contract.Requires(name.IsValid);
            Contract.Requires(Qualifier == QualifierValue.Unqualified);
            Contract.Requires(binding != null);

            lock (m_syncRoot)
            {
                m_bindings = m_bindings ?? new BindingDictionary();
                if (m_bindings.ContainsKey(name))
                {
                    return false;
                }

                m_bindings.Add(name, binding);
            }

            return true;
        }

        /// <summary>
        /// Gets module binding based on module full symbol name.
        /// </summary>
        protected internal ModuleBinding GetNamespaceBinding(ImmutableContextBase context, FullSymbol fullName, bool recurs)
        {
            // Namespace resolution performs in 3 steps:
            // 1. Trying resolve namespace locally using local namespace table
            // 2. Trying resolve by looking into imported namespaces
            // 3. Trying resolve by creating partial name and looking in the file module.
            ModuleLiteral current = this;

            while (current != null)
            {
                ModuleBinding binding = null;

                // Trying to resolve in the local table
                if (current.m_nsBindings?.TryGetValue(fullName, out binding) == true)
                {
                    return binding;
                }

                // Local namespace table doesn't have a requested name. Looking in forwared...

                // The same logic could be achieved using virtual dispatch, but this implementation
                // keep all the logic in one place.
                if (current is FileModuleLiteral currentAsFile)
                {
                    if (currentAsFile.TryResolveExtendedName(context, Name, fullName, out binding))
                    {
                        return binding;
                    }
                }

                current = recurs ? current.OuterScope : null;
            }

            return null;
        }

        /// <summary>
        /// Evaluates all named values and all reachable file modules via local import/export relation.
        /// </summary>
        protected internal async Task<bool> EvaluateAllTransitiveClosureAsync(
            ImmutableContextBase context,
            ModuleEvaluationMode mode,
            VisitedModuleTracker tracker)
        {
            var evaluateTasks = new List<Task<object>>();

            EvaluateAllNamedValues(context, evaluateTasks);

            object[] results = await TaskUtilities.WithCancellationHandlingAsync(
                context.LoggingContext,
                Task.WhenAll(evaluateTasks),
                context.Logger.EvaluationCanceled,
                s_errorValueAsObjectArray,
                context.EvaluationScheduler.CancellationToken);

            return results.All(result => !result.IsErrorValue());
        }

        private EvaluationResult GetOrEvalField(Context context, SymbolAtom name, ModuleLiteral startEnv, bool recurs, ModuleLiteral origin, LineInfo location)
        {
            // This logic is still used only V1 evaluation
            if (IsFileModule && name == context.ContextTree.CommonConstants.Qualifier)
            {
                // Someone references 'qualifier' on the file level.
                return EvaluationResult.Create(Qualifier.Qualifier);
            }

            ModuleBinding binding = null;

            // TODO:ST: sounds reasonable to add "warning" if name was resolved but it is not exposed!
            if (m_bindings?.TryGetValue(name, out binding) == true && (recurs || binding.IsExported))
            {
                return GetOrEvalFieldBinding(context, name, binding, location);
            }

            if (recurs && OuterScope != null)
            {
                return OuterScope.GetOrEvalField(context, name, startEnv, true, origin, location);
            }

            context.Errors.ReportMissingMember(origin ?? startEnv, name, startEnv, location);

            return EvaluationResult.Error;
        }

        /// <summary>
        /// Evaluates given <paramref name="binding"/> that corresponds to field <paramref name="name"/> 
        /// (<seealso cref="GetOrEvalField(Context, SymbolAtom, bool, ModuleLiteral, LineInfo)"/>)
        /// </summary>
        public EvaluationResult GetOrEvalFieldBinding(Context context, SymbolAtom name, ModuleBinding binding, LineInfo callingLocation)
        {
            object o = binding.Body;

            if (o is Thunk thunk)
            {
                return GetOrEvalFieldBindingThunk(context, name, binding, thunk);
            }

            if (o is Expression expr)
            {
                using (var frame = EvaluationStackFrame.Empty())
                {
                    return expr.Eval(context, this, frame);
                }
            }

            return EvaluationResult.Create(o);
        }

        private EvaluationResult GetOrEvalFieldBindingThunk(ImmutableContextBase context, SymbolAtom name, ModuleBinding binding, Thunk thunk)
        {
            // Keep this call in a separate method to avoid always creating a closure object on the heap in the caller
            // If the thunk hasn't been evaluated yet for the current qualifier, then its evaluation gets kicked off in a newly allocated mutable named context.
            // We must not mutate the context at hand directly, as there might be other concurrent child contexts alive.
            var contextName = GetFullyQualifiedBindingName(context.FrontEndContext.SymbolTable, this, name);

            return thunk.LegacyEvaluateWithNewNamedContext(context, this, contextName, binding.Location);
        }
        
        /// <summary>
        /// Creates an encoding consisting of 0s and 1s, one for each character in <paramref name="name"/>,
        /// where '0' means that the corresponding character in <paramref name="name"/> is lower case,
        /// and '1' means that it is upper case.
        /// </summary>
        private static string EncodeCasing(string name)
        {
            Contract.Requires(name != null);

            return new string(name.Select(ch => char.IsLower(ch) ? '0' : '1').ToArray());
        }

        /// <nodoc />
        protected static FullSymbol GetFullyQualifiedBindingName(SymbolTable symbolTable, ModuleLiteral owningModule, FullSymbol name)
        {
            Contract.Requires(name.IsValid);

            return owningModule.Id.Name.Combine(symbolTable, name);
        }

        /// <nodoc />
        protected static FullSymbol GetFullyQualifiedBindingName(SymbolTable symbolTable, ModuleLiteral owningModule, SymbolAtom name)
        {
            Contract.Requires(name.IsValid);

            return owningModule.Id.Name.Combine(symbolTable, name);
        }

        /// <summary>
        /// Evaluates all named values that are declared locally and collects the evaluation tasks.
        /// </summary>
        private async Task<bool> EvaluateAllNamedValuesAsync(ImmutableContextBase context)
        {
            Contract.Requires(context != null);

            var evaluateTasks = new List<Task<object>>();
            EvaluateAllNamedValues(context, evaluateTasks);

            object[] results = await TaskUtilities.WithCancellationHandlingAsync(
                context.LoggingContext,
                Task.WhenAll(evaluateTasks),
                context.Logger.EvaluationCanceled,
                s_errorValueAsObjectArray,
                context.EvaluationScheduler.CancellationToken);

            return results.All(result => !result.IsErrorValue());
        }

        /// <summary>
        /// Evaluates all named values that are declared locally and collects the evaluation tasks.
        /// </summary>
        protected virtual void EvaluateAllNamedValues(ImmutableContextBase context, List<Task<object>> evaluateTasks)
        {
            if (m_nsBindings != null)
            {
                foreach (var moduleBinding in m_nsBindings)
                {
                    var module = moduleBinding.Value.Body as ModuleLiteral;
                    Contract.Assume(module != null);

                    if (module.m_bindings != null)
                    {
                        AddGetOrEvalFieldTasks(context, evaluateTasks, origin: module);
                    }
                }
            }

            if (m_bindings != null)
            {
                // This part allows for the evaluations of values declared out of namespaces.
                AddGetOrEvalFieldTasks(context, evaluateTasks, origin: this);
            }
        }

        /// <nodoc />
        protected static ModuleLiteralId ReadModuleLiteralId(BuildXLReader reader)
        {
            var name = reader.ReadFullSymbol();
            var path = reader.ReadAbsolutePath();

            // Name and Id could be invalid in case of a fake type or namespace.
            if (!name.IsValid && !path.IsValid)
            {
                return default(ModuleLiteralId);
            }

            return ModuleLiteralId.Create(path).WithName(name);
        }

        /// <nodoc />
        protected static void Write(BuildXLWriter writer, ModuleLiteralId moduleId)
        {
            writer.Write(moduleId.Name);
            writer.Write(moduleId.Path);
        }

        private void AddGetOrEvalFieldTasks(ImmutableContextBase context, List<Task<object>> list, ModuleLiteral origin)
        {
            Contract.Requires(origin.m_bindings != null);

            foreach (var binding in origin.m_bindings)
            {
                SymbolAtom name = binding.Key;
                ModuleBinding bindingValue = binding.Value;

                list.Add(RunGetOrEvalFieldAsync(context, origin, name, bindingValue));
            }
        }

        /// <summary>
        /// Creates a task that runs GetOrEvalField taking into consideration concurrency settings
        /// </summary>
        protected Task<object> RunGetOrEvalFieldAsync(ImmutableContextBase context, ModuleLiteral origin, SymbolAtom name, ModuleBinding bindingValue)
        {
            return context.EvaluationScheduler.EvaluateValue(() => Task.FromResult(GetOrEvalField(context, origin, name, bindingValue)));
        }

        private object GetOrEvalField(ImmutableContextBase context, ModuleLiteral origin, SymbolAtom name, ModuleBinding bindingValue)
        {
            // This method may be invoked concurrently on the same context. Thus we may not mutate the context at hand, but must create a local mutable child context.
            using (var localMutableContext = context.CreateWithModule(this))
            {
                var result = origin.GetOrEvalField(
                    localMutableContext,
                    name,
                    recurs: true,
                    origin: origin,
                    location: bindingValue.Location);
                Contract.Assert(!localMutableContext.HasChildren); // just before the newly created context get disposed, we want to assert that all of its child contexts have already been disposed
                return result.Value;
            }
        }

        #region For debugger's sake only

        /// <nodoc />
        public IReadOnlyCollection<KeyValuePair<SymbolAtom, ModuleBinding>> GetFieldBindings()
        {
            return CopyDictionaryForPublicUse(m_bindings);
        }

        /// <nodoc />
        public virtual IEnumerable<KeyValuePair<string, ModuleBinding>> GetAllBindings(Context context)
        {
            return GetBindings(m_bindings, key => key.ToString(context.StringTable)).Concat(
                GetBindings(m_nsBindings, key => key.ToString(context.FrontEndContext.SymbolTable)));
        }
        
        /// <nodoc />
        public virtual IEnumerable<KeyValuePair<string, ModuleBinding>> GetAllBindings(SymbolTable symbolTable)
        {
            return GetBindings(m_bindings, key => key.ToString(symbolTable.StringTable)).Concat(
                GetBindings(m_nsBindings, key => key.ToString(symbolTable)));
        }

        /// <nodoc />
        protected static IEnumerable<KeyValuePair<string, ModuleBinding>> GetBindings<TKey>(
            IEnumerable<KeyValuePair<TKey, ModuleBinding>> values, Func<TKey, string> converter)
        {
            if (values == null)
            {
                yield break;
            }

            foreach (var kvp in values)
            {
                yield return new KeyValuePair<string, ModuleBinding>(converter(kvp.Key), kvp.Value);
            }
        }

        private static IReadOnlyCollection<KeyValuePair<TK, TV>> CopyDictionaryForPublicUse<TK, TV>(IDictionary<TK, TV> dict)
        {
            return dict?.AsEnumerable().ToList() ?? new List<KeyValuePair<TK, TV>>();
        }
        #endregion For debugger's sake only
    }
}

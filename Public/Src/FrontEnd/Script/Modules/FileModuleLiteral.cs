// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Declarations;
using BuildXL.Utilities;
using BuildXL.Utilities.Qualifier;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Sdk;
using TypeScript.Net.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Represents runtime model of a single file.
    /// </summary>
    /// <remarks>
    /// Unlike TypeScript, DScript visibility rules are very similar to C#: each file of a module has an implicit visibility of all the exposed members 
    /// of every file of the module.
    /// 
    /// Each file contains a list of resolved entries that can be evaluated on demand.
    /// </remarks>
    public sealed class FileModuleLiteral : ModuleLiteral
    {
        /// <nodoc />
        private readonly ModuleRegistry m_moduleRegistry;

        /// <summary>
        /// Cache for partial symbols.
        /// </summary>
        /// <remarks>
        /// Partial symbol look-up's are provided to support nested namespace names. Such look-up's typically
        /// occur for enum. For example,
        /// namespace X {
        ///     enum E { v }
        ///     export const y = E.v;
        /// }
        /// // TODO:ST: clarification: in TypeScript following syntax is invalid: X.E.v, so following comment is true, but this is an example of deviation from typescript.
        /// Note that, from inside X, one does not need to access v of E using a fully qualified name X.E.v.
        /// Such a look-up for E is called here partial symbol look-up.
        ///
        /// This cache is provided because it turns out that converting full symbols into partial ones are expensive.
        /// TODO: Consider sharing this cache accross instances.
        /// </remarks>
        private readonly Lazy<ConcurrentDictionary<FullSymbol, ModuleBinding>> m_partialSymbolsCache;

        private Dictionary<FilePosition, ResolvedEntry> m_resolvedEntries = new Dictionary<FilePosition, ResolvedEntry>();

        // To run the tests we need to keep all fullName to ResolveEntry bindings.
        private Dictionary<FullSymbol, ResolvedEntry> m_resolvedEntriesByFullName = new Dictionary<FullSymbol, ResolvedEntry>();

        /// <summary>
        /// Line map associated with the file module literal
        /// </summary>
        public LineMap LineMap { get; }

        /// <inheritdoc/>
        public override Package Package { get; }

        /// <summary>
        /// 'this' is a valid file module in this case.
        /// </summary>
        public override FileModuleLiteral CurrentFileModule => this;

        /// <inheritdoc />
        public override SyntaxKind Kind => SyntaxKind.FileModuleLiteral;

        /// <nodoc/>
        internal FileModuleLiteral(AbsolutePath path, QualifierValue qualifier, GlobalModuleLiteral outerScope, Package package, ModuleRegistry moduleRegistry, LineMap lineMap)
            : this(ModuleLiteralId.Create(path), qualifier, outerScope, package, lineMap)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(lineMap != null);

            m_moduleRegistry = moduleRegistry;
        }

        /// <nodoc/>
        internal FileModuleLiteral(ModuleLiteralId id, QualifierValue qualifier, GlobalModuleLiteral outerScope, Package package, LineMap lineMap)
            : base(id, qualifier, outerScope, location: default(LineInfo))
        {
            Contract.Requires(id.Path.IsValid);
            Contract.Requires(package != null);
            Contract.Requires(lineMap != null);

            Package = package;
            m_partialSymbolsCache = Lazy.Create(() => new ConcurrentDictionary<FullSymbol, ModuleBinding>());
            LineMap = lineMap;
        }

        /// <nodoc />
        private FileModuleLiteral(BuildXLReader reader, PathTable pathTable, AbsolutePath path, Package package, GlobalModuleLiteral outerScope, ModuleRegistry moduleRegistry, LineMap lineMap)
            : this(path, QualifierValue.Unqualified, outerScope, package, moduleRegistry, lineMap)
        {
            var context = new DeserializationContext(this, reader, pathTable, lineMap);

            int resolveEntries = reader.ReadInt32Compact();
            for (int i = 0; i < resolveEntries; i++)
            {
                FilePosition location = ReadFilePosition(reader);

                var resolvedEntry = ResolvedEntry.ReadResolvedEntry(context);
                AddResolvedEntry(location, resolvedEntry);

                if (resolvedEntry.SymbolName.IsValid)
                {
                    AddResolvedEntry(resolvedEntry.SymbolName, resolvedEntry);
                }
            }
        }

        /// <nodoc />
        internal FileModuleLiteral(
            BuildXLReader reader,
            PathTable pathTable,
            GlobalModuleLiteral outerScope,
            ModuleRegistry moduleRegistry,
            LineMap lineMap)
            : this(reader, pathTable, reader.ReadAbsolutePath(), ReadPackage(reader, pathTable), outerScope, moduleRegistry, lineMap)
        { }

        internal static FileModuleLiteral Read(
            BuildXLReader reader,
            PathTable pathTable,
            GlobalModuleLiteral outerScope,
            ModuleRegistry moduleRegistry)
        {
            var kind = (SyntaxKind)reader.ReadInt32Compact();

            var lineMap = LineMap.Read(reader);

            switch (kind)
            {
                case SyntaxKind.FileModuleLiteral:
                    return new FileModuleLiteral(reader, pathTable, outerScope, moduleRegistry, lineMap);
                default:
                    string message = I($"The file module literal {kind} is not deserializable yet.");
                    throw new InvalidOperationException(message);
            }
        }

        /// <inheritdoc/>
        public override void Serialize(BuildXLWriter writer)
        {
            // The kind for the base class (FileModuleLiteral) is sealed and thera are consumers
            // that rely on it for any subclass of FileModuleLiteral. So serializing the most specific kind
            // explicitly so deserialization knows what's coming
            writer.WriteCompact((int)SyntaxKind.FileModuleLiteral);

            LineMap.Write(writer);

            writer.Write(Path);

            WritePackage(Package, writer);

            // Don't need to save qualifier, because it only valid for instantiated modules, and this module should be uninstantiated.
            writer.WriteCompact(m_resolvedEntries.Count);
            foreach (var kvp in m_resolvedEntries)
            {
                WriteFilePosition(kvp.Key, writer);
                kvp.Value.Serialize(writer);
            }
        }

        /// <summary>
        /// Instantiates this file module for with a given qualifier.
        /// </summary>
        public override ModuleLiteral Instantiate(ModuleRegistry moduleRegistry, QualifierValue qualifier)
        {
            Contract.Assert(moduleRegistry != null);
            Contract.Assert(qualifier != QualifierValue.Unqualified);

            // Due to sharing the following contract no longer holds: Contract.Requires(Qualifier == Unqualified);
            var moduleKey = QualifiedModuleId.Create(Id, qualifier.QualifierId);

            var globalOuterScope = OuterScope as GlobalModuleLiteral;

            Contract.Assert(globalOuterScope != null, "For a FileModuleLiteral, the outer scope should be always a global module literal");

            return moduleRegistry.InstantiateModule((this, qualifier, globalOuterScope), moduleKey,
                (state, k) =>
                {
                    // Avoid closure allocation.
                    var @this = state.Item1;
                    var capturedQualifier = state.Item2;
                    var capturedGlobalOuterScope = state.Item3;

                    return @this.DoInstantiate(@this, capturedQualifier, capturedGlobalOuterScope);
                });
        }

        /// <summary>
        /// Same as <see cref="Instantiate"/>, but the result is presented with a more specific type (FileModuleLiteral)
        /// </summary>
        public FileModuleLiteral InstantiateFileModuleLiteral(ModuleRegistry moduleRegistry, QualifierValue qualifier)
        {
            return (FileModuleLiteral)Instantiate(moduleRegistry, qualifier);
        }

        /// <inheritdoc/>
        public override void AddResolvedEntry(FilePosition location, FunctionLikeExpression lambda)
        {
            lock (m_resolvedEntries)
            {
                m_resolvedEntries.Add(location, new ResolvedEntry(FullSymbol.Invalid, lambda));
            }
        }

        /// <inheritdoc/>
        public override void AddResolvedEntry(FilePosition location, ResolvedEntry entry)
        {
            lock (m_resolvedEntries)
            {
                m_resolvedEntries.Add(location, entry);
            }
        }

        /// <inheritdoc/>
        public override void AddResolvedEntry(FullSymbol fullName, ResolvedEntry entry)
        {
            lock (m_resolvedEntriesByFullName)
            {
                m_resolvedEntriesByFullName[fullName] = entry;
            }
        }

        /// <inheritdoc/>
        protected override bool TryGetResolvedEntryByFullName(FullSymbol fullName, out ResolvedEntry resolvedEntry)
        {
            lock (m_resolvedEntriesByFullName)
            {
                return m_resolvedEntriesByFullName.TryGetValue(fullName, out resolvedEntry);
            }
        }

        private void CopyBindings(FileModuleLiteral file, QualifierValue qualifier)
        {
            // Need to copy resolved bindings from a given module.
            // This requires direct copy for all resolved symbol except namespaces.
            // In that case new uninstantiated namespace instance is created.
            if (file.m_resolvedEntries != null)
            {
                // Current instance is still under construction, so no locking is needed for m_resolvedEntries field
                // ReSharper disable once InconsistentlySynchronizedField
                m_resolvedEntries = new Dictionary<FilePosition, ResolvedEntry>();
                foreach (var kvp in file.m_resolvedEntries)
                {
                    AddResolvedEntry(
                        kvp.Key,
                        CreateResolvedEntryWithNewlyCreatedModuleIfNeeded(kvp.Value, qualifier));
                }
            }

            if (file.m_resolvedEntriesByFullName != null)
            {
                // Current instance is still under construction, so no locking is needed for m_resolvedEntriesByFullName field
                // ReSharper disable once InconsistentlySynchronizedField
                m_resolvedEntriesByFullName = new Dictionary<FullSymbol, ResolvedEntry>();
                foreach (var kvp in file.m_resolvedEntriesByFullName)
                {
                    AddResolvedEntry(
                        kvp.Key,
                        CreateResolvedEntryWithNewlyCreatedModuleIfNeeded(kvp.Value, qualifier));
                }
            }
        }

        private FileModuleLiteral DoInstantiate(FileModuleLiteral module, QualifierValue qualifier, GlobalModuleLiteral outerScope)
        {
            Contract.Requires(module != null);
            Contract.Requires(qualifier != QualifierValue.Unqualified);

            Interlocked.CompareExchange(ref m_qualifier, qualifier, QualifierValue.Unqualified);

            if (m_qualifier.QualifierId == qualifier.QualifierId)
            {
                // Uninstantiated module becomes the first instance.
                return this;
            }

            // Create a new file module instance.
            var newModule = CreateInstantiatedFileModule(module.Id.Path, qualifier, outerScope, module.Package, module.m_moduleRegistry, LineMap);
            newModule.CopyBindings(module, qualifier);

            return newModule;
        }

        /// <inheritdoc />
        public override QualifierValue GetFileQualifier()
        {
            return Qualifier;
        }

        /// <inheritdoc />
        public override bool IsEmpty => m_resolvedEntries.Count == 0;

        private FileModuleLiteral GetFileModuleInstanceFromImportOrExportDeclaration(
            ModuleRegistry moduleRegistry,
            AbsolutePath referencedPath)
        {
            var importedModuleId = ModuleLiteralId.Create(referencedPath);

            // Get the uninstantiated version of this file module.
            UninstantiatedModuleInfo importedModuleInfo = moduleRegistry.GetUninstantiatedModuleInfoByModuleId(importedModuleId);

            // Evaluate qualifier if specified.
            QualifierValue qualifierValue = Qualifier;

            // Instantiate this file module according to the qualifier.
            FileModuleLiteral importedModule = importedModuleInfo.FileModuleLiteral.InstantiateFileModuleLiteral(moduleRegistry, qualifierValue);
            return importedModule;
        }

        /// <inheritdoc/>
        public override bool TryGetResolvedEntry(ModuleRegistry moduleRegistry, FilePosition location, out ResolvedEntry resolvedEntry, out FileModuleLiteral resolvedModule)
        {
            if (location.Path != Path)
            {
                // TODO:ST: instantiation is happening more than once!
                var referencedModule = GetFileModuleInstanceFromImportOrExportDeclaration(moduleRegistry, location.Path);
                return referencedModule.TryGetResolvedEntry(moduleRegistry, location, out resolvedEntry, out resolvedModule);
            }

            resolvedModule = this;
            return m_resolvedEntries.TryGetValue(location, out resolvedEntry);
        }

        internal bool TryResolveExtendedName(ImmutableContextBase context, FullSymbol enclosingName, FullSymbol requestedName, out ModuleBinding result)
        {
            // Semantically nested namespaces forms hierarchical structure, when nested namespace is stored in the outer namespace.
            // For performance reasons, file module stores all names in one big map with combined keys.
            // This means that when looking for A.B we should look at the current scope for A, then need to combine A and B
            // and look for A.B in the file module.
            // This means that extended (or partial) names are only allowed if the enclosing name is valid.
            // In this case we will combine requested name with the enclosing one and look in the current file module.
            result = null;

            if (!enclosingName.IsValid)
            {
                return false;
            }

            if (m_partialSymbolsCache.Value.TryGetValue(requestedName, out result))
            {
                return true;
            }

            var extendedFullName = enclosingName.Combine(context.FrontEndContext.SymbolTable, requestedName);

            // Now trying to resolve extended name in the current file.
            result = GetNamespaceBinding(context, extendedFullName, recurs: false);

            // If resolution was successful, saving it in the cache.
            if (result != null)
            {
                m_partialSymbolsCache.Value.GetOrAdd(requestedName, result);
                return true;
            }

            return false;
        }

        /// <inheritdoc />
        internal override void AddType(FullSymbol name, UniversalLocation location, QualifierSpaceId? qualifierSpaceId, out TypeOrNamespaceModuleLiteral module)
        {
            module = CreateTypeOrNamespaceModule(name, outerScope: this, location: location.AsLineInfo());

            Contract.Assert(qualifierSpaceId == null, "Only namespace support qualifier types");

            AddResolvedEntry(location.AsFilePosition(), new ResolvedEntry(name, module));
        }

        /// <inheritdoc />
        internal override void AddNamespace(FullSymbol name, UniversalLocation location, QualifierSpaceId? qualifierSpaceId, out TypeOrNamespaceModuleLiteral module)
        {
            module = CreateTypeOrNamespaceModule(name, outerScope: this, location: location.AsLineInfo());

            Contract.Assert(qualifierSpaceId != null, "Qualifier type should be provided for a semantic evaluation");
            m_moduleRegistry.AddUninstantiatedModuleInfo(new UninstantiatedModuleInfo(sourceFile: null, typeOrNamespaceLiteral: module, qualifierSpaceId: qualifierSpaceId.Value));

            AddResolvedEntry(location.AsFilePosition(), new ResolvedEntry(name, module));
        }

        /// <summary>
        /// If a given resolved entry points to a namespace, then new uninstantiated <see cref="TypeOrNamespaceModuleLiteral"/> will be created.
        /// </summary>
        /// <remarks>
        /// This method plays a crucial role in qualifiers implementation.
        /// In V1 the current qualifier of a nested namespace is searched by looking up until the current qualifier of the parent file
        /// module literal is found. So when instantiating a new module, its child namespaces need to be chained to it.
        /// </remarks>
        private ResolvedEntry CreateResolvedEntryWithNewlyCreatedModuleIfNeeded(ResolvedEntry resolvedEntry, QualifierValue qualifier)
        {
            if (!(resolvedEntry.Expression is TypeOrNamespaceModuleLiteral typeOrNamespace))
            {
                return resolvedEntry;
            }

            var newM = new TypeOrNamespaceModuleLiteral(typeOrNamespace.Id, qualifier, this, typeOrNamespace.Location);
            return new ResolvedEntry(FullSymbol.Invalid, newM);
        }

        /// <summary>
        /// Evaluates all named values that are declared locally and collects the evaluation tasks.
        /// </summary>
        protected override void EvaluateAllNamedValues(ImmutableContextBase immutableContext, List<Task<object>> evaluateTasks)
        {
            // For resolved modules all 'legacy' bindings are null (should not be used in any way).
            Contract.Assert(m_bindings == null);
            Contract.Assert(m_nsBindings == null);

            var context = immutableContext.ContextTree.RootContext;

            // But for this class we need to add the resolved entries information on top of that.
            foreach (var kvp in m_resolvedEntries)
            {
                // Resolved entries contains different types of symbols:
                // import * aliases, function declarations, type declarations, namespaces and variable declarations.
                // This function needs to evaluate only const bindings and should not touch anything else.
                if (kvp.Value.IsVariableDeclaration)
                {
                    var coercedQualifier = QualifierId.Invalid;

                    // Need to coerce qualifier id with a current qualifier value for top-most variable declarations.
                    // If coercion fails, then no errors are emitted, this just means that we can't evaluate this declaration
                    // under a given qualifier because it is not a part of the build goals.
                    // Consider following case:
                    // namespace X {export declare const qualifier: {n: '42'}; export const v = qualifier.n;}
                    // export const v = 42;
                    // A user should be able to evaluate this file with default qualifier.
                    // But in order to do that, nested values should be evaluated with a default qualifier.
                    if (!kvp.Value.QualifierSpaceId.IsValid || context.FrontEndContext.QualifierTable.TryCreateQualifierForQualifierSpace(
                        context.PathTable,
                        context.LoggingContext,
                        context.LastActiveModuleQualifier.QualifierId, // We are evaluating a top level entry, so the current qualifier comes from the root context
                        kvp.Value.QualifierSpaceId,
                        immutableContext.FrontEndHost.ShouldUseDefaultsOnCoercion(CurrentFileModule.Path),
                        out coercedQualifier,
                        error: out _))
                    {
                        Contract.Assert(coercedQualifier.IsValid);
                        evaluateTasks.Add(GetEvaluateResolvedEntryTaskAsync(context, coercedQualifier, kvp.Key, kvp.Value));
                    }
                }
            }
        }

        /// <summary>
        /// Returns <see cref="ModuleBinding"/>s for all resolved entries that have a name.
        /// </summary>
        public override IEnumerable<KeyValuePair<string, ModuleBinding>> GetAllBindings(Context context)
        {
            var baseBindings = base.GetAllBindings(context);
            var myBindings = m_resolvedEntries.Values.Select(entry => new KeyValuePair<string, ModuleBinding>(
                                                                 key: GetSymbolNameAsString(entry, context),
                                                                 value: ToModuleBinding(entry)));
            return baseBindings.Concat(myBindings);
        }

        /// <inheritdoc />
        public override IEnumerable<KeyValuePair<string, ModuleBinding>> GetAllBindings(SymbolTable symbolTable)
        {
            var baseBindings = base.GetAllBindings(symbolTable);
            var myBindings = m_resolvedEntries.Values.Select(entry => new KeyValuePair<string, ModuleBinding>(
                                                                 key: GetSymbolNameAsString(entry, symbolTable),
                                                                 value: ToModuleBinding(entry)));
            return baseBindings.Concat(myBindings);
        }

        private static string GetSymbolNameAsString(ResolvedEntry entry, Context context)
        {
            return entry.Function != null
                ? entry.Function.Name.ToDisplayString(context)
                : entry.SymbolName.ToDisplayString(context);
        }

        private static string GetSymbolNameAsString(ResolvedEntry entry, SymbolTable symbolTable)
        {
            return entry.Function != null
                ? entry.Function.Name.ToDisplayString(symbolTable.StringTable)
                : entry.SymbolName.ToDisplayString(symbolTable);
        }

        private static ModuleBinding ToModuleBinding(ResolvedEntry entry)
        {
            return new ModuleBinding(entry.GetValue(), Declaration.DeclarationFlags.None, entry.Location);
        }

        private Task<object> GetEvaluateResolvedEntryTaskAsync(Context context, QualifierId qualifier, FilePosition filePosition, ResolvedEntry resolvedEntry)
        {
            if (resolvedEntry.ResolverCallback != null)
            {
                return EvaluateResolverCallback(context, qualifier, filePosition, resolvedEntry);
            }

            return context.EvaluationScheduler.EvaluateValue(
                () => Task.FromResult(EvaluateResolvedEntry(context, qualifier, filePosition, resolvedEntry).Value));
        }

        private async Task<object> EvaluateResolverCallback(Context context, QualifierId qualifier, FilePosition filePosition, ResolvedEntry resolvedEntry)
        {
            var qualifierValue = QualifierValue.Create(qualifier, context.QualifierValueCache, context.FrontEndContext.QualifierTable, context.StringTable);
            var env = InstantiateFileModuleLiteral(context.ModuleRegistry, qualifierValue);

            using (var args= EvaluationStackFrame.Empty())
            {
                return await resolvedEntry.ResolverCallback(context, env, args);
            }
        }

        private EvaluationResult EvaluateResolvedEntry(Context context, QualifierId qualifier, FilePosition filePosition, ResolvedEntry resolvedEntry)
        {
            var qualifierValue = QualifierValue.Create(qualifier, context.QualifierValueCache, context.FrontEndContext.QualifierTable, context.StringTable);
            var instantiatedModule = InstantiateFileModuleLiteral(context.ModuleRegistry, qualifierValue);

            if (resolvedEntry.Thunk == null)
            {
                return instantiatedModule.EvaluateNonThunkedResolvedSymbol(context, instantiatedModule, resolvedEntry);
            }
            
            var name = resolvedEntry.ThunkContextName;
            return instantiatedModule.EvaluateByLocation(context, filePosition, name, resolvedEntry.Location);
        }
    }
}

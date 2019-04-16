// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.FrontEnd.Script.Constants;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.RuntimeModel.AstBridge;
using BuildXL.FrontEnd.Script.Tracing;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.FrontEnd.Sdk.Workspaces;
using TypeScript.Net.Types;
using LineInfo = TypeScript.Net.Utilities.LineInfo;

namespace BuildXL.FrontEnd.Script
{
    /// <summary>
    /// Resolver for DScript source kinds. It heavily relies on a <see cref="DScriptWorkspaceResolverFactory"/>
    /// to handle package configuration interpretation and determining package ownership. The factory passed at construction time must
    /// contain a registered workspace resolver that can handle source and default source kinds. This is a temporary stage while:
    /// - IResolver contains package-related logic.
    /// - The workspace API is an optional feature
    /// </summary>
    public class DScriptSourceResolver : DScriptInterpreterBase, IResolver
    {
        /// <summary>
        /// Resolver initialization states.
        /// </summary>
        protected enum State
        {
            /// <summary>
            /// Instance has been created, but resolver initialization hasn't even started yet
            /// </summary>
            Created,

            /// <summary>
            /// Resolver initialization is ongoing; some properties such as the <code>Name</code> is now available.
            /// </summary>
            ResolverInitializing,

            /// <summary>
            /// Resolver initialization has successfully completed.
            /// </summary>
            ResolverInitialized,
        }

        /// <summary>
        /// State of the resolver initialization
        /// </summary>
        protected State m_resolverState;

        /// <summary>
        /// Mappings from package id's to package locations and descriptors.
        /// </summary>
        protected ConcurrentDictionary<PackageId, Package> m_packages = new ConcurrentDictionary<PackageId, Package>(PackageIdEqualityComparer.NameOnly);

        /// <summary>
        /// DScript V2 pipeline: a map of owning modules.
        /// </summary>
        protected Dictionary<BuildXL.Utilities.ModuleId, Package> m_owningModules;

        /// <summary>
        /// Mappings package directories to lists of packages.
        /// </summary>
        /// <remarks>
        /// We allow multiple packages in a single directory, and hence the list of packages. Moreover, by construction, the packages in the same list
        /// must reside in the same directory.
        /// </remarks>
        protected ConcurrentDictionary<AbsolutePath, List<Package>> m_packageDirectories = new ConcurrentDictionary<AbsolutePath, List<Package>>();

        /// <summary>
        /// Workspace factory that are used for the parsing part of this resolver
        /// </summary>
        protected readonly DScriptWorkspaceResolverFactory WorkspaceFactory;

        private readonly SourceFileProcessingQueue<bool> m_parseQueue;

        private readonly IDecorator<Values.EvaluationResult> m_evaluationDecorator;

        private WorkspaceSourceModuleResolver m_workspaceSourceModuleResolver;

        // Cummulative evaluation results across evaluation calls. Only populated when a decorator is present, to pass it over when evaluation is finished.
        private readonly ConcurrentQueue<EvaluationResult> m_evaluationResults = new ConcurrentQueue<EvaluationResult>();

        /// <nodoc />
        public DScriptSourceResolver(
            GlobalConstants constants,
            ModuleRegistry sharedModuleRegistry,
            FrontEndHost host,
            FrontEndContext context,
            IConfiguration configuration,
            IFrontEndStatistics statistics,
            SourceFileProcessingQueue<bool> parseQueue,
            Logger logger = null,
            IDecorator<Values.EvaluationResult> evaluationDecorator = null)
            : base(constants, sharedModuleRegistry, statistics, logger, host, context, configuration)
        {
            Contract.Requires(parseQueue != null);

            m_parseQueue = parseQueue;
            m_evaluationDecorator = evaluationDecorator;
        }

        /// <nodoc/>
        public virtual async Task<bool> InitResolverAsync(IResolverSettings resolverSettings, object workspaceResolver)
        {
            Contract.Requires(resolverSettings != null);

            Contract.Assert(m_resolverState == State.Created);
            var sourceResolverSettings = resolverSettings as IDScriptResolverSettings;
            Contract.Assert(sourceResolverSettings != null, "Wrong type for resolver");

            Name = resolverSettings.Name;
            m_resolverState = State.ResolverInitializing;

            m_workspaceSourceModuleResolver = workspaceResolver as WorkspaceSourceModuleResolver;
            Contract.Assert(m_workspaceSourceModuleResolver != null, "Workspace module resolver is expected to be of source type");

            var moduleResolutionResult = await m_workspaceSourceModuleResolver.ResolveModuleAsyncIfNeeded();

            if (!moduleResolutionResult.Succeeded)
            {
                // Error should have been reported.
                return false;
            }

            m_packageDirectories = moduleResolutionResult.PackageDirectories;
            m_packages = moduleResolutionResult.Packages;

            m_owningModules = moduleResolutionResult.Packages.ToDictionary(p => p.Value.ModuleId, p => p.Value);

            m_resolverState = State.ResolverInitialized;

            return true;
        }

        /// <inheritdoc />
        public async Task<bool?> TryConvertModuleToEvaluationAsync(IModuleRegistry moduleRegistry, ParsedModule module, IWorkspace workspace)
        {
            Contract.Requires(module != null);
            Contract.Assert(m_resolverState == State.ResolverInitialized);
            Contract.Assert(m_owningModules != null, "Owning modules should not be null if the instance is initialized.");

            if (!m_owningModules.TryGetValue(module.Descriptor.Id, out Package package))
            {
                // Current resolver doesn't own a given module.
                return null;
            }
            
            var factory = CreateRuntimeModelFactory((Workspace)workspace);
            var tasks = module.Specs.Select(spec => ConvertFileToEvaluationAsync(factory, spec.Key, spec.Value, package)).ToArray();
            var allTasks = await Task.WhenAll(tasks);

            return allTasks.All(b => b);
        }

        /// <inheritdoc/>
        public async Task<bool?> TryEvaluateModuleAsync(IEvaluationScheduler scheduler, ModuleDefinition module, QualifierId qualifierId)
        {
            Contract.Requires(scheduler != null);
            Contract.Requires(module != null);
            Contract.Requires(qualifierId.IsValid);

            Contract.Assert(m_resolverState == State.ResolverInitialized);
            Contract.Assert(m_owningModules != null, "Owning modules should not be null if the instance is initialized.");

            var moduleDefinition = (ModuleDefinition)module;
            if (!m_owningModules.TryGetValue(moduleDefinition.Descriptor.Id, out Package package))
            {
                // Current resolver doesn't own the given module.
                return null;
            }

            return await DoTryEvaluateModuleAsync(scheduler, moduleDefinition, qualifierId);
        }

        /// <summary>
        /// An alternative to <see cref="InitResolverAsync(IResolverSettings, object)"/> used for testing.
        /// Instead of taking and parsing a config object, this method directly takes a list of owned packages.
        /// </summary>
        internal void InitResolverForTesting(string name, IEnumerable<Package> packages)
        {
            Contract.Assert(m_resolverState == State.Created);
            Name = name;
            m_resolverState = State.ResolverInitializing;

            m_packages = new ConcurrentDictionary<PackageId, Package>();
            m_owningModules = new Dictionary<ModuleId, Package>();
            foreach (var package in packages)
            {
                m_packages.Add(package.Id, package);
                m_owningModules.Add(package.ModuleId, package);
            }

            m_resolverState = State.ResolverInitialized;
        }

        private async Task<bool> DoTryEvaluateModuleAsync(IEvaluationScheduler scheduler, ModuleDefinition module, QualifierId qualifierId)
        {
            var qualifier = QualifierValue.Create(qualifierId, QualifierValueCache, Context.QualifierTable, Context.StringTable);

            // We don't want to evaluate the transitive closure of the import/export relationship, just the package.
            var evalJobs = module.Specs.Select(
                spec => EvaluateAsync(scheduler, spec, qualifier, asPackageEvaluation: false));

            var allTasks = await Task.WhenAll(evalJobs);

            var result = allTasks.All(t => t.Success);

            if (m_evaluationDecorator != null)
            {
                foreach (var evaluationResult in allTasks)
                {
                    m_evaluationResults.Enqueue(evaluationResult);
                }
            }

            return result;
        }

        private async Task<bool> ConvertFileToEvaluationAsync(RuntimeModelFactory factory, AbsolutePath specPath, ISourceFile sourceFile, Package package)
        {
            using (FrontEndStatistics.SpecConversion.Start(sourceFile.Path.AbsolutePath))
            {
                // Need to skip configuration files
                if (IsConfigFile(specPath) || IsPackageConfigFile(specPath))
                {
                    return true;
                }

                return await m_parseQueue.ProcessFileAsync(sourceFile, ConvertFileToEvaluationAsync);
            }

            async Task<bool> ConvertFileToEvaluationAsync(ISourceFile f)
            {
                Context.CancellationToken.ThrowIfCancellationRequested();

                var parserContext = CreateParserContext(
                    this,
                    package,
                    origin: null);

                var conversionResult = await factory.ConvertSourceFileAsync(parserContext, f);
                Contract.Assert(!conversionResult.Success || conversionResult.Module != null);

                if (conversionResult.Success)
                {
                    RegisterSuccessfullyParsedModule(conversionResult.SourceFile, conversionResult, package);

                    // TODO: should the project be registered only when the parse is successful?
                    // In the original implementation (v1) the path was added all the time.
                    package.AddParsedProject(AbsolutePath.Create(parserContext.PathTable, sourceFile.FileName));
                }

                return conversionResult.Success;
            }
        }

        /// <inheritdoc/>
        public void NotifyEvaluationFinished()
        {
            // In case a decorator is present, we notify evaluation is over
            if (m_evaluationDecorator != null)
            {
                var success = m_evaluationResults.All(evaluationResult => evaluationResult.Success);
                var contexts = m_evaluationResults.SelectMany(evaluationResult => evaluationResult.Contexts);
                m_evaluationDecorator.NotifyEvaluationFinished(success, contexts);
            }
        }

        /// <inheritdoc/>
        public void LogStatistics()
        {
            Logger.ContextStatistics(
                Context.LoggingContext,
                Name,
                Statistics.ContextTrees,
                Statistics.Contexts);

            Logger.ArrayEvaluationStatistics(
                Context.LoggingContext,
                Name,
                Statistics.EmptyArrays,
                Statistics.ArrayEvaluations,
                Statistics.AlreadyEvaluatedArrays);

            Logger.GlobStatistics(
                Context.LoggingContext,
                Name,
                (long)TimeSpan.FromTicks(Interlocked.Read(ref Statistics.TotalGlobTimeInTicks)).TotalMilliseconds);

            LogMethodInvocationStatistics();
        }

        private void LogMethodInvocationStatistics()
        {
            var topMethodCalls = Statistics.FunctionInvocationStatistics.GetTopCounters(10, Context.StringTable);
            foreach (var method in topMethodCalls)
            {
                string totalDuration = string.Format("{0,12:0,000}", (long)method.Duration.TotalMilliseconds);
                Logger.MethodInvocationCountStatistics(Context.LoggingContext, Name, method.MethodName, method.Count, totalDuration);
            }
        }

        /// <summary>
        /// Evaluates a file given the file path and a qualifier.
        /// </summary>
        /// <remarks>
        /// If the evaluation is part of package evaluation, then all files in the transitive closure of
        /// local import/export relation will be evaluated as well.
        /// </remarks>
        private async Task<EvaluationResult> EvaluateAsync(IEvaluationScheduler scheduler, AbsolutePath fullPath, QualifierValue qualifier, bool asPackageEvaluation)
        {
            Contract.Requires(fullPath.IsValid);
            Contract.Requires(qualifier != null);

            // Get an uninstantiated module.
            if (!SharedModuleRegistry.TryGetUninstantiatedModuleInfoByPath(fullPath, out UninstantiatedModuleInfo moduleInfo))
            {
                Logger.ReportSourceResolverFailEvaluateUnregisteredFileModule(Context.LoggingContext, Name, fullPath.ToString(Context.PathTable));
                return CreateResult(false);
            }

            // If this spec belongs to a V1 module, then coercion happens at this point, since in V1 the qualifier space is always defined at the file level
            // and we want an explicit failure if coercion fails, to keep V1 modules back compat
            // Otherwise, we don't coerce, since coercion will happen on a namespace level in FileModuleLiteral.EvaluateAllNamedValues
            if (!FrontEndHost.SpecBelongsToImplicitSemanticsModule(fullPath))
            {
                // Coerce qualifier.
                if (!qualifier.TryCoerce(
                    moduleInfo.QualifierSpaceId,
                    Context.QualifierTable,
                    QualifierValueCache,
                    Context.PathTable,
                    Context.StringTable,
                    Context.LoggingContext,
                    out QualifierValue coercedQualifier,
                    default(LineInfo),
                    FrontEndHost.ShouldUseDefaultsOnCoercion(moduleInfo.ModuleLiteral.Path),
                    fullPath))
                {
                    string qualifierName = Context.QualifierTable.GetQualifier(qualifier.QualifierId).ToDisplayString(Context.StringTable);
                    string qualifierSpace =
                        Context.QualifierTable.GetQualifierSpace(moduleInfo.QualifierSpaceId).ToDisplayString(Context.StringTable);

                    Logger.ReportQualifierCannotBeCoarcedToQualifierSpace(
                        Context.LoggingContext,
                        new Location
                        {
                            // Ideally the referencing location is used for this error, but that is a big replumbing effort.
                            // Task 615531
                            File = fullPath.ToString(Context.PathTable),
                        },
                        qualifierName,
                        qualifierSpace);
                    return CreateResult(false);
                }

                qualifier = coercedQualifier;
            }

            // Instantiate module with the coerced qualifier.
            var module = InstantiateModule(moduleInfo.FileModuleLiteral, qualifier);

            // Create an evaluation context tree and root context.
            using (var contextTree = CreateContext(module, scheduler, m_evaluationDecorator, CreateEvaluatorConfiguration(), FileType.Project))
            using (FrontEndStatistics.SpecEvaluation.Start(fullPath.ToString(Context.PathTable)))
            {
                var context = contextTree.RootContext;

                // Evaluate module.
                var moduleTracker = VisitedModuleTracker.Create(IsBeingDebugged);
                var mode = asPackageEvaluation ? ModuleEvaluationMode.LocalImportExportTransitive : ModuleEvaluationMode.None;
                var success = await module.EvaluateAllAsync(context, moduleTracker, mode);

                return CreateResult(success, moduleTracker);
            }
        }

        private EvaluatorConfiguration CreateEvaluatorConfiguration()
        {
            return new EvaluatorConfiguration(
                FrontEndConfiguration.TrackMethodInvocations(),
                TimeSpan.FromSeconds(FrontEndConfiguration.CycleDetectorStartupDelay()));
        }

        /// <summary>
        /// Evaluates a single package, and all projects that the package owns.
        /// </summary>
        private IReadOnlyList<Task<EvaluationResult>> EvaluatePackage(IEvaluationScheduler scheduler, Package package, QualifierValue qualifier)
        {
            Contract.Requires(package != null);
            Contract.Requires(qualifier != null);

            var projectsToEvaluate = GetProjectsOfPackage(package);
            var shouldEvaluateLocalTransitive = ShouldEvaluateLocalTransitivePackage(package);

            // Note that, if package does not explicitly specifies the projects that it owns, then
            // we only evaluate the package's main file, and rely on the import/export relation to evaluate all
            // specs in the package. Another alternative is to glob all projects in the package's cone, and
            // evaluate those projects as well. Globbing involves IO, and can be expensive in a spinning disk.
            // For a consideration, WDG may only have one package, and may not specifiy all projects that the package
            // owns. Globbing the entire WDG tree will be very costly.
            return projectsToEvaluate.Select(project => EvaluateAsync(scheduler, project, qualifier, asPackageEvaluation: shouldEvaluateLocalTransitive)).ToList();
        }

        [Pure]
        private bool IsPackageConfigFile(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);

            var name = path.GetName(Context.PathTable).ToString(Context.StringTable);
            return ExtensionUtilities.IsModuleConfigurationFile(name);
        }

        /// <summary>
        /// Gets projects owned by a package.
        /// </summary>
        private static IReadOnlyList<AbsolutePath> GetProjectsOfPackage(Package package)
        {
            Contract.Requires(package != null);

            var projects = new List<AbsolutePath>(1)
            {
                // Evaluate package's main file.
                package.Path,
            };

            if (package.DescriptorProjects != null)
            {
                // If package explicitly specifies the projects that it owns, then evaluate those projects as well.
                projects.AddRange(package.DescriptorProjects);
            }

            return projects;
        }

        private static bool ShouldEvaluateLocalTransitivePackage(Package package)
        {
            Contract.Requires(package != null);

            // Do local transitive evaluation if only the main file of package is specified.
            return package.DescriptorProjects == null;
        }

        private EvaluationResult CreateResult(bool success, ModuleLiteral module = null, ContextTree tree = null)
        {
            if (IsBeingDebugged)
            {
                var contexts = tree != null && module != null ? new[] { new ModuleAndContext(module, tree) } : CollectionUtilities.EmptyArray<ModuleAndContext>();
                return new EvaluationResult(success, contexts);
            }

            return new EvaluationResult(success, CollectionUtilities.EmptyArray<ModuleAndContext>());
        }

        private static EvaluationResult CreateResult(bool success, VisitedModuleTracker tracker)
        {
            return new EvaluationResult(success, tracker.GetVisitedModules());
        }

        private sealed class EvaluationResult
        {
            internal EvaluationResult(bool success, IEnumerable<IModuleAndContext> contexts)
            {
                Contract.Requires(contexts != null);

                Success = success;
                Contexts = contexts.ToArray();
            }

            internal bool Success { get; }

            internal IEnumerable<IModuleAndContext> Contexts { get; }
        }
    }
}

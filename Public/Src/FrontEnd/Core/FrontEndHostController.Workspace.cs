// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Tasks;
using BuildXL.FrontEnd.Workspaces;
using BuildXL.FrontEnd.Workspaces.Core;
using JetBrains.Annotations;
using BuildXL.Utilities.Configuration;
using BuildXL.FrontEnd.Core.Incrementality;
using BuildXL.FrontEnd.Sdk;
using TypeScript.Net.DScript;
using TypeScript.Net.Types;
using TypeScript.Net.Utilities;
using static BuildXL.Utilities.FormattableStringEx;
using CancellationToken = System.Threading.CancellationToken;
using Diagnostic = TypeScript.Net.Diagnostics.Diagnostic;

namespace BuildXL.FrontEnd.Core
{
    public partial class FrontEndHostController
    {
        /// <summary>
        /// Result of the conversion phase.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1815:ShouldOverrideEquals", Justification = "Never used in comparisons")]
        public readonly struct ConversionResult
        {
            /// <summary>Whether it succeeded.</summary>
            public bool Succeeded { get; }

            /// <summary>How many modules were converted (not available in V1).</summary>
            public int NumberOfModulesConverted { get; }

            /// <summary>How many specs were converted.</summary>
            public int NumberOfSpecsConverted { get; }

            /// <summary>Instance which denotes a failed conversion.</summary>
            public static ConversionResult Failed { get; } = default(ConversionResult);

            /// <nodoc/>
            public ConversionResult(bool succeeded, int numModulesConverted, int numSpecsConverted)
            {
                Succeeded = succeeded;
                NumberOfModulesConverted = numModulesConverted;
                NumberOfSpecsConverted = numSpecsConverted;
            }
        }

        /// <summary>
        /// Directory for front end cache.
        /// </summary>
        private AbsolutePath m_frontEndCacheDirectory;

        private readonly DScriptWorkspaceResolverFactory m_workspaceResolverFactory;

        private readonly bool m_collectMemoryAsSoonAsPossible;

        private LoggingContext LoggingContext => FrontEndContext.LoggingContext;

        /// <summary>
        /// Converts workspace and registers all translated files in the module registry.
        /// </summary>
        public async Task<ConversionResult> ConvertWorkspaceToEvaluationAsync(Workspace workspace)
        {
            // Prelude module shouldn't be converted.
            var tasks = workspace
                .SpecModules
                .Select(m => ConvertModuleToEvaluationAsync(m, workspace))
                .ToArray();
            var task = Task.WhenAll(tasks);

            var numSpecs = workspace.SpecModules.Sum(m => m.Specs.Count);

            var succeeded = await WithConversionProgressReportingAsync(numSpecs, task);

            return new ConversionResult(succeeded, workspace.SpecModules.Count, numSpecs);
        }

        /// <summary>
        /// Builds and filters the worksapce.
        /// </summary>
        [System.Diagnostics.ContractsLight.Pure]
        internal Workspace DoPhaseBuildWorkspace(IConfiguration configuration, FrontEndEngineAbstraction engineAbstraction, EvaluationFilter evaluationFilter)
        {
            if (!TryGetWorkspaceProvider(configuration, out var workspaceProvider, out var failures))
            {
                var workspaceConfiguration = GetWorkspaceConfiguration(configuration);

                return Workspace.Failure(workspaceConfiguration: workspaceConfiguration, failures: failures.ToArray());
            }

            var result = TaskUtilities.WithCancellationHandlingAsync(
                FrontEndContext.LoggingContext,
                BuildAndFilterWorkspaceAsync(workspaceProvider, engineAbstraction, evaluationFilter),
                m_logger.FrontEndBuildWorkspacePhaseCanceled,
                GetOrCreateComputationCancelledWorkspace(workspaceProvider),
                FrontEndContext.CancellationToken).GetAwaiter().GetResult();

            ReportWorkspaceParsingAndBindingErrorsIfNeeded(result);

            return result;
        }

        /// <summary>
        /// Compute semantic information for a workspace.
        /// </summary>
        /// <remarks>
        /// The result is never null
        /// </remarks>
        [System.Diagnostics.ContractsLight.Pure]
        internal Workspace DoPhaseAnalyzeWorkspace(IConfiguration configuration, Workspace workspace)
        {
            Contract.Requires(configuration != null);
            Contract.Requires(workspace != null);

            var result =
                TaskUtilities.WithCancellationHandlingAsync(
                FrontEndContext.LoggingContext,
                    WithAnalysisProgressReportingAsync(
                        workspace.AllSpecCount,
                        ComputeSemanticWorkspaceAsync(workspace, workspace.WorkspaceConfiguration)),
                    m_logger.FrontEndWorkspaceAnalysisPhaseCanceled,
                    GetOrCreateComputationCancelledWorkspace(workspace.WorkspaceProvider),
                    FrontEndContext.CancellationToken).GetAwaiter().GetResult();

            ReportWorkspaceSemanticErrorsIfNeeded(result);

            return result;
        }

        /// <summary>
        /// Builds and filters the worksapce.
        /// </summary>
        /// <remarks>
        /// This method not just builds the workspace from scratch, but it also tries to compute it in an efficient way.
        /// If there is a front end snapshot from the previous BuildXL run and the engine gives us a set of changed files,
        /// then we can build a filtered workspace based on the old spec-2-spec map without parsing the entire world.
        /// </remarks>
        private async Task<Workspace> BuildAndFilterWorkspaceAsync(IWorkspaceProvider workspaceProvider, FrontEndEngineAbstraction engineAbstraction, EvaluationFilter evaluationFilter)
        {
            // this step downloads nugets too, and that's why we want to do it outside of the progress reporting block below
            Possible<WorkspaceDefinition> workspaceDefinition = await TryGetWorkspaceDefinitionAsync(workspaceProvider);

            if (!workspaceDefinition.Succeeded)
            {
                return Workspace.Failure(workspaceProvider, workspaceProvider.Configuration, workspaceDefinition.Failure);
            }

            return await WithWorkspaceProgressReportingAsync(
                numSpecs: workspaceDefinition.Result.SpecCount,
                task: BuildAndFilterWorkspaceAsync(workspaceDefinition.Result, workspaceProvider, engineAbstraction, evaluationFilter));
        }

        private async Task<bool> WithConversionProgressReportingAsync(int totalSpecs, Task<bool[]> task)
        {
            var counter = m_frontEndStatistics.SpecConversion;
            var results = await TaskUtilities.AwaitWithProgressReporting(
                task,
                period: EvaluationProgressReportingPeriod,
                action: (elapsed) =>
                {
                    m_logger.FrontEndConvertPhaseProgress(FrontEndContext.LoggingContext, counter.Count, totalSpecs);
                    NotifyProgress(WorkspaceProgressEventArgs.Create(ProgressStage.Conversion, counter.Count, totalSpecs));
                });

            return results.All(t => t);
        }

        private Task<Workspace> WithWorkspaceProgressReportingAsync(int? numSpecs, Task<Workspace> task)
        {
            var numParseTotal = numSpecs?.ToString(CultureInfo.InvariantCulture) ?? "?";

            var counter = m_frontEndStatistics.SpecBinding;
            return TaskUtilities.AwaitWithProgressReporting(
                task,
                EvaluationProgressReportingPeriod,
                (elapsed) =>
                {
                    m_logger.FrontEndWorkspacePhaseProgress(FrontEndContext.LoggingContext, counter.Count, numParseTotal);
                    NotifyProgress(WorkspaceProgressEventArgs.Create(ProgressStage.Parse, counter.Count, numSpecs));
                });
        }

        private void NotifyProgress(WorkspaceProgressEventArgs args)
        {
            m_frontEndStatistics.WorkspaceProgress?.Invoke(this, args);
        }

        private Task<Workspace> WithAnalysisProgressReportingAsync(int numSpecsTotal, Task<Workspace> task)
        {
            var counter = m_frontEndStatistics.SpecTypeChecking;
            return TaskUtilities.AwaitWithProgressReporting(
                task,
                EvaluationProgressReportingPeriod,
                (elapsed) =>
                {
                    m_logger.FrontEndWorkspaceAnalysisPhaseProgress(FrontEndContext.LoggingContext, counter.Count, numSpecsTotal);
                    NotifyProgress(WorkspaceProgressEventArgs.Create(ProgressStage.Analysis, counter.Count, numSpecsTotal));
                });
        }

        private async Task<Workspace> BuildAndFilterWorkspaceAsync(WorkspaceDefinition workspaceDefinition, IWorkspaceProvider workspaceProvider, FrontEndEngineAbstraction engineAbstraction, EvaluationFilter evaluationFilter)
        {
            // First, trying to filter workspace based on information from the previous run
            var possibleFilteredWorkspace = await TryCreateFilteredWorkspaceAsync(workspaceDefinition, workspaceProvider, engineAbstraction, evaluationFilter);
            if (!possibleFilteredWorkspace.Succeeded)
            {
                // Error was already logged
                return Workspace.Failure(workspaceProvider, workspaceProvider.Configuration, possibleFilteredWorkspace.Failure);
            }

            // If the filtered workspace is not null, just return it.
            // Otherwise falling back to the full parse mode.
            if (possibleFilteredWorkspace.Result != null)
            {
                return possibleFilteredWorkspace.Result;
            }

            // "Incremental" workspace construction has failed, but we still can try to use module filter to build a smaller workspace.
            if (evaluationFilter.ModulesToResolve.Count != 0)
            {
                var filteredDefinition = this.ApplyModuleFilter(workspaceDefinition, evaluationFilter.ModulesToResolve);
                return await workspaceProvider.CreateWorkspaceAsync(filteredDefinition, userFilterWasApplied: true);
            }

            Logger.BuildingFullWorkspace(LoggingContext);
            return await workspaceProvider.CreateWorkspaceAsync(workspaceDefinition, userFilterWasApplied: false);
        }

        /// <nodoc />
        internal void FilterWorkspace(Workspace workspace, EvaluationFilter evaluationFilter)
        {
            if (!evaluationFilter.CanPerformPartialEvaluationScript(PrimaryConfigFile))
            {
                return;
            }

            using (var sw = Watch.Start())
            {
                int originalCount = workspace.SpecCount;

                // WorkspaceFilter updates the existing workspace instead of creating brand new one.
                // This is crucial to avoid redundant type checking required for semantic workspace creation.
                var filter = new WorkspaceFilter(FrontEndContext.PathTable);
                workspace.FilterWorkspace(filter.FilterForConversion(workspace, evaluationFilter));

                Logger.WorkspaceFiltered(LoggingContext, workspace.SpecCount, originalCount, sw.ElapsedMilliseconds);
            }
        }

        /// <summary>
        /// Filters a <paramref name="workspace"/> using <paramref name="modules"/>.
        /// </summary>
        /// <remarks>
        /// Currently only module filter is supported.
        /// </remarks>
        private WorkspaceDefinition ApplyModuleFilter(WorkspaceDefinition workspace, IReadOnlyList<StringId> modules)
        {
            using (var sw = Watch.Start())
            {
                // WorkspaceFilter updates the existing workspace instead of creating brand new one.
                // This is crucial to avoid redundant type checking required for semantic workspace creation.
                var filter = new WorkspaceFilter(FrontEndContext.PathTable);
                WorkspaceDefinition filteredDefinition = filter.ApplyModuleFilter(workspace, modules);

                Logger.WorkspaceDefinitionFilteredBasedOnModuleFilter(LoggingContext, 
                    workspace.ModuleCount - filteredDefinition.ModuleCount, 
                    workspace.SpecCount - filteredDefinition.SpecCount, 
                    workspace.ModuleCount, 
                    filteredDefinition.ModuleCount, 
                    sw.ElapsedMilliseconds);
                return filteredDefinition;
            }
        }

        private void SaveFileToFileReport(Workspace workspace)
        {
            Contract.Assert(FrontEndConfiguration.FileToFileReportDestination != null);

            var reportDestination = FrontEndConfiguration.FileToFileReportDestination.Value;
            Logger.MaterializingFileToFileDepdencyMap(LoggingContext, reportDestination.ToString(FrontEndContext.PathTable));

            try
            {
                File2FileDependencyGenerator.GenerateAndSaveFile2FileDependencies(FrontEndContext.PathTable, reportDestination, workspace);
            }
            catch (BuildXLException ex)
            {
                Logger.ErrorMaterializingFileToFileDepdencyMap(LoggingContext, ex.LogEventErrorCode, ex.LogEventMessage);
            }
        }

        private void SaveFrontEndSnapshot(IWorkspaceBindingSnapshot snapshot)
        {
            Analysis.IgnoreResult(
                Task.Run(
                    () => { FrontEndArtifactManager.SaveFrontEndSnapshot(snapshot); }),
                justification: "fire and forget"
            );
        }

        /// <summary>
        /// Tries to create a filtered workspace based on a front-end snapshot from the previous BuildXL invocation.
        /// </summary>
        /// <returns>
        /// * Possibke&lt;ValidConstructedWorkspace&gt; when the workspace was successfully constructed.
        /// * Possible&lt;null&gt; when the snapshot was unaiable.
        /// * Failure when the snapshot was available but parsing failed.
        /// </returns>
        private async Task<Possible<Workspace>> TryCreateFilteredWorkspaceAsync(Possible<WorkspaceDefinition> workspaceDefinition, IWorkspaceProvider workspaceProvider, FrontEndEngineAbstraction engineAbstraction, EvaluationFilter evaluationFilter)
        {
            if (!FrontEndConfiguration.ConstructAndSaveBindingFingerprint())
            {
                Logger.FailToReuseFrontEndSnapshot(
                    LoggingContext,
                    "Binding fingerprint is disabled. Please use 'constructAndSaveBindingFingerprint' option to turn it on");
                return default(Possible<Workspace>);
            }

            // If a filter cannot be performed and public facade + AST is not to be used, then there is no point in continuing and we can
            // go to full mode
            if (!evaluationFilter.CanPerformPartialEvaluationScript(PrimaryConfigFile) && !CanUseSpecPublicFacadeAndAst())
            {
                var message = !CanUseSpecPublicFacadeAndAst()
                    ? "Engine state was not reloaded"
                    : "User filter was not specified";
                Logger.FailToReuseFrontEndSnapshot(LoggingContext, message);
                return default(Possible<Workspace>);
            }

            var changedFiles = engineAbstraction.GetChangedFiles()?.ToList();

            if (changedFiles == null)
            {
                Logger.FailToReuseFrontEndSnapshot(LoggingContext, "Change journal is not available");
                return default(Possible<Workspace>);
            }

            using (var sw = Watch.Start())
            {
                // We're potentially in incremental mode.
                var filteredDefinitionResult = await TryFilterWorkspaceDefinitionIncrementallyAsync(
                    changedFiles,
                    workspaceProvider,
                    workspaceDefinition.Result,
                    evaluationFilter);

                if (filteredDefinitionResult.Failed)
                {
                    return filteredDefinitionResult.Failure;
                }

                if (filteredDefinitionResult.Filtered)
                {
                    var filteredDefinition = filteredDefinitionResult.FilteredDefinition;
                    Logger.WorkspaceDefinitionFiltered(
                        LoggingContext,
                        filteredDefinition.SpecCount,
                        workspaceDefinition.Result.SpecCount,
                        sw.ElapsedMilliseconds);

                    // TODO: with C# 7, use tuple instead of changing the workspace to carry the information about the filtering.
                    return await workspaceProvider.CreateWorkspaceAsync(filteredDefinition, userFilterWasApplied: true);
                }
            }

            return default(Possible<Workspace>);
        }

        private async Task<Possible<WorkspaceDefinition>> TryGetWorkspaceDefinitionAsync(IWorkspaceProvider workspaceProvider)
        {
            Possible<WorkspaceDefinition> workspaceDefinition;
            using (var sw = Watch.Start())
            {
                workspaceDefinition = await workspaceProvider.GetWorkspaceDefinitionForAllResolversAsync();
                if (workspaceDefinition.Succeeded)
                {
                    var configurationFiles = workspaceProvider.GetConfigurationModule()?.Specs.Count ?? 0;
                    Logger.WorkspaceDefinitionCreated(
                        LoggingContext,
                        workspaceDefinition.Result.ModuleCount,
                        workspaceDefinition.Result.SpecCount,
                        configurationFiles,
                        sw.ElapsedMilliseconds);
                }
            }

            return workspaceDefinition;
        }

        /// <summary>
        /// Helper struct used by <see cref="TryFilterWorkspaceDefinitionIncrementallyAsync(List{string}, IWorkspaceProvider, WorkspaceDefinition, EvaluationFilter)"/>.
        /// </summary>
        /// <remarks>
        /// This struct is effectively the following union type:
        /// Error | FilteredWorkspace | CantFilterWorkspace.
        /// </remarks>
        private readonly struct FilteredWorkspaceDefinition
        {
            /// <summary>
            /// Error occur during workspace filtering (for instance, error occurred when the changed spec was parsed).
            /// </summary>
            public bool Failed => Failure != null;

            /// <summary>
            /// True when the <see cref="FilteredDefinition"/> was filtered based on the spec-2-spec map from the previous invocation.
            /// </summary>
            public bool Filtered
            {
                get
                {
                    Contract.Requires(!Failed);
                    return FilteredDefinition != null;
                }
            }

            /// <summary>
            /// The workspace definition that was filtered based on the spec-2-spec map from the previous invocation.
            /// </summary>
            [CanBeNull]
            public WorkspaceDefinition FilteredDefinition { get; }

            /// <summary>
            /// Failure that occur during workspace filtering.
            /// </summary>
            [CanBeNull]
            public Failure Failure { get; }

            /// <summary>
            /// Constructor for a failure case.
            /// </summary>
            private FilteredWorkspaceDefinition(Failure failure, WorkspaceDefinition workspaceDefinition)
                : this()
            {
                Failure = failure;
                FilteredDefinition = workspaceDefinition;
            }

            /// <nodoc />
            public static FilteredWorkspaceDefinition Error(Failure failure)
            {
                Contract.Requires(failure != null);
                return new FilteredWorkspaceDefinition(failure, null);
            }

            /// <nodoc />
            public static FilteredWorkspaceDefinition CanNotFilter()
            {
                return new FilteredWorkspaceDefinition(null, null);
            }

            /// <nodoc />
            public static FilteredWorkspaceDefinition Filter(WorkspaceDefinition workspaceDefinition)
            {
                Contract.Requires(workspaceDefinition != null);
                return new FilteredWorkspaceDefinition(null, workspaceDefinition);
            }
        }

        /// <summary>
        /// Tries to filter a given workspace definition by reusing information from the previous BuildXL invocation.
        /// </summary>
        /// <returns>
        /// 1. Failure if the error occurred during parsing/binding one of the changed specs.
        /// 2. Result(null) when the filtering failed due to symbols mismatch or due to another reason.
        /// 3. Result(WorkspaceDefinition) when the filtering succeeded.
        /// </returns>
        /// <remarks>
        /// If the previous binding information can be reused, then the set of specs that are safe to use as public facades + serialized AST
        /// are identified as well
        /// </remarks>
        private async Task<FilteredWorkspaceDefinition> TryFilterWorkspaceDefinitionIncrementallyAsync(
            List<string> changedFiles,
            IWorkspaceProvider workspaceProvider,
            WorkspaceDefinition workspaceDefinition,
            EvaluationFilter evaluationFilter)
        {
            Logger.TryingToReuseFrontEndSnapshot(LoggingContext);

            // TODO: potentially, we could check the number of changes compared to the workspace definition size.
            // If the number of changes is too big, maybe we should go into the full parse mode.
            // But we need to check the perf implications before making this decision.
            var changedSpecs = changedFiles.Select(
                p =>
                {
                    var fullPath = AbsolutePath.Create(FrontEndContext.PathTable, p);
                    var containingModule = workspaceDefinition.TryGetModuleDefinition(fullPath);
                    return new SpecWithOwningModule(fullPath, containingModule);
                }).ToArray();

            // Need to check if the spec does not belong to the current workspace
            // or the changed spec belongs to the prelude.
            foreach (var changedSpec in changedSpecs)
            {
                if (changedSpec.OwningModule == null)
                {
                    Logger.FailToReuseFrontEndSnapshot(
                        LoggingContext,
                        I($"Changed spec file '{changedSpec.Path.ToString(FrontEndContext.PathTable)}' is not part of the computed workspace."));
                    return FilteredWorkspaceDefinition.CanNotFilter();
                }

                if (changedSpec.OwningModule.Descriptor == workspaceDefinition.PreludeModule.Descriptor)
                {
                    Logger.FailToReuseFrontEndSnapshot(
                        LoggingContext,
                        I($"Changed spec file '{changedSpec.Path.ToString(FrontEndContext.PathTable)}' is part of the prelude."));
                    return FilteredWorkspaceDefinition.CanNotFilter();
                }
            }

            // Getting the snapshot from the previous run.

            // Binding snapshot contains all the specs as well as all the configuration files.
            // Need to adjust the count.
            var expectedNumberOfSpecs = workspaceDefinition.SpecCount + (workspaceProvider.GetConfigurationModule()?.Specs.Count ?? 0);
            var snapshot = FrontEndArtifactManager.TryLoadFrontEndSnapshot(expectedNumberOfSpecs);
            if (snapshot == null)
            {
                // The error message was already logged.
                return FilteredWorkspaceDefinition.CanNotFilter();
            }

            // Parsing and binding all the changed specs.
            var possibleParseResult = await workspaceProvider.ParseAndBindSpecsAsync(changedSpecs);
            var firstFailure = LogParseOrBindingErrorsIfAny(possibleParseResult);
            if (firstFailure != null)
            {
                // This is actual failure.
                // Instead of switching to the full mode, we can actually stop here.
                return FilteredWorkspaceDefinition.Error(firstFailure);
            }

            // Snapshot is valid and parse/binding is completed successfully.
            var snapshotState = GetSnapshotReuseState(possibleParseResult, snapshot);

            if (snapshotState.State == SnapshotState.NoMatch)
            {
                // NoMatch is returned if the snapshot is unavailable.
                if (snapshotState.SpecsWithIncompatiblePublicSurface.Count != 0)
                {
                    Logger.FailToReuseFrontEndSnapshot(
                        LoggingContext,
                        I($"Spec file '{snapshotState.SpecsWithIncompatiblePublicSurface.First().Path.AbsolutePath}' changed its binding symbols."));
                }

                return FilteredWorkspaceDefinition.CanNotFilter();
            }

            // Changed file could get different symbols.
            // Need to re-save it within the front-end snapshot.
            UpdateAndSaveSnapshot(possibleParseResult, snapshot);

            var snapshotProvider = new SnapshotBasedSpecProvider(snapshot);

            // Now we know exactly which are all the files that need to go through parsing/type checking/AST conversion. So we
            // inform that to the artifact manager so the public surface and AST serialization
            // can be resued for the rest, if available.
            // Observe these set of files are not reflecting a potential user filter, but that's fine. If there is a dirty spec
            // that is outside of the filter, that spec won't be requested by the workspace anyway
            NotifyDirtySpecsForPublicFacadeAndAstReuse(
                snapshotProvider,
                workspaceDefinition,
                changedSpecs.Select(f => f.Path).ToList());

            // The fingerprints for all changed specs are still the same,
            // so we can filter the workspace definition provided that the filter allows it.
            if (snapshotState.State == SnapshotState.FullMatch)
            {
                var filter = new WorkspaceFilter(FrontEndContext.PathTable);
                var filteredWorkspace = evaluationFilter.CanPerformPartialEvaluationScript(PrimaryConfigFile)
                    ? filter.FilterWorkspaceDefinition(workspaceDefinition, evaluationFilter, snapshotProvider)
                    : workspaceDefinition.Modules;

                return FilteredWorkspaceDefinition.Filter(new WorkspaceDefinition(filteredWorkspace, workspaceDefinition.PreludeModule));
            }

            // Specs are not the same, but we would be able to load public facades for all unaffected specs.
            var dirtySpecNames = string.Join(
                ", ",
                snapshotState.SpecsWithTheSamePublicSurface.Take(10).Select(p => Path.GetFileName(p.Path.AbsolutePath)));

            Logger.FailedToFilterWorkspaceDefinition(
                LoggingContext,
                I($"{dirtySpecNames} changed one or more declarations."));

            return FilteredWorkspaceDefinition.CanNotFilter();
        }

        /// <summary>
        /// Computes the transitive closure of all dependents of the changed specs and report them as 'dirty' to the artifact manager,
        /// so they are not replaced by their public facade version and their serialized AST is not used
        /// </summary>
        private void NotifyDirtySpecsForPublicFacadeAndAstReuse(ISpecDependencyProvider snapshotProvider, WorkspaceDefinition workspaceDefinition, IReadOnlyList<AbsolutePath> changedSpecs)
        {
            if (CanUseSpecPublicFacadeAndAst())
            {
                var changedSpecsWithDependents = snapshotProvider.ComputeReflectiveClosureOfDependentFiles(changedSpecs);
                FrontEndArtifactManager.NotifySpecsCannotBeUsedAsFacades(changedSpecsWithDependents);
                var requiredSpecs = snapshotProvider.ComputeReflectiveClosureOfDependencyFiles(changedSpecsWithDependents);
                Logger.ReportDestructionCone(LoggingContext, changedSpecs.Count, changedSpecsWithDependents.Count, requiredSpecs.Count, workspaceDefinition.SpecCount);
            }
        }

        private void UpdateAndSaveSnapshot(Possible<ISourceFile>[] parseResults, IWorkspaceBindingSnapshot snapshot)
        {
            if (!FrontEndConfiguration.ConstructAndSaveBindingFingerprint())
            {
                return;
            }

            foreach (var sourceFile in parseResults)
            {
                ISourceFile source = sourceFile.Result;
                Contract.Assert(source.BindingSymbols != null, "source.BindingSymbols != null");

                snapshot.UpdateBindingFingerprint(
                    source.GetAbsolutePath(FrontEndContext.PathTable),
                    source.BindingSymbols.ReferencedSymbolsFingerprint,
                    source.BindingSymbols.DeclaredSymbolsFingerprint);
            }

            SaveFrontEndSnapshot(snapshot);

        }

        /// <summary>
        /// State of the snapshot compared to the current state of the workspace.
        /// </summary>
        public enum SnapshotState
        {
            /// <summary>
            /// The workspace perfectly matches the snapshot.
            /// </summary>
            FullMatch,

            /// <summary>
            /// The workspace is dirty: there are some changes in the implementation details of the file like in in a method body, in object literal etc.
            /// </summary>
            PublicSurfaceMatch,

            /// <summary>
            /// The snapshot is invalid compared to the current state of the workspace.
            /// Public/internal surface of one or more spec has changed.
            /// </summary>
            NoMatch,
        }

#pragma warning disable CA1815 // Override equals and operator equals on value types
        /// <summary>
        /// Result of a snapshot validation.
        /// </summary>
        public readonly struct CanReuseSnapshotResult
#pragma warning restore CA1815 // Override equals and operator equals on value types
        {
            /// <summary>
            /// Snapshot state.
            /// </summary>
            public SnapshotState State { get; }

            /// <summary>
            /// List of dirty specs, i.e. changed specs with unchanged declaration fingerprint.
            /// </summary>
            [NotNull]
            public List<ISourceFile> SpecsWithTheSamePublicSurface { get; }

            /// <summary>
            /// List of incompatible specs, i.e. changed specs with different declaration fingerprint.
            /// </summary>
            [NotNull]
            public List<ISourceFile> SpecsWithIncompatiblePublicSurface { get; }

            /// <nodoc />
            public CanReuseSnapshotResult(List<ISourceFile> specsWithTheSamePublicSurface, List<ISourceFile> specsWithIncompatiblePublicSurface)
                : this()
            {
                Contract.Requires(specsWithTheSamePublicSurface != null);
                Contract.Requires(specsWithIncompatiblePublicSurface != null);

                SpecsWithTheSamePublicSurface = specsWithTheSamePublicSurface;
                SpecsWithIncompatiblePublicSurface = specsWithIncompatiblePublicSurface;

                if (specsWithIncompatiblePublicSurface.Count != 0)
                {
                    State = SnapshotState.NoMatch;
                }
                else if (specsWithTheSamePublicSurface.Count != 0)
                {
                    State = SnapshotState.PublicSurfaceMatch;
                }
                else
                {
                    State = SnapshotState.FullMatch;
                }
            }

            private CanReuseSnapshotResult(SnapshotState state)
            {
                Contract.Requires(state == SnapshotState.NoMatch);

                SpecsWithTheSamePublicSurface = new List<ISourceFile>();
                SpecsWithIncompatiblePublicSurface = new List<ISourceFile>();
                State = state;
            }

            /// <nodoc />
            public static CanReuseSnapshotResult Invalid { get; } = new CanReuseSnapshotResult(SnapshotState.NoMatch);
        }

        /// <summary>
        /// Returns true if the given snapshot is still valid and all the parsed spec has the same symbols.
        /// </summary>
        private CanReuseSnapshotResult GetSnapshotReuseState(Possible<ISourceFile>[] specs, IWorkspaceBindingSnapshot snapshot)
        {
            var dirtySpecs = new List<ISourceFile>();
            var invalidSpecs = new List<ISourceFile>();

            foreach (var s in specs)
            {
                // All specs should be valid here, getting result from the 'Result' property directly.
                ISourceFile spec = s.Result;

                var state = snapshot.TryGetSpecState(spec.GetAbsolutePath(FrontEndContext.PathTable));
                if (state == null)
                {
                    // If symbols is missing from the snapshot, we can't reuse it
                    return CanReuseSnapshotResult.Invalid;
                }

                Contract.Assert(spec.BindingSymbols != null);
                Contract.Assert(state.BindingSymbols != null);

                if (spec.BindingSymbols.DeclaredSymbolsFingerprint != state.BindingSymbols.DeclaredSymbolsFingerprint)
                {
                    invalidSpecs.Add(spec);
                }
                else if (spec.BindingSymbols.ReferencedSymbolsFingerprint != state.BindingSymbols.ReferencedSymbolsFingerprint)
                {
                    dirtySpecs.Add(spec);
                }
            }

            return new CanReuseSnapshotResult(dirtySpecs, invalidSpecs);
        }

        private Workspace GetMainConfigWorkspace()
        {
            return (Workspace)m_frontEndFactory.ConfigurationProcessor.PrimaryConfigurationWorkspace;
        }

        private void LogWorkspaceSemanticErrors(ISemanticModel semanticModel)
        {
            // It is relatively hard to stop analysis when the error limit is reached.
            // For now, the limit is applied only on the error reporting stage.
            foreach (var semanticError in semanticModel.GetAllSemanticDiagnostics().Take(FrontEndConfiguration.ErrorLimit()))
            {
                ReportSemanticError(semanticError);
            }

            // Then writing generic error message that workspace computation failed
            m_logger.CannotBuildWorkspace(LoggingContext, "One or more error occurred during workspace analysis. See output for more details.");
        }

        private Task<Workspace> ComputeSemanticWorkspaceAsync(Workspace workspace, WorkspaceConfiguration workspaceConfiguration)
        {
            var semanticWorkspaceProvider = new SemanticWorkspaceProvider(m_frontEndStatistics, workspaceConfiguration);
            return semanticWorkspaceProvider.ComputeSemanticWorkspaceAsync(FrontEndContext.PathTable, workspace);
        }

        /// <summary>
        /// Logs all parsing and local binding errors.
        /// </summary>
        private void ReportWorkspaceParsingAndBindingErrorsIfNeeded(Workspace workspace)
        {
            if (workspace == null || workspace.Succeeded || workspace.IsCanceled)
            {
                return;
            }

            // It is relatively hard to stop analysis when the error limit is reached.
            // For now, the limit is applied only on the error reporting stage.
            foreach (var error in workspace.GetAllParsingAndBindingErrors().Take(FrontEndConfiguration.ErrorLimit()))
            {
                ReportSyntaxError(error);
            }
            
            // TODO: current design is not good.
            // Consider following: we're getting 10 errors during workspace construction.
            // Next line will print just one error and all other will just sit in memory.

            // Then writing generic error message that workspace computation failed
            m_logger.CannotBuildWorkspace(LoggingContext, workspace.Failures.First().Describe());
        }

        private Failure LogParseOrBindingErrorsIfAny(Possible<ISourceFile>[] parsedSpecs)
        {
            Failure failure = null;
            foreach (var e in parsedSpecs.Where(p => !p.Succeeded))
            {
                if (failure == null)
                {
                    failure = e.Failure;
                }

                foreach (var d in e.Failure.TryGetDiagnostics())
                {
                    ReportSyntaxError(d);
                }
            }

            return failure;
        }

        private WorkspaceConfiguration GetWorkspaceConfiguration(IConfiguration configuration, List<IResolverSettings> resolverSettings, CancellationToken cancellationToken)
        {
            return new WorkspaceConfiguration(
                resolverSettings: resolverSettings,
                constructFingerprintDuringParsing: configuration.FrontEnd.ConstructAndSaveBindingFingerprint(),
                maxDegreeOfParallelismForParsing: configuration.FrontEnd.MaxFrontEndConcurrency(),
                maxDegreeOfParallelismForTypeChecking: configuration.FrontEnd.MaxTypeCheckingConcurrency(),
                parsingOptions: new ParsingOptions(
                    namespacesAreAutomaticallyExported: true,
                    generateWithQualifierFunctionForEveryNamespace: false,
                    preserveTrivia: configuration.FrontEnd.PreserveTrivia(),
                    allowBackslashesInPathInterpolation: !configuration.FrontEnd.UseLegacyOfficeLogic(),
                    useSpecPublicFacadeAndAstWhenAvailable: CanUseSpecPublicFacadeAndAst(),
                    escapeIdentifiers: true,
                    failOnMissingSemicolons: true,
                    convertPathLikeLiteralsAtParseTime: true),
                cancelOnFirstFailure: configuration.FrontEnd.CancelParsingOnFirstFailure(),
                includePreludeWithName: PreludeModuleName,
                trackFileToFileDepedendencies: configuration.FrontEnd.TrackFileToFileDependencies(), // Spec-2-spec dependencies are required, if partial evaluation is on, or when the binding fingerprints are enabled.
                cancellationToken: cancellationToken);
        }

        private void ReportSemanticError(Diagnostic diagnostic)
        {
            if (diagnostic.File != null)
            {
                var lineAndColumn = diagnostic.GetLineAndColumn(diagnostic.File);
                var location = new Location
                {
                    Line = lineAndColumn.Line,
                    Position = lineAndColumn.Character,
                    File = diagnostic.File.FileName,
                };

                m_logger.CheckerError(LoggingContext, location, diagnostic.MessageText.ToString());
            }
            else
            {
                m_logger.CheckerGlobalError(LoggingContext, diagnostic.MessageText.ToString());
            }
        }

        private void ReportSyntaxError(Diagnostic diagnostic)
        {
            Location location = GetLocation(diagnostic);
            m_logger.SyntaxError(LoggingContext, location, diagnostic.MessageText.ToString());
        }

        /// <summary>
        /// The legacy build extent consists of projects and modules explicitly specified via the `project` and `packages`
        /// fields in the config file. In the current implementation, this amounts to modules resolved by the
        /// DefaultSourceResolver.  If no module is marked as <see cref="ModuleDescriptor.ResolverKind"/>
        /// <see cref="KnownResolverKind.DefaultSourceResolverKind"/>, then all non-prelude modules are returned.
        /// </summary>
        private static IEnumerable<ParsedModule> FindModulesConstitutingLegacyBuildExtent(List<ParsedModule> allModules)
        {
            var modulesResolvedByDefaultSourceResolver = allModules.Where(m => m.Definition.Descriptor.ResolverKind == KnownResolverKind.DefaultSourceResolverKind).ToList();

            // TODO: this is for backward compat only; remove when not needed any longer
            var buildExtentModules = modulesResolvedByDefaultSourceResolver.Any()
                ? modulesResolvedByDefaultSourceResolver
                : allModules;

            return buildExtentModules;
        }

        private List<ModuleDefinition> GetModulesAndSpecsToEvaluate(EvaluationFilter evaluationFilter)
        {
            if (
                // Module filter can be applied without /enableIncrementalFrontEnd+
                evaluationFilter.ModulesToResolve.Count != 0 || 
                (evaluationFilter.CanPerformPartialEvaluationScript(PrimaryConfigFile) && FrontEndConfiguration.EnableIncrementalFrontEnd()))
            {
                var workspaceFilter = new WorkspaceFilter(FrontEndContext.PathTable);
                return workspaceFilter.FilterForEvaluation(Workspace, evaluationFilter);
            }

            // The prelude is never part of the build extent
            var allModules = Workspace.SpecModules.ToList();

            // Under an Office build, the default source resolver defines the build extent. Otherwise, all modules are used
            var moduleExtent = FrontEndConfiguration.UseLegacyOfficeLogic() ? FindModulesConstitutingLegacyBuildExtent(allModules) : allModules;

            return moduleExtent.Select(m => m.Definition).ToList();
        }

        /// <summary>
        /// Method that make sure that the workspace and all related data is collected.
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2001:AvoidCallingProblematicMethods")]
        private void CleanWorkspaceMemory()
        {
            if (!m_collectMemoryAsSoonAsPossible)
            {
                return;
            }

            // Clean up pools used by parsing/typechecking.
            TypeScript.Net.Utilities.Pools.Clear();

            var weakReference = GetWorkspaceAnchorAndReleaseWorkspace();
            if (weakReference == null)
            {
                // Nothing to do, workspace is empty.
                return;
            }

            GC.Collect();

            if (FrontEndConfiguration.FailIfWorkspaceMemoryIsNotCollected())
            {
                Contract.Assert(!weakReference.IsAlive, "Failed to collect workspace");
            }
        }

        private WeakReference GetWorkspaceAnchorAndReleaseWorkspace()
        {
            if (Workspace.SpecCount == 0)
            {
                // Nothing to do, workspace is empty or missing.
                return null;
            }

            // Can't use just the workspace as an anchor for a weak reference,
            // because other pieces of the system can hold a reference to the workspace.
            var result = new WeakReference(Workspace.SpecSources.First().Value.SourceFile);
            Workspace = null;
            return result;
        }

        private readonly struct Watch : IDisposable
        {
            private readonly Stopwatch m_stopwatch;

            [SuppressMessage("Microsoft.Usage", "CA1801:ReviewUnusedParameters", MessageId = "fakeArgument", Justification = "Struct doesn't support default constructors")]
            private Watch(bool fakeArgument)
            {
                m_stopwatch = Stopwatch.StartNew();
            }

            public int ElapsedMilliseconds => (int)m_stopwatch.ElapsedMilliseconds;

            public void Dispose()
            {
            }

            public static Watch Start() => new Watch(false);
        }
    }
}

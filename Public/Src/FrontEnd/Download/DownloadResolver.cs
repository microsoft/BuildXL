// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Script.Values;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.Evaluation;
using BuildXL.FrontEnd.Sdk.Mutable;
using BuildXL.FrontEnd.Sdk.Workspaces;
using BuildXL.FrontEnd.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.FrontEnd.Download
{
    /// <summary>
    /// Download resolver frontend
    /// </summary>
    public sealed class DownloadResolver : IResolver
    {
        private readonly FrontEndStatistics m_frontEndStatistics;
        private readonly EvaluationStatistics m_evaluationStatistics;
        private readonly FrontEndHost m_frontEndHost;
        private readonly FrontEndContext m_context;
        private readonly Script.Tracing.Logger m_logger;
        private DownloadWorkspaceResolver m_workspaceResolver;
        private readonly SemaphoreSlim m_evaluationSemaphore = new SemaphoreSlim(1);

        /// <nodoc />
        public string Name { get; private set; }

        /// <nodoc/>
        public DownloadResolver(
            FrontEndStatistics frontEndStatistics,
            EvaluationStatistics evaluationStatistics,
            FrontEndHost frontEndHost,
            FrontEndContext context,
            Script.Tracing.Logger logger,
            string frontEndName)
        {
            Contract.Requires(!string.IsNullOrEmpty(frontEndName));

            Name = frontEndName;
            m_frontEndStatistics = frontEndStatistics;
            m_evaluationStatistics = evaluationStatistics;
            m_frontEndHost = frontEndHost;
            m_context = context;
            m_logger = logger;
        }

        /// <inheritdoc />
        public Task<bool> InitResolverAsync([NotNull] IResolverSettings resolverSettings, object workspaceResolver)
        {
            m_workspaceResolver = workspaceResolver as DownloadWorkspaceResolver;

            if (m_workspaceResolver == null)
            {
                Contract.Assert(false, I($"Wrong type for resolver, expected {nameof(DownloadWorkspaceResolver)} but got {nameof(workspaceResolver.GetType)}"));
            }

            Name = resolverSettings.Name;

            return Task.FromResult(true);
        }

        /// <inheritdoc />
        public void LogStatistics()
        {
            // Statistics are logged in the FrontEnd.
        }

        /// <inheritdoc />
        public void NotifyEvaluationFinished()
        {
            // Nothing to do
        }

        /// <inheritdoc />
        public async Task<bool?> TryConvertModuleToEvaluationAsync(IModuleRegistry moduleRegistry, ParsedModule module, IWorkspace workspace)
        {
            if (!string.Equals(module.Descriptor.ResolverName, Name, StringComparison.Ordinal))
            {
                return null;
            }

            var package = CreatePackage(module.Definition);

            Contract.Assert(module.Specs.Count == 1, "This resolver generated the module, so we expect a single spec.");
            var sourceKv = module.Specs.First();

            // The in-memory generated spec is a regular DScript one, so run regular AST conversion
            var result = await FrontEndUtilities.RunAstConversionAsync(m_frontEndHost, m_context, m_logger, m_frontEndStatistics, package, sourceKv.Key);

            if (!result.Success)
            {
                return false;
            }

            // Register the uninstantiated module
            var moduleData = new UninstantiatedModuleInfo(
                result.SourceFile,
                result.Module,
                result.QualifierSpaceId.IsValid ? result.QualifierSpaceId : m_context.QualifierTable.EmptyQualifierSpaceId);

            m_frontEndHost.ModuleRegistry.AddUninstantiatedModuleInfo(moduleData);

            return true;
        }

        private Package CreatePackage(ModuleDefinition moduleDefinition)
        {
            var moduleDescriptor = moduleDefinition.Descriptor;

            var packageId = PackageId.Create(StringId.Create(m_context.StringTable, moduleDescriptor.Name));
            var packageDescriptor = new PackageDescriptor
            {
                Name = moduleDescriptor.Name,
                Main = moduleDefinition.MainFile,
                NameResolutionSemantics = NameResolutionSemantics.ImplicitProjectReferences,
                Publisher = null,
                Version = moduleDescriptor.Version,
                Projects = new List<AbsolutePath>(moduleDefinition.Specs),
            };

            return Package.Create(packageId, moduleDefinition.ModuleConfigFile, packageDescriptor, moduleId: moduleDescriptor.Id);
        }

        /// <inheritdoc />
        public async Task<bool?> TryEvaluateModuleAsync([NotNull] IEvaluationScheduler scheduler, [NotNull] ModuleDefinition module, QualifierId qualifierId)
        {
            // Abstraction between SDK/Workspace/Core/Resolvers is broken here...
            var moduleDefinition = (ModuleDefinition)module;

            if (!string.Equals(moduleDefinition.Descriptor.ResolverName, Name, StringComparison.Ordinal))
            {
                return null;
            }

            var downloadData = m_workspaceResolver.Downloads[module.Descriptor.Name];

            // Make sure evaluating is guarded by the semaphore, so it only happens one at a time
            // There is no need to make sure we don't evaluate duplicate work here since module evaluation
            // happens once per qualifier.
            await m_evaluationSemaphore.WaitAsync();

            try
            {
                // Modules of the download resolver are always instantiated with the empty qualifier
                var moduleRegistry = (ModuleRegistry)m_frontEndHost.ModuleRegistry;
                var moduleLiteral = moduleRegistry
                    .GetUninstantiatedModuleInfoByPath(downloadData.ModuleSpecFile)
                    .FileModuleLiteral
                    .InstantiateFileModuleLiteral(moduleRegistry, QualifierValue.CreateEmpty(m_context.QualifierTable));

                // Evaluate all values of the module
                using (var contextTree = new ContextTree(
                    m_frontEndHost,
                    m_context,
                    m_logger,
                    m_evaluationStatistics,
                    new QualifierValueCache(),
                    isBeingDebugged: false,
                    decorator: null,
                    moduleLiteral,
                    new EvaluatorConfiguration(trackMethodInvocations: false, cycleDetectorStartupDelay: TimeSpanUtilities.MillisecondsToTimeSpan(10)),
                    scheduler,
                    FileType.Project))
                {
                    var moduleTracker = VisitedModuleTracker.Create(isDebug: false);
                    var success = await moduleLiteral.EvaluateAllAsync(contextTree.RootContext, moduleTracker, ModuleEvaluationMode.None);
                    return success;
                }
            }
            finally 
            {
                m_evaluationSemaphore.Release();
            }
        }
    }
}
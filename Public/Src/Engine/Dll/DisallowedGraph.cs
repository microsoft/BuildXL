// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Ipc.Interfaces;
using BuildXL.Pips;
using BuildXL.Pips.Builders;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Engine
{
    /// <summary>
    /// A pip graph that fails when someone tries to add pips.
    /// </summary>
    /// <remarks>
    /// This graph is intended to be used when processing Config and Module build files.
    /// </remarks>
    internal sealed class DisallowedGraph : IPipGraph
    {
        /// <inheritdoc />
        public bool AddProcess(Process process, PipId valuePip)
        {
            Contract.Requires(process != null, "Argument process cannot be null");
            Tracing.Logger.Log.CannotAddCreatePipsDuringConfigOrModuleEvaluation(Events.StaticContext);
            return false;
        }

        /// <inheritdoc />
        public bool AddIpcPip(IpcPip ipcPip, PipId valuePip)
        {
            Contract.Requires(ipcPip != null, "Argument pip cannot be null");
            Tracing.Logger.Log.CannotAddCreatePipsDuringConfigOrModuleEvaluation(Events.StaticContext);
            return false;
        }

        /// <inheritdoc />
        public bool AddCopyFile(CopyFile copyFile, PipId valuePip)
        {
            Contract.Requires(copyFile != null, "Argument copyFile cannot be null");
            Tracing.Logger.Log.CannotAddCreatePipsDuringConfigOrModuleEvaluation(Events.StaticContext);
            return false;
        }

        /// <inheritdoc />
        public bool AddWriteFile(WriteFile writeFile, PipId valuePip)
        {
            Contract.Requires(writeFile != null, "Argument writeFile cannot be null");
            Tracing.Logger.Log.CannotAddCreatePipsDuringConfigOrModuleEvaluation(Events.StaticContext);
            return false;
        }

        /// <inheritdoc />
        public DirectoryArtifact AddSealDirectory(SealDirectory sealDirectory, PipId valuePip)
        {
            Contract.Requires(sealDirectory != null);
            Tracing.Logger.Log.CannotAddCreatePipsDuringConfigOrModuleEvaluation(Events.StaticContext);
            return DirectoryArtifact.Invalid;
        }

        /// <inheritdoc />
        public bool AddOutputValue(ValuePip value)
        {
            Contract.Requires(value != null, "Argument outputValue cannot be null");

            // Value pips are ok to generate, Value pips are just not allowed to create executable pips.
            return true;
        }

        /// <inheritdoc />
        public bool AddValueValueDependency(in ValuePip.ValueDependency valueDependency)
        {
            Contract.Requires(valueDependency.ParentIdentifier.IsValid);
            Contract.Requires(valueDependency.ChildIdentifier.IsValid);

            // Value to value pip dependencies are also allowed
            return true;
        }

        /// <inheritdoc />
        public bool AddSpecFile(SpecFilePip specFile)
        {
            Contract.Requires(specFile != null, "Argument specFile cannot be null");
            Tracing.Logger.Log.CannotAddCreatePipsDuringConfigOrModuleEvaluation(Events.StaticContext);
            return false;
        }

        /// <inheritdoc />
        [SuppressMessage("Microsoft.Naming", "CA1716:IdentifiersShouldNotMatchKeywords")]
        public bool AddModule(ModulePip module)
        {
            Tracing.Logger.Log.CannotAddCreatePipsDuringConfigOrModuleEvaluation(Events.StaticContext);
            return false;
        }

        /// <inheritdoc />
        public bool AddModuleModuleDependency(ModuleId moduleId, ModuleId dependency)
        {
            Tracing.Logger.Log.CannotAddCreatePipsDuringConfigOrModuleEvaluation(Events.StaticContext);
            return false;
        }

        /// <inheritdoc />
        /// <inheritdoc />
        public IEnumerable<Pip> RetrieveScheduledPips()
        {
            Tracing.Logger.Log.CannotAddCreatePipsDuringConfigOrModuleEvaluation(Events.StaticContext);
            yield break;
        }

        /// <inheritdoc />
        public IEnumerable<Pip> RetrievePipImmediateDependencies(Pip pip)
        {
            Tracing.Logger.Log.CannotAddCreatePipsDuringConfigOrModuleEvaluation(Events.StaticContext);
            yield break;
        }

        /// <inheritdoc />
        public IEnumerable<Pip> RetrievePipImmediateDependents(Pip pip)
        {
            Tracing.Logger.Log.CannotAddCreatePipsDuringConfigOrModuleEvaluation(Events.StaticContext);
            yield break;
        }

        /// <inheritdoc />
        public int PipCount => 0;

        /// <summary>
        /// Creates a new moniker if it hasn't already been created; otherwise returns the previously created one.
        /// </summary>
        public IIpcMoniker GetApiServerMoniker()
        {
            return null;
        }

        /// <inheritdoc />
        public GraphPatchingStatistics PartiallyReloadGraph(HashSet<AbsolutePath> affectedSpecs)
        {
            Contract.Requires(affectedSpecs != null);
            Tracing.Logger.Log.CannotAddCreatePipsDuringConfigOrModuleEvaluation(Events.StaticContext);
            return default(GraphPatchingStatistics);
        }

        /// <inheritdoc />
        public void SetSpecsToIgnore(IEnumerable<AbsolutePath> specsToIgnore)
        {
            Tracing.Logger.Log.CannotAddCreatePipsDuringConfigOrModuleEvaluation(Events.StaticContext);
        }

        /// <inheritdoc />
        public DirectoryArtifact ReserveSharedOpaqueDirectory(AbsolutePath directoryArtifactRoot)
        {
            return DirectoryArtifact.CreateWithZeroPartialSealId(directoryArtifactRoot);
        }

        /// <inheritdoc />
        public void ApplyCurrentOsDefaults(ProcessBuilder processBuilder)
        {
        }
    }
}

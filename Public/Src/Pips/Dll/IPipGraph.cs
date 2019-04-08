// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Ipc.Interfaces;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using JetBrains.Annotations;

namespace BuildXL.Pips
{
    /// <summary>
    /// A mutable pip graphs
    /// </summary>
    /// <remarks>
    /// All methods are thread-safe.
    /// </remarks>
    public interface IPipGraph : IPipScheduleTraversal
    {
        /// <summary>
        /// Schedules a process Pip. The return value indicate whether this was a valid Pip.
        /// </summary>
        bool AddProcess([NotNull]Process process, PipId valuePip);

        /// <summary>
        /// Schedules an IPC Pip. The return value indicate whether this was a valid Pip.
        /// </summary>
        bool AddIpcPip([NotNull]IpcPip ipcPip, PipId valuePip);

        /// <summary>
        /// Schedules a copy file Pip. The return value indicate whether this was a valid Pip.
        /// </summary>
        bool AddCopyFile([NotNull]CopyFile copyFile, PipId valuePip);

        /// <summary>
        /// Schedules a write file Pip. The return value indicate whether this was a valid Pip.
        /// </summary>
        bool AddWriteFile([NotNull]WriteFile writeFile, PipId valuePip);

        /// <summary>
        /// Schedules a directory or partial view of a directory to be sealed (made immutable) once its specified contents are satisfied.
        /// </summary>
        /// <returns>
        /// An artifact representing the directory once it is sealed with final contents.
        /// If scheduling the seal fails, <see cref="DirectoryArtifact.Invalid"/> is returned.
        /// </returns>
        DirectoryArtifact AddSealDirectory([NotNull]SealDirectory sealDirectory, PipId valuePip);
        
        /// <summary>
        /// Adds a value pip representing a value. The return value indicates whether this was a valid Pip.
        /// </summary>
        bool AddOutputValue([NotNull]ValuePip value);

        /// <summary>
        /// Adds a value to value dependency. Also creates a value pip for each pip if one does not already exists.
        /// </summary>
        /// <remarks>
        /// This relies on output value pips being created via <see cref="AddOutputValue"/> before this method is called
        /// to add any dependencies. Otherwise when adding the relationship, each value pip would be added since it
        /// wouldn't already exist. Then adding the actual output value pip would cause a collision.
        /// </remarks>
        bool AddValueValueDependency(in ValuePip.ValueDependency valueDependency);

        /// <summary>
        /// Adds a specFile pip. The return value indicates whether this was a valid Pip.
        /// </summary>
        bool AddSpecFile([NotNull]SpecFilePip specFile);

        /// <summary>
        /// Adds a module pip. The return value indicates whether this was a valid Pip.
        /// </summary>
        bool AddModule([NotNull]ModulePip module);

        /// <summary>
        /// Add a dependency between 2 modules
        /// </summary>
        bool AddModuleModuleDependency(ModuleId moduleId, ModuleId dependency);

        /// <summary>
        /// Creates a new moniker if it hasn't already been created; otherwise returns the previously created one.
        /// </summary>
        IIpcMoniker GetApiServerMoniker();

        /// <summary>
        /// Partially reloads pip graph.
        ///
        /// Reads pips from an "old" pip table (provided via a constructor or initializer); each one of them which has
        /// provenance information and its producer spec is not in the given list of affected specs is added to this graph.
        ///
        /// Provided <paramref name="affectedSpecs"/> <strong>must</strong> form a transitive closure of affected specs.
        /// If there exists a node which has an affected dependency node while its own producing spec is not listed in
        /// <paramref name="affectedSpecs"/>, this method should throw <see cref="BuildXLException"/>.
        /// </summary>
        /// <remarks>
        /// This method could, in principle, skip reloading transitively affected pips and hence not require
        /// <paramref name="affectedSpecs"/> to form a closure.  The reason why that's not what's the contract of this
        /// method is the following:
        ///   - any partial/incremental evaluation of the front end will be based on modified files, so it better
        ///     correctly decide which files are affected and which are not
        ///   - if this implementation would skip reloading transitively affected pips, then it could hapen that there is
        ///     a spec file from which some pips are reloaded and some are not.  That scenario would not work well with
        ///     partial front end evaluation which is always spec-based (a spec is evaluated entirely or not at all).
        /// </remarks>
        /// <param name="affectedSpecs">Affected spec files---pips originating from these specs will not be reloaded.</param>
        GraphPatchingStatistics PartiallyReloadGraph([NotNull]HashSet<AbsolutePath> affectedSpecs);

        /// <summary>
        /// If set, this builder will ignore requests to add pips originating from one of these specs.
        /// Pass <code>null</code> to reset this value and disable this feature.
        /// </summary>
        /// <remarks>
        /// The reasons why <see cref="PartiallyReloadGraph"/> and <see cref="SetSpecsToIgnore"/> are 2 separate methods
        /// is to allow for greater flexibility when it comes to partial front end evaluation.  An implementation of this
        /// interface could, in principle, treat all specs that are not specified as 'affected' in <see cref="PartiallyReloadGraph"/>
        /// as 'ignored'; that, however, is not necessarily the most efficient thing to do.  Consider the following graph:
        ///
        ///   0     _________________________
        ///   |    /                         \
        ///   1*   | huge graph disconnected |
        ///   |    |   from nodes 0, 1, 2    |
        ///   2    \_________________________/
        ///
        /// Only spec '1' is modified, so affected specs are '1', and '2'.  When graph is reloaded, all pips from
        /// all specs but '1' and '2' are reloaded.  To partially evaluate the build extent, the front end will likely
        /// have to include spec '0' in its evaluation workspace (because it is referenced by the modified specs,
        /// and hence necessary for the overall evaluation); in that case, the front end should tell the pip graph to
        /// only ignore pips from spec '0', making all lookups performed by this pip graph much faster than what
        /// they would be if the set of ignored specs included all the specs but '1' and '2'.
        /// </remarks>
        void SetSpecsToIgnore(IEnumerable<AbsolutePath> specsToIgnore);

        /// <summary>
        /// Reserves a shared opaque directory to be added to the pip graph with the proper seal id
        /// </summary>
        DirectoryArtifact ReserveSharedOpaqueDirectory(AbsolutePath directoryArtifactRoot);
    }
}

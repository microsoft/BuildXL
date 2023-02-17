// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Core;

namespace BuildXL.Pips.Graph
{
    /// <summary>
    /// Manager to add binary pip fragments to a pip graph builder
    /// </summary>
    /// <remarks>
    /// TODO: Should this implement <see cref="IMutablePipGraph"/>?
    /// </remarks>
    public interface IPipGraphFragmentManager
    {
        /// <summary>
        /// Add a pip graph fragment file to the graph.
        /// </summary>
        /// <param name="filePath">Path to the file to read.</param>
        /// <param name="description">Description of the fragment for printing on the console</param>
        /// <param name="dependencies">Path to the fragments this fragment depends on.</param>
        bool AddFragmentFileToGraph(AbsolutePath filePath, string description, IEnumerable<AbsolutePath> dependencies);

        /// <summary>
        /// Get all tasks
        /// </summary>
        IReadOnlyCollection<(PipGraphFragmentSerializer, Task<bool>)> GetAllFragmentTasks();

        /// <summary>
        /// Adds a module pip.
        /// </summary>
        bool AddModulePip(ModulePip modulePip);

        /// <summary>
        /// Adds a spec file pip.
        /// </summary>
        bool AddSpecFilePip(SpecFilePip specFilePip);
    }
}
// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Threading.Tasks;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Pips
{
    /// <summary>
    /// Manager to add binary pip fragments to a pip graph builder
    /// </summary>
    /// <remarks>
    /// TODO: Should this implement <see cref="IPipGraph"/>?
    /// </remarks>
    public interface IPipGraphFragmentManager
    {
        /// <summary>
        /// Add a pip graph fragment file to the graph.
        /// </summary>
        /// <param name="id">Unique Id belonging to this fragment</param>
        /// <param name="filePath">Path to the file to read.</param>
        /// <param name="dependencyIds">Name to the pip fragments this fragment depends on.</param>
        /// <param name="description">Description of the fragment for printing on the console</param>
        Task<bool> AddFragmentFileToGraph(int id, AbsolutePath filePath, int[] dependencyIds, string description);

        /// <summary>
        /// Get a list of (fragment description, fragment load task)
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
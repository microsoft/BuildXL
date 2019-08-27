// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        /// <param name="filePath">Path to the file to read.</param>
        /// <param name="description">Description of the fragment for printing on the console</param>
        bool AddFragmentFileToGraph(AbsolutePath filePath, string description);

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
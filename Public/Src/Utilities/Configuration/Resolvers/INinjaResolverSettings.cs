// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Utilities.Configuration.Resolvers;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Settings for Ninja resolver
    /// </summary>
    public interface INinjaResolverSettings : IResolverSettings
    {
        /// <summary>
        /// Targets to resolve
        /// </summary>
        IReadOnlyList<string> Targets { get; }
     
        /// <summary>
        /// Root of the project
        /// </summary>
        AbsolutePath ProjectRoot { get; }

        /// <summary>
        /// The build file, typically build.ninja. If null, "{ProjectRoot}/build.ninja" is used
        /// </summary>
        AbsolutePath SpecFile { get; }

        /// <summary>
        /// Module name. Should be unique, as it is used as an id
        /// </summary>
        string ModuleName { get; }

        /// <summary>
        /// Preserve intermediate outputs used to construct the graph,
        /// that is, the arguments passed to the tools and the JSON reperesentation of the dependency graph.
        /// </summary>
        /// <remarks>
        /// Defaults to false
        /// </remarks>
        bool? KeepToolFiles { get; }

        /// <summary>
        /// User-specified untracked artifacts
        /// </summary>
        IUntrackingSettings UntrackingSettings { get; }

        /// <summary>
        /// Remove all flags involved with the output of debug information (PDB files).
        /// This is, remove the /Zi, /ZI, /Z7, /FS flags.
        /// This option is helpful to troubleshoot debug builds that are failing with related errors
        /// </summary>
        /// <remarks>
        /// Defaults to false
        /// </remarks>
        bool? RemoveAllDebugFlags { get; }
    }
}

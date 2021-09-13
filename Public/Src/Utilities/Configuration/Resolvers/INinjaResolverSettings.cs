// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Configuration.Resolvers;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Settings for Ninja resolver
    /// </summary>
    public interface INinjaResolverSettings : IProjectGraphResolverSettings
    {
        /// <summary>
        /// Targets to resolve
        /// </summary>
        IReadOnlyList<string> Targets { get; }

        /// <summary>
        /// The build file, typically build.ninja. If null, "{ProjectRoot}/build.ninja" is used
        /// </summary>
        AbsolutePath SpecFile { get; }

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

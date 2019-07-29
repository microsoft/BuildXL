// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace MsBuildGraphBuilderTool
{
    public interface IMsBuildAssemblyLoader
    {
        /// <summary>
        /// Given a collection of search locations, performs a recursive search on those trying to find the required
        /// MsBuild assemblies.
        /// </summary>
        /// <remarks>
        /// Proper versions and public tokens are verified as well. This method registers an assembly resolver that loads
        /// the required assemblies from the locations found, or fails with a given failure reason if those assemblies are
        /// not found
        /// </remarks>
        /// <param name="searchLocations">A collection of full paths to directories, representing search locations on disk</param>
        /// <param name="failureReason">On failure, the reason for why the assemblies were not found</param>
        /// <param name="locatedAssemblyPaths">A dictionary from assembly names to paths to the required assemblies that were found. May not be complete if the invocation failed.</param>
        bool TryLoadMsBuildAssemblies(
            IEnumerable<string> searchLocations, 
            GraphBuilderReporter reporter, 
            out string failureReason, 
            out IReadOnlyDictionary<string, string> locatedAssemblyPaths, 
            out string locatedMsBuildExePath);
    }
}
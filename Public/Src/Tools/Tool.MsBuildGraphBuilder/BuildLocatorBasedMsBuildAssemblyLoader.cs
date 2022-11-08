// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Locator;
using MsBuildGraphBuilderTool;

namespace ProjectGraphBuilder
{
    /// <summary>
    /// MsBuild assembly loader based on <code>Microsoft.Build.Locator</code>: https://github.com/Microsoft/MSBuildLocator.
    /// </summary>
    internal class BuildLocatorBasedMsBuildAssemblyLoader : IMsBuildAssemblyLoader
    {
        /// <summary>
        /// Tries to load MsBuild assemblies using <see cref="MSBuildLocator"/>.
        /// </summary>
        /// <remarks>
        /// This method has to be called before referencing any MsBuild types (from the Microsoft.Build namespace).
        /// https://learn.microsoft.com/en-us/visualstudio/msbuild/find-and-use-msbuild-versions?view=vs-2022
        /// </remarks>
        public bool TryLoadMsBuildAssemblies(
            IEnumerable<string> searchLocations,
            GraphBuilderReporter reporter,
            out string failureReason,
            out IReadOnlyDictionary<string, string> locatedAssemblyPaths,
            out string locatedMsBuildExePath)
        {
            failureReason = string.Empty;

            // MSBuildLocator currently does not provide information about the loaded assembly.
            locatedAssemblyPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Since we cannot reference any MSBuild types (from the Microsoft.Build namespace) in this method,
            // we will infer the located MsBuild path in the caller.
            locatedMsBuildExePath = string.Empty;

            try
            {
                if (MSBuildLocator.IsRegistered)
                {
                    return true;
                }

                if (searchLocations == null || !searchLocations.Any())
                {
                    MSBuildLocator.RegisterDefaults();
                }
                else
                {
                    MSBuildLocator.RegisterMSBuildPath(searchLocations.Where(Directory.Exists).ToArray());
                }

                return true;
            }
            catch (Exception e)
            {
                string locations = searchLocations == null || !searchLocations.Any() ? "<DEFAULT>" : string.Join("; ", searchLocations);
                failureReason = $"Failed to load MsBuild assemblies using '{locations}' using {nameof(BuildLocatorBasedMsBuildAssemblyLoader)}: {e}";
            }

            return false;
        }
    }
}

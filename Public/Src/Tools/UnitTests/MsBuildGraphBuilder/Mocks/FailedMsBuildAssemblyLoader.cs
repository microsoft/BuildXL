// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using MsBuildGraphBuilderTool;

namespace Test.ProjectGraphBuilder.Mocks
{
    /// <summary>
    /// An assembly loader that always fails
    /// </summary>
    class FailedMsBuildAssemblyLoader : IMsBuildAssemblyLoader
    {
        /// <nodoc/>
        public bool TryLoadMsBuildAssemblies(IEnumerable<string> searchLocations, GraphBuilderReporter reporter, out string failureReason, out IReadOnlyDictionary<string, string> locatedAssemblyPaths, out string locatedMsBuildExePath)
        {
            failureReason = "This always fails";
            locatedAssemblyPaths = new Dictionary<string, string>();
            locatedMsBuildExePath = string.Empty;

            return false;
        }
    }
}

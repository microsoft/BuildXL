// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.FrontEnd.MsBuild.Serialization;
using MsBuildGraphBuilderTool;
using Test.BuildXL.TestUtilities.Xunit;
using Xunit.Abstractions;

namespace Test.ProjectGraphBuilder.Utilities
{
    public abstract class GraphBuilderToolTestBase : TemporaryStorageTestBase
    {
        /// <nodoc/>
        public GraphBuilderToolTestBase(ITestOutputHelper output) : base(output) {}

#if NET_FRAMEWORK
        /// <nodoc/>
        protected bool RunningUnderDotNetCore => false;
#else
        /// <nodoc/>
        protected bool RunningUnderDotNetCore => true;
#endif
        /// <nodoc/>
        protected MsBuildAssemblyLoader AssemblyLoader => new MsBuildAssemblyLoader(RunningUnderDotNetCore);

        /// <nodoc/>
        protected MSBuildGraphBuilderArguments GetStandardBuilderArguments(
            IReadOnlyCollection<string> projectsToParse,
            string outputFile,
            GlobalProperties globalProperties,
            IReadOnlyCollection<string> entryPointTargets,
            IReadOnlyCollection<GlobalProperties> requestedQualifiers,
            bool allowProjectsWithoutTargetProtocol)
        {
            return new MSBuildGraphBuilderArguments(
                    projectsToParse,
                    outputFile,
                    globalProperties,
                    mSBuildSearchLocations: new[] {TestDeploymentDir},
                    entryPointTargets,
                    requestedQualifiers,
                    allowProjectsWithoutTargetProtocol,
                    RunningUnderDotNetCore);
        }
    }
}

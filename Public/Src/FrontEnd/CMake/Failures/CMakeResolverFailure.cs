// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Utilities;

namespace BuildXL.FrontEnd.CMake.Failures
{
    internal abstract class CMakeResolverFailure : Failure
    {
        /// <inheritdoc/>
        public override BuildXLException CreateException() => new BuildXLException(Describe());

        /// <inheritdoc/>
        public override BuildXLException Throw() => throw CreateException();
    }


    internal class NinjaWorkspaceResolverInitializationFailure : CMakeResolverFailure
    {
        /// <inheritdoc/>
        public override string Describe() => "The embedded Ninja resolver wasn't successfully initialized";
    }

    internal class InnerNinjaFailure : CMakeResolverFailure
    {
        private readonly Failure m_innerFailure;
        
        /// <nodoc/>
        public InnerNinjaFailure(Failure innerFailure)
        {
            m_innerFailure = innerFailure;
        }

        /// <inheritdoc/>
        public override string Describe() => $"There was an error associated with the embedded Ninja resolver. Details: {m_innerFailure.Describe()}";
    }


    internal class CMakeGenerationError : CMakeResolverFailure
    {
        private readonly string m_moduleName;
        private readonly string m_buildDirectory;

        public CMakeGenerationError(string moduleName, string buildDirectory)
        {
            m_moduleName = moduleName;
            m_buildDirectory = buildDirectory;
        }

        public override string Describe() => $"There was an issue with the trying to generate the build directory {m_buildDirectory} for module {m_moduleName}. Details were logged.";
    }

}

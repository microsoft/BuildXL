// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Builders
{
    /// <summary>
    /// The implementation of <see cref="IBuildXLCredentialScanner"/> that performs no credential scanning. This is used when credential scanner is not available.
    /// </summary>
    public sealed class NoOpCredentialScanner : IBuildXLCredentialScanner
    {
        /// <inheritdoc/>
        public NoOpCredentialScanner()
        {
        }

        /// <summary>
        /// NoOp method for credscan post functionality.
        /// </summary>
        public void PostEnvVarsForProcessing(Process process, ReadOnlyArray<EnvironmentVariable> environmentVariables)
        {
        }

        /// <summary>
        /// This method is used to check if the credscan operation has been successfully completed or not and if secrets were detected or not.
        /// Since this is a NoOp interface this method always return true. Ensuring that graph construction proceeds with no issues.
        /// </summary>
        public bool Complete(PipExecutionContext context)
        {
            return true;
        }
    }
}

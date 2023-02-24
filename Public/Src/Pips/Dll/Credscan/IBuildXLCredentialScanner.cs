// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Pips.Operations;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;

namespace BuildXL.Pips.Builders
{
    /// <summary>
    /// This is used to scan the environment variables for secrets using the credscan library.
    /// </summary>
    public interface IBuildXLCredentialScanner
    {
        /// <summary>
        /// This method adds the environment variables to be scanned for secrets.
        /// </summary>
        void PostEnvVarsForProcessing(Process process, ReadOnlyArray<EnvironmentVariable> environmentVariables);

        /// <summary>
        /// This method ensures that credscan has completed scanning of the environment variables.
        /// Also performs appropriate error handling operations if any secrets are detected.
        /// </summary>
        bool Complete(PipExecutionContext context);
    }
}

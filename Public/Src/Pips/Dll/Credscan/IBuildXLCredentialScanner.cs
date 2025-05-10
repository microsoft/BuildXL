// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
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
        IBuildXLCredentialScanResult Complete(PipExecutionContext context);
    }

    /// <summary>
    /// The result of a credential scan by a <see cref="IBuildXLCredentialScanner"/>
    /// </summary>
    public interface IBuildXLCredentialScanResult
    {
        /// <summary>
        /// True if a credential was detected in the scan
        /// </summary>
        bool CredentialDetected { get; }

        /// <summary>
        /// The collection of variables for which a credential was detected
        /// </summary>
        IReadOnlySet<string> EnvVarsWithDetections { get; }
    }
}

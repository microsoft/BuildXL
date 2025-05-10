// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Pips.Operations;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;

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

        /// <inheritdoc />
        IBuildXLCredentialScanResult IBuildXLCredentialScanner.Complete(PipExecutionContext context)
        {
            return new NoOpCredScanResult();
        }
    }

    /// <nodoc />
    public class NoOpCredScanResult : IBuildXLCredentialScanResult
    {
        private static readonly ReadOnlyHashSet<string> s_empty = new();

        /// <inheritdoc />
        public bool CredentialDetected => false;

        /// <inheritdoc />
        public IReadOnlySet<string> EnvVarsWithDetections => s_empty;
    }
}

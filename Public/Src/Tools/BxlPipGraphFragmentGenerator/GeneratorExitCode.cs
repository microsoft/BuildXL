// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BxlPipGraphFragmentGenerator
{
    /// <summary>
    /// Exit codes.
    /// </summary>
    public enum GeneratorExitCode : int
    {
        /// <summary>
        /// Success run.
        /// </summary>
        Success = 0,

        /// <summary>
        /// Failed run.
        /// </summary>
        Failed = 1,

        /// <summary>
        /// Invalid arguments or command line.
        /// </summary>
        InvalidArguments = 1,

        /// <summary>
        /// Missing runtime dependencies.
        /// </summary>
        MissingRuntimeDependency = 2,

        /// <summary>
        /// Infrastructure error, e.g., out of disk space.
        /// </summary>
        InfrastructureError = 3,

        /// <summary>
        /// Unknown internal error.
        /// </summary>
        InternalError = -1,
    }
}

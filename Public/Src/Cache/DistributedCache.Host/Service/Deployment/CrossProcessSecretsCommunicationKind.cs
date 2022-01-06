// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.Host.Service.OutOfProc;

namespace BuildXL.Cache.Host.Service
{
    /// <summary>
    /// Defines which communication process is used for passing secrets between the current and the launched processes.
    /// </summary>
    public enum CrossProcessSecretsCommunicationKind
    {
        /// <summary>
        /// The mode used by the launcher via <see cref="EnvironmentVariableHost"/> when all the secrets serialized through environment variables one by one.
        /// </summary>
        Environment,

        /// <summary>
        /// The mode used by <see cref="CacheServiceWrapper"/> when all the <see cref="RetrievedSecrets"/> serialized in a single environment variable.
        /// </summary>
        EnvironmentSingleEntry,

        /// <summary>
        /// Not implemented yet: will be used by <see cref="CacheServiceWrapper"/> when the secrets will be communicated via memory mapped file that will also support updates.
        /// </summary>
        MemoryMappedFile,
    }
}

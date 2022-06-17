// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// A status code for push file operation.
    /// </summary>
    public enum ProactivePushStatus
    {
        /// <summary>
        /// A location was pushed successfully at least to one machine.
        /// </summary>
        Success,

        /// <summary>
        /// The whole proactive copy operation was skipped, for instance, because there is enough locations for a given hash.
        /// </summary>
        Skipped,

        /// <summary>
        /// At least one target machine rejected the content and the other machine either rejected the content or the operation failed.
        /// </summary>
        Rejected,

        /// <summary>
        /// All candidates in the build ring for given build already have the content.
        /// </summary>
        MachineAlreadyHasCopy,

        /// <summary>
        /// Could not find any eligible machine
        /// </summary>
        MachineNotFound,

        /// <summary>
        /// BuildId was not specified, so machines in the build ring cannot be found
        /// </summary>
        BuildIdNotSpecified,

        /// <summary>
        /// Proactive copy failed.
        /// </summary>
        Error,
    }
}

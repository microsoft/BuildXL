// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Engine
{
    /// <summary>
    /// Represents the state of loading specification files
    /// </summary>
    public enum SpecFileLoadingState : byte
    {
        /// <summary>
        /// Not started.
        /// </summary>
        NotStarted = 0x0,

        /// <summary>
        /// Failed to load specification files.
        /// </summary>
        Failed = 0x1,

        /// <summary>
        /// Successfully loaded specification files.
        /// </summary>
        Success = 0x2,
    }
}

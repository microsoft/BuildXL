// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

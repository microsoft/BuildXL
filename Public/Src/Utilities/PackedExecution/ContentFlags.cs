// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Utilities.PackedExecution
{
    /// <summary>
    /// Flags relating to the origin and/or destination of content (files and directories) in the build.
    /// </summary>
    [Flags]
    public enum ContentFlags
    {
        /// <summary>
        /// Default value: nothing known.
        /// </summary>
        None = 0,

        /// <summary>
        /// The content is known to have been produced during the build.
        /// </summary>
        /// <remarks>
        /// In this case the content's ProducerPip should always be set. However, this data
        /// derives from different sources in the BXL execution log, so we represent both to allow
        /// checking for consistency when analyzing.
        /// </remarks>
        Produced = 1 << 0,

        /// <summary>
        /// The content is known to have been materialized from cache during the build.
        /// </summary>
        MaterializedFromCache = 1 << 1,

        /// <summary>
        /// The content is known to have been materialized during the build somehow, but not specifically from cache.
        /// </summary>
        Materialized = 1 << 2,
    }
}

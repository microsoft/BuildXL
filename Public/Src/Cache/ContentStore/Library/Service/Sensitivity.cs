// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Service
{
    /// <summary>
    ///     Session hint for sensitivity to resource usage.
    /// </summary>
    public enum Sensitivity
    {
        /// <summary>
        ///     Session is not sensitive.
        /// </summary>
        None = 0,

        /// <summary>
        ///     Session is sensitive to resource usage.
        /// </summary>
        Sensitive
    }
}

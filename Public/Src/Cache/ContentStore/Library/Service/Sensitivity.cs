// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

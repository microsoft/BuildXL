// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// The verbosity level for logging
    /// </summary>
    public enum ViewerMode : byte
    {
        /// <summary>
        /// The viewer won't be initialized
        /// </summary>
        Disable,

        /// <summary>
        /// The viewer will be initialized and a browser will pop up to point ot the build.
        /// </summary>
        Show,

        /// <summary>
        /// The viewer will be initialized and a user can point to the build using their browser.
        /// </summary>
        Hide,

        /// <summary>
        /// The viewer will be initialized but the browser will not start unless there is an error.
        /// </summary>
        ShowOnError,
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.ContentStore.Interfaces.Logging
{
    /// <summary>
    ///     Severity or impact of the message.
    /// </summary>
    public enum Severity
    {
        /// <summary>
        ///     Uninitialized
        /// </summary>
        Unknown,

        /// <summary>
        ///     Developer diagnostics
        /// </summary>
        Diagnostic,

        /// <summary>
        ///     Debugging
        /// </summary>
        Debug,

        /// <summary>
        ///     Some additional information
        /// </summary>
        Info,

        /// <summary>
        ///     Warning should be investigated soon
        /// </summary>
        Warning,

        /// <summary>
        ///     Error requires attention
        /// </summary>
        Error,

        /// <summary>
        ///     Unrecoverable error
        /// </summary>
        Fatal,

        /// <summary>
        ///     Expected messaging (i.e. stdout)
        /// </summary>
        Always
    }
}

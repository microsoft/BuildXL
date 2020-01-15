// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Cache.Analyzer
{
    /// <summary>
    /// Enumeration for possible errors retrieving content during analysis.
    /// </summary>
    public enum ContentError
    {
        /// <summary>
        /// No errors.
        /// </summary>
        None = 0,

        /// <summary>
        /// Analysis was unable to pin content from the session.
        /// </summary>
        UnableToPin = -1,

        /// <summary>
        /// Analysis was unable to stream content from the session.
        /// </summary>
        UnableToStream = -2,
    }
}

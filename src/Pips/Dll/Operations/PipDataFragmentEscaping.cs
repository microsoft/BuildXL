// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Enumeration representing the type of escaping needed for the values.
    /// </summary>
    public enum PipDataFragmentEscaping : byte
    {
        /// <summary>
        /// Invalid, default value.
        /// </summary>
        Invalid = 0,

        /// <summary>
        /// No need for escaping, just concatenate the values.
        /// </summary>
        NoEscaping,

        /// <summary>
        /// The Standard C Runtime parsing semantics.
        /// </summary>
        /// <remarks>
        /// See http://msdn.microsoft.com/en-us/library/a1y7w461.aspx for semantics.
        /// </remarks>
        CRuntimeArgumentRules,

        ///// <summary>
        ///// Encoded as argument for Cmd.exe.
        ///// </summary>
        ///// <remarks>
        ///// This inserts the funny ^ escapes in the right place.
        ///// </remarks>
        // CmdExe,
    }
}

// --------------------------------------------------------------------
//
// Copyright (c) Microsoft Corporation.  All rights reserved.
//
// ---------------

using System;

namespace BuildXL.Pips.Operations
{
    public partial class Process
    {
        /// <summary>
        /// Flag options controlling process pip behavior.
        /// </summary>
        [Flags]
        public enum UnsafeOptions : byte
        {
            /// <nodoc />
            None = 0,

            /// <summary>
            /// Treat double write errors as warnings (useful when using shared opaques where
            /// traditional BuildXL constraints need to be relaxed)
            /// </summary>
            /// TODO: Make BuildXLScript support this option
            DoubleWriteErrorsAreWarnings = 1 << 0,
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;

namespace BuildXL.Utilities.Serialization
{
    /// <todoc />
    public enum InlinedStringKind : byte
    {
        /// <todoc />
        Default = 0,

        /// <todoc />
        PipData = 1,
    }

}

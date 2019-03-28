// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Ipc.Common
{
    /// <nodoc/>
    public sealed class IpcResultTimestamp
    {
        /// <nodoc/>
        public DateTime Response_BeforeDeserializeTime { get; set; }

        /// <nodoc/>
        public DateTime Response_AfterDeserializeTime { get; set; }

        /// <nodoc/>
        public DateTime Response_BeforeSetTime { get; set; }

        /// <nodoc/>
        public DateTime Response_AfterSetTime { get; set; }
    }
}

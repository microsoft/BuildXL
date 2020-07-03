// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Ipc.Common
{
    /// <nodoc/>
    public sealed class IpcOperationTimestamp
    {
        #region DaemonSide

        /// <nodoc/>
        public DateTime Daemon_AfterReceivedTime { get; set; }

        /// <nodoc/>
        public DateTime Daemon_BeforeExecuteTime { get; set; }

        #endregion

        #region BuildXLSide

        /// <nodoc/>
        public DateTime Request_BeforeSendTime { get; set; }

        /// <nodoc/>
        public DateTime Request_AfterSendTime { get; set; }

        /// <nodoc/>
        public DateTime Request_BeforePostTime { get; set; }

        /// <nodoc/>
        public DateTime Request_AfterServerAckTime { get; set; }

        #endregion
    }
}

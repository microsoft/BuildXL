// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Runtime.Remoting.Messaging;

namespace Tool.ServicePipDaemon
{
    /// <summary>
    /// Generic DropDaemon exception.
    /// </summary>
    public sealed class DaemonException : Exception
    {
        /// <nodoc/>
        public DaemonException(string message)
            : base(message) { }
    }
}

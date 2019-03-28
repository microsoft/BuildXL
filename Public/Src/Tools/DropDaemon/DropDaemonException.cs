// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace Tool.DropDaemon
{
    /// <summary>
    /// Generic DropDaemon exception.
    /// </summary>
    public sealed class DropDaemonException : Exception
    {
        /// <nodoc/>
        public DropDaemonException(string message)
            : base(message) { }
    }
}

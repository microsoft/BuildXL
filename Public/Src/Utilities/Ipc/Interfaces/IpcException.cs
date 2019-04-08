// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Globalization;

namespace BuildXL.Ipc.Interfaces
{
    /// <summary>
    /// Used for serialization/deserialization related exceptions.
    /// </summary>
    public sealed class IpcException : Exception
    {
        /// <summary>
        /// Exception kind.
        /// </summary>
        /// <remarks>
        /// These kinds are not necessarily very descriptive; some of them
        /// may be very low-level, and as such used primarily for testing.
        /// </remarks>
        public enum IpcExceptionKind
        {
            /// <summary>Any serialization-related error.</summary>
            Serialization,

            /// <summary>Object is disposed before completed.</summary>
            DisposeBeforeCompletion,

            /// <summary>Received response cannot be assigned to a matching request.</summary>
            SpuriousResponse,

            /// <summary>Server object was started multiple times.</summary>
            MultiStart,

            /// <summary>Server object was started after it was stopped.</summary>
            StartAfterStop,

            /// <summary>Moniker of a wrong type was given to an IPC provider.</summary>
            InvalidMoniker,

            /// <summary>Generic error.</summary>
            GenericIpcError,
        }

        /// <nodoc/>
        public IpcExceptionKind Kind { get; }

        /// <nodoc/>
        public IpcException(IpcExceptionKind kind)
        {
            Kind = kind;
        }

        /// <nodoc/>
        public IpcException(IpcExceptionKind kind, string error)
            : base(error)
        {
            Kind = kind;
        }

        /// <nodoc/>
        public IpcException(IpcExceptionKind kind, string format, params object[] args)
            : this(kind, args.Length > 0 ? string.Format(CultureInfo.CurrentCulture, format, args) : format)
        {
        }

        /// <nodoc/>
        public override string ToString()
        {
            return "IpcExceptionKind: '" + Kind + "'. " + base.ToString();
        }
    }
}

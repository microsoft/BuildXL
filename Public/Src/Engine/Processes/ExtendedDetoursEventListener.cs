// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Processes
{
    /// <summary>
    /// Extends <see cref="IDetoursEventListener"/> to provide extra sandbox information
    /// </summary>
    /// <remarks>
    /// We current have external dependencies consuming <see cref="IDetoursEventListener"/>, so any addition there will be a breaking change.
    /// </remarks>
    public abstract class ExtendedDetoursEventListener: IDetoursEventListener
    {
        /// <summary>
        /// Represents an infrastructure related event coming from the sandbox
        /// </summary>
        /// <remarks>
        /// Used to indicate any error/warning that occurred during the lifetime of the sandbox related to a sandbox internal/infrastructure issue 
        /// </remarks>
        public struct SandboxInfraMessage
        {
            /// <nodoc/>
            public long PipId { get; set; }

            /// <nodoc/>
            public string PipDescription { get; set; }

            /// <nodoc/>
            public string Message { get; set; }

            /// <nodoc/>
            public SandboxInfraSeverity Severity { get; set; }
        }

        /// <summary>
        /// Called to handle a <see cref="SandboxInfraMessage"/>
        /// </summary>
        public abstract void HandleSandboxInfraMessage(SandboxInfraMessage sandboxMessage);
    }
}
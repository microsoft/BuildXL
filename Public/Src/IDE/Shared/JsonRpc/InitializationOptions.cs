// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.Serialization;

namespace BuildXL.Ide.JsonRpc
{
    /// <summary>
    /// Identifies the type of client that launched the server process.
    /// This is used mainly for telemetry purposes.
    /// </summary>
    public enum ClientType
    {
        /// <summary>
        /// The default value if not specified.
        /// </summary>
        Unknown,

        /// <summary>
        /// Visual Studio Code
        /// </summary>
        VisualStudioCode,

        /// <summary>
        /// Visual Studio
        /// </summary>
        VisualStudio
    }

    /// <summary>
    /// Initialization options sent along with the Initialize request.
    /// </summary>
    [DataContract]
    public sealed class InitializationOptions
    {
        /// <summary>
        /// The client type that launched the server instance (e.g. VS Code, Visual Studio etc)
        /// </summary>
        [DataMember(Name = "clientType")]
        public ClientType ClientType { get; set; }
    }
}

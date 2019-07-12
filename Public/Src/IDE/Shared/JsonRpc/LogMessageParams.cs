// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.Tracing;
using System.Runtime.Serialization;

namespace BuildXL.Ide.JsonRpc
{
    /// <summary>
    /// Represents the log message sent to IDE (VSCode and VS)
    /// </summary>
    /// <remarks>
    /// This must be kept in sync with the VSCode and VS extensions.
    /// {vscode extension location} Public\Src\FrontEnd\IDE\VsCode\client\src\outputTracer.ts
    /// </remarks>
    [DataContract]
    public sealed class LogMessageParams
    {
        /// <summary>
        /// The event level of the message.
        /// </summary>
        [DataMember(Name = "level")]
        public EventLevel EventLevel { get; set; }

        /// <summary>
        /// The log message.
        /// </summary>
        [DataMember(Name = "message")]
        public string Message { get; set; }

        /// <summary>
        /// Creates an instance of LogMessageParams.
        /// </summary>
        /// <param name="level">The event level of the message.</param>
        /// <param name="message">The log message.</param>
        public static LogMessageParams Create(EventLevel level, string message) => new LogMessageParams() { EventLevel = level, Message = message };
    }
}

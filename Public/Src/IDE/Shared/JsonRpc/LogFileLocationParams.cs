// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.Serialization;

namespace BuildXL.Ide.JsonRpc
{
    /// <summary>
    /// Used to send the log file location to the IDE client.
    /// </summary>
    /// <remarks>
    /// This allows clients to decide to open the DScript log file inside the editor directly
    /// rather than executing the "openLogFile" command on the server which launches a new process
    /// This must be kept in sync with the VSCode and VS extensions.
    /// {vscode extension location} Public\Src\FrontEnd\IDE\VsCode\client\src\logFileNotification.ts
    /// </remarks>
    [DataContract]
    public sealed class LogFileLocationParams
    {
        /// <summary>
        /// Contains the file path to the log file that is in use by the language server.
        /// </summary>
        /// <remarks>
        /// This notification is sent from the language server to the IDE.
        /// 
        /// Note that this is sent as a file path string versus a URI due to the fact that clients
        /// such as VSCode have different constructs for URI than .Net does. Trying to coerce 
        /// the two versions to "play nicely" together isn't really necessary.
        /// </remarks>
        [DataMember(Name = "file")]
        public string File { get; set; }
    }
}

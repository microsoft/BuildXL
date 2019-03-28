// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.Serialization;

namespace BuildXL.Ide.JsonRpc
{
    /// <summary>
    /// The parameters for the "dscript/addSourceFileToProject" request.
    /// </summary>
    [DataContract]
    public sealed class AddSourceFileToProjectParams
    {
        /// <summary>
        /// The project spec file that will have the source file added to it.
        /// </summary>
        [DataMember(Name = "projectSpecFileName")]
        public string ProjectSpecFileName { get; set; }

        /// <summary>
        /// The relateive source file path to add the project.
        /// </summary>
        [DataMember(Name = "relativeSourceFilePath")]
        public string RelativeSourceFilePath { get; set; }
    }
}

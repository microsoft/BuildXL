// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;

namespace BuildXL.Ide.JsonRpc
{
    /// <summary>
    /// The parameters for the "dscript/sourceFileConfiguration" notification.
    /// </summary>
    [DataContract]
    public sealed class AddSourceFileConfigurationParams
    {
        /// <summary>
        /// The set of configurations needed for adding a source file
        /// </summary>
        /// <remarks>
        /// Each configuration can be different. For example, adding a source file to a DLL
        /// can be different than adding a source file to a static link library.
        /// </remarks>
        [DataMember(Name = "configurations")]
        public AddSourceFileConfiguration[] Configurations { get; set; }
    }
}

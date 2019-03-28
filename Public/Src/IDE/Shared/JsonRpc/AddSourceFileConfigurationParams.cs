// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

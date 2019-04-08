// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.Serialization;

namespace BuildXL.Ide.JsonRpc
{
    /// <summary>
    /// Represents a spec file known to the BuildXL workspace.
    /// </summary>
    [DataContract]
    public sealed class SpecDescriptor
    {
        /// <summary>
        /// The numeric identifier of a spec.
        /// </summary>
        [DataMember(Name = "id")]
        public int Id { get; set; }

        /// <summary>
        /// The file name for the spec file.
        /// </summary>
        [DataMember(Name = "fileName")]
        public string FileName { get; set; }
    }
}

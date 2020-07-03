// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;

namespace BuildXL.Ide.JsonRpc
{
    /// <summary>
    /// Represents the result from the "dscript/specsForModule" request.
    /// </summary>
    [DataContract]
    public sealed class SpecsFromModuleResult
    {
        /// <summary>
        /// The array of specs present in the BuildXL module.
        /// </summary>
        [DataMember(Name = "specs")]
        public SpecDescriptor[] Specs { get; set; }
    };
}

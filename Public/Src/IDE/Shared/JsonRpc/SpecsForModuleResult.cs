// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;

namespace BuildXL.Ide.JsonRpc
{
    /// <summary>
    /// The parameters for the "dscript/specsForModule" request.
    /// </summary>
    [DataContract]
    public sealed class SpecsForModuleParams
    {
        /// <summary>
        ///  The module identifier to return the spec for.
        /// </summary>
        [DataMember(Name = "moduleDescriptor")]
        public ModuleDescriptor ModuleDescriptor { get; set; }
    }
}

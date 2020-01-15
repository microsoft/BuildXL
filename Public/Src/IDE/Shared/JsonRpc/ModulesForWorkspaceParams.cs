// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Runtime.Serialization;

namespace BuildXL.Ide.JsonRpc
{
    /// <summary>
    /// Parameters for "dscript/modulesForWorkspace" JSON-RPC request.
    /// </summary>
    [DataContract]
    public sealed class ModulesForWorkspaceParams
    {
        /// <summary>
        /// Indicates whether or not to include special configuration modules
        /// 
        /// </summary>
        [DataMember(Name = "includeSpecialConfigurationModules")]
        public bool IncludeSpecialConfigurationModules { get; set; }

    }
}

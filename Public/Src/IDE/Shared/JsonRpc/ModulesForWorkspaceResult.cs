// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.Serialization;

namespace BuildXL.Ide.JsonRpc
{
    /// <summary>
    /// Represents the result of the "dscript/modulesForWorkspace" JSON RPC request.
    /// </summary>
    [DataContract]
    public sealed class ModulesForWorkspaceResult
    {
        /// <summary>
        /// Array of modules present in the BuildXL workspace.
        /// </summary>
        [DataMember(Name = "modules")]
        public ModuleDescriptor[] Modules { get; set; }
    };
}

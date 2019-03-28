// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.Serialization;

namespace BuildXL.Ide.JsonRpc
{
    /// <summary>
    /// Contains the information that identifies a module in BuildXL.
    /// </summary>
    [DataContract]
    public sealed class ModuleDescriptor
    {
        /// <summary>
        /// Represents the numeric identifier of the module known to the BuildXL workspace.
        /// </summary>
        [DataMember(Name = "id")]
        public int Id { get; set; }

        /// <summary>
        /// The name of the module as defined in the module definition.
        /// </summary>
        [DataMember(Name = "name")]
        public string Name { get; set; }

        /// <summary>
        /// The configuration file for the module.
        /// </summary>
        [DataMember(Name = "configFilename")]
        public string ConfigFilename { get; set; }

        /// <summary>
        /// The version of the module
        /// </summary>
        [DataMember(Name = "version")]
        public string Version { get; set; }

        /// <summary>
        /// The name of the module as defined in the module definition.
        /// </summary>
        [DataMember(Name = "resolverKind")]
        public string ResolverKind { get; set; }
        
        /// <summary>
        /// The name of the module as defined in the module definition.
        /// </summary>
        [DataMember(Name = "resolverName")]
        public string ResolverName { get; set; }
    }
}

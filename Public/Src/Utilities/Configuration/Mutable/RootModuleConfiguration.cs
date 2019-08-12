// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public class RootModuleConfiguration : ModuleConfiguration, IRootModuleConfiguration
    {
        /// <nodoc />
        public RootModuleConfiguration()
        {
            Name = "<Global>";
            ModulePolicies = new Dictionary<ModuleId, IModuleConfiguration>();
            SearchPathEnumerationTools = new List<RelativePath>();
            IncrementalTools = new List<RelativePath>();
        }

        /// <nodoc />
        public RootModuleConfiguration(IRootModuleConfiguration template, PathRemapper pathRemapper)
            : base(template, pathRemapper)
        {
            Contract.Assume(template != null);
            Contract.Assume(pathRemapper != null);

            ModulePolicies = new Dictionary<ModuleId, IModuleConfiguration>();
            SearchPathEnumerationTools = new List<RelativePath>(
                template.SearchPathEnumerationTools.Select(pathRemapper.Remap));
            IncrementalTools = new List<RelativePath>(
                template.IncrementalTools.Select(pathRemapper.Remap));

            foreach (var module in template.ModulePolicies.Values)
            {
                ModulePolicies.Add(module.ModuleId, new ModuleConfiguration(module, pathRemapper));
            }
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public Dictionary<ModuleId, IModuleConfiguration> ModulePolicies { get; set; }

        /// <inheritdoc />
        IReadOnlyDictionary<ModuleId, IModuleConfiguration> IRootModuleConfiguration.ModulePolicies => ModulePolicies;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<RelativePath> SearchPathEnumerationTools { get; set; }

        /// <inheritdoc />
        IReadOnlyList<RelativePath> IRootModuleConfiguration.SearchPathEnumerationTools => SearchPathEnumerationTools;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<RelativePath> IncrementalTools { get; set; }

        /// <inheritdoc />
        IReadOnlyList<RelativePath> IRootModuleConfiguration.IncrementalTools => IncrementalTools;
    }
}

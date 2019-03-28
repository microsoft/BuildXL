// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Visualization.Models
{
    /// <summary>
    /// Viewmodel that represents a module
    /// </summary>
    public sealed class Module : ModuleReference
    {
        private readonly List<ModuleReference> m_dependencies = new List<ModuleReference>();
        private readonly List<ModuleReference> m_dependents = new List<ModuleReference>();
        private readonly List<ValueReference> m_values = new List<ValueReference>();
        private readonly List<FileReference> m_specFiles = new List<FileReference>();

        /// <nodoc/>
        public Module(ModulePip module, PathTable pathTable)
            : base(module, pathTable.StringTable)
        {
            Location = Location.FromToken(pathTable, module.Location);
        }

        /// <summary>
        /// The definition location of the module
        /// </summary>
        public Location Location { get; set; }

        /// <summary>
        /// The dependencies of the module
        /// </summary>
        public List<ModuleReference> Dependencies => m_dependencies;

        /// <summary>
        /// The modules that are dependent upon this module.
        /// </summary>
        public List<ModuleReference> Dependents => m_dependents;

        /// <summary>
        /// The spec files that make up this Module
        /// </summary>
        public List<FileReference> SpecFiles => m_specFiles;

        /// <summary>
        /// The values that got evaluated in this Module
        /// </summary>
        public List<ValueReference> Values => m_values;
    }
}

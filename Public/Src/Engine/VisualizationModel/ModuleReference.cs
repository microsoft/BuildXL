// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips.Operations;
using BuildXL.Utilities;

namespace BuildXL.Visualization.Models
{
    /// <summary>
    /// ViewModel that Represents a reference to a module
    /// </summary>
    public class ModuleReference
    {
        /// <summary>
        /// The id of the referenced module
        /// </summary>
        public int Id { get; set; }

        /// <summary>
        /// Tha nem of the referenced module
        /// </summary>
        public string Name { get; set; }

        /// <nodoc/>
        public ModuleReference(ModulePip module, StringTable stringTable)
        {
            Id = (int)module.PipId.Value;
            Name = module.Identity.ToString(stringTable);
        }
    }
}

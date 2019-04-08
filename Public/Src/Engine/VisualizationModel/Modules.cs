// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace BuildXL.Visualization.Models
{
    /// <summary>
    /// Viewmodel of all modules
    /// </summary>
    public sealed class Modules
    {
        private readonly List<ModuleReference> m_all = new List<ModuleReference>();

        /// <summary>
        /// All modules
        /// </summary>
        public List<ModuleReference> All => m_all;
    }
}

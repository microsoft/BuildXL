// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <inheritdoc/>
    public class InlineModuleDefinition : IInlineModuleDefinition
    {
        /// <nodoc />
        public InlineModuleDefinition()
        {
        }

        /// <nodoc />
        public InlineModuleDefinition(IInlineModuleDefinition template)
        {
            ModuleName = template.ModuleName;
            Projects = template.Projects;
        }

        /// <inheritdoc/>
        public string ModuleName { get; set; }

        /// <inheritdoc/>
        public IReadOnlyList<AbsolutePath> Projects { get; set; }
    }
}

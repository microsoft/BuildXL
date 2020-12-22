// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// A module definition that doesn't require a module.config.dsc file
    /// </summary>
    public interface IInlineModuleDefinition
    {
        /// <nodoc/>
        string ModuleName { get; }

        /// <nodoc/>
        IReadOnlyList<AbsolutePath> Projects { get; }
    }
}

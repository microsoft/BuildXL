// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.FrontEnd.Sdk.Workspaces
{
    /// <summary>
    /// Interface for the workspace.
    /// </summary>
    /// <remarks>
    /// All interfaces in this folder are extremely lightweight.
    /// The main purpose for them is not to provide abstraction, but to decouple different application layers.
    /// The Sdk is extremely low in the dependency chain and should be as shallow and not specific as possible,
    /// and, most importantly, should not rely on some assemblies, like the one that provides a real workspace.
    /// </remarks>
    public interface IWorkspace
    {
        /// <summary>
        /// Returns all the specs that belongs to DScript V2 modules.
        /// </summary>
        IReadOnlySet<AbsolutePath> GetSpecFilesWithImplicitNameVisibility();

        /// <summary>
        /// Returns all the spec of a workspace.
        /// </summary>
        IReadOnlySet<AbsolutePath> GetAllSpecFiles();
    }
}

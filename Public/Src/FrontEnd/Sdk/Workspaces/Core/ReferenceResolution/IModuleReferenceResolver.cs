// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities;
using JetBrains.Annotations;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Can distinguish internal vs external references in a source file and update the source file resolved modules accordingly.
    /// </summary>
    public interface IModuleReferenceResolver
    {
        /// <summary>
        /// Returns a collection of external module names from  <param name="sourceFile"/>
        /// </summary>
        [NotNull]
        IEnumerable<ModuleReferenceWithProvenance> GetExternalModuleReferences([NotNull]ISourceFile sourceFile);

        /// <summary>
        /// Tries to update <param name="sourceFile"/> resolved modules with <param name="externalModuleReference"/>. If that fails
        /// <param name="failure"/> contains the failure details.
        /// </summary>
        bool TryUpdateExternalModuleReference([NotNull]ISourceFile sourceFile, [NotNull]ModuleDefinition externalModuleReference, out Failure failure);

        /// <summary>
        /// Tries to update <param name="sourceFile"/> with owning module <param name="owningModule"/> with all the internal module references. If that fails
        /// <param name="failures"/> contains the failure details.
        /// </summary>
        bool TryUpdateAllInternalModuleReferences([NotNull]ISourceFile sourceFile, [NotNull]ModuleDefinition owningModule, out Failure[] failures);
    }

}

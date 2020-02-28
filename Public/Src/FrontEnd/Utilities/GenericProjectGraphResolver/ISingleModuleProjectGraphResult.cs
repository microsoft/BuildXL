// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.FrontEnd.Workspaces.Core;

namespace BuildXL.FrontEnd.Utilities.GenericProjectGraphResolver
{
    /// <summary>
    /// Project graph results for resolvers that group all specs into a single module
    /// </summary>
    public interface ISingleModuleProjectGraphResult
    {
        /// <nodoc/>
        public ModuleDefinition ModuleDefinition { get; }
    }
}

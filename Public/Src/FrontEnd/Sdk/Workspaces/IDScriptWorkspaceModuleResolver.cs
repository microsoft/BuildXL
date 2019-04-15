// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using JetBrains.Annotations;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Sdk.Workspaces
{
    /// <summary>
    /// A DScript-specific workspace module resolver. DScript workspace resolvers
    /// need an extra initialization step since not all context objects are available at
    /// construction time.
    /// </summary>
    public interface IDScriptWorkspaceModuleResolver
    {
        /// <summary>
        /// Initializes the workspace resolver
        /// </summary>
        bool TryInitialize(
            [NotNull]FrontEndHost host, 
            [NotNull]FrontEndContext context, 
            [NotNull]IConfiguration configuration, 
            [NotNull]IResolverSettings resolverSettings,
            [NotNull]QualifierId[] requestedQualifiers);
    }
}

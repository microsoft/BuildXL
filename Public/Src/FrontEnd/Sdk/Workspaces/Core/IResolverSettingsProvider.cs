// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using JetBrains.Annotations;
using TypeScript.Net.Types;

namespace BuildXL.FrontEnd.Workspaces.Core
{
    /// <summary>
    /// Interface to extract <see cref="IResolverSettings"/> settings of a particular kind by parsing a resolver configuration file.
    /// </summary>
    public interface IResolverSettingsProvider
    {
        /// <summary>
        /// Parses <see cref="IResolverSettings"/> from an object literal of resolver settings in configuration file.
        /// </summary>
        /// <param name="resolverConfigurationLiteral">Object literal that represents the resolver settings in the configuration file.</param>
        Possible<IResolverSettings> TryGetResolverSettings([NotNull]IObjectLiteralExpression resolverConfigurationLiteral);
    }
}

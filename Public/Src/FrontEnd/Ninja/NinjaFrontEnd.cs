// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;

namespace BuildXL.FrontEnd.Ninja
{
    /// <summary>
    /// Resolver frontend that can schedule Ninja projects
    /// </summary>
    public sealed class NinjaFrontEnd : IFrontEnd
    {
        /// <nodoc />
        public const string Name = NinjaWorkspaceResolver.NinjaResolverName;

        private FrontEndContext m_context;
        private FrontEndHost m_host;

        /// <nodoc/>
        public NinjaFrontEnd()
        {
        }

        /// <inheritdoc/>
        public IReadOnlyCollection<string> SupportedResolvers => new[] { NinjaWorkspaceResolver.NinjaResolverName };

        /// <inheritdoc/>
        public IResolver CreateResolver(string kind)
        {
            Contract.Requires(kind == KnownResolverKind.NinjaResolverKind);

            return new NinjaResolver(m_host, m_context, Name);
        }

        /// <inheritdoc/>
        public void InitializeFrontEnd(FrontEndHost host, FrontEndContext context, IConfiguration configuration)
        {
            m_host = host;
            m_context = context;
        }

        /// <inheritdoc/>
        public void LogStatistics(Dictionary<string, long> statistics)
        {
        }
    }
}

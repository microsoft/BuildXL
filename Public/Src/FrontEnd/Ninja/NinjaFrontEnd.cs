// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Core;
using BuildXL.FrontEnd.Script;
using BuildXL.FrontEnd.Script.Evaluator;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities.Configuration;
using Logger = BuildXL.FrontEnd.Script.Tracing.Logger;

namespace BuildXL.FrontEnd.Ninja
{
    /// <summary>
    /// Resolver frontend that can schedule Ninja projects
    /// </summary>
    public sealed class NinjaFrontEnd : DScriptInterpreterBase, IFrontEnd
    {
        /// <nodoc/>
        public NinjaFrontEnd(
            GlobalConstants constants,
            ModuleRegistry sharedModuleRegistry,
            IFrontEndStatistics statistics,
            Logger logger = null)
            : base(constants, sharedModuleRegistry, statistics, logger)
        {
            Name = nameof(NinjaFrontEnd);
        }

        /// <inheritdoc/>
        public IReadOnlyCollection<string> SupportedResolvers => new[] { NinjaWorkspaceResolver.NinjaResolverName };

        /// <inheritdoc/>
        public IResolver CreateResolver(string kind)
        {
            Contract.Requires(kind == KnownResolverKind.NinjaResolverKind);

            return new NinjaResolver(
                Constants,
                SharedModuleRegistry,
                FrontEndStatistics,
                FrontEndHost,
                Context,
                Configuration,
                Logger,
                Name);
        }

        /// <inheritdoc/>
        public void InitializeFrontEnd(FrontEndHost host, FrontEndContext context, IConfiguration configuration)
        {
            InitializeInterpreter(host, context, configuration);
        }

        /// <inheritdoc/>
        public void LogStatistics(Dictionary<string, long> statistics)
        {
        }
    }
}

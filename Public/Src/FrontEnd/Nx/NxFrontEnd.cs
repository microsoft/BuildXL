// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Workspaces.Core;

namespace BuildXL.FrontEnd.Nx
{
    /// <summary>
    /// Resolver frontend that can schedule Nx projects.
    /// </summary>
    public sealed class NxFrontEnd : FrontEnd<NxWorkspaceResolver>
    {
        /// <summary>
        /// Environment variable to turn off the Nx daemon
        /// </summary>
        /// <remarks>
        /// <see href="https://nx.dev/docs/concepts/nx-daemon#turning-it-off"/>
        /// </remarks>
        public static (string key, string value) NxDaemonOffEnvVar { get; } = ("NX_DAEMON", "false");

        /// <summary>
        /// Environment variable to turn off the Nx database
        /// </summary>
        /// <remarks>
        /// Not officially documented. Nx uses an internal database to store file caching related activity. However, this database is not designed to be used concurrently,
        /// and since BuildXL may run multiple pips in parallel, this leads to races. At the same time, the DB is not really needed for BuildXL scenarios since we use Nx as a pure executor.
        /// </remarks>
        public static (string key, string value) NxDBOffEnvVar { get; } = ("NX_DISABLE_DB", "true");

        /// <summary>
        /// Environment variable to point to a specific socket directory
        /// </summary>
        /// <remarks>
        /// This needs to be set to a short path, since otherwise we may get a 'Attempted to open socket that exceeds the maximum socket length.'. Without
        /// an explicit value, Nx will use a temp folder which is typically very long under BuildXL, since we use a per-pip temp folder.
        /// <see href="https://github.com/nrwl/nx/issues/27725"/> 
        /// </remarks>
        public static string NxSocketDirVar { get; } = "NX_SOCKET_DIR";

        /// <inheritdoc />
        public override string Name => KnownResolverKind.NxResolverKind;

        /// <inheritdoc />
        public override bool ShouldRestrictBuildParameters => false;

        /// <inheritdoc/>
        public override IReadOnlyCollection<string> SupportedResolvers => new[] { KnownResolverKind.NxResolverKind };

        /// <inheritdoc/>
        public override IResolver CreateResolver(string kind)
        {
            return new NxResolver(Host, Context, Name);
        }
    }
}

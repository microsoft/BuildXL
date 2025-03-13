// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;

namespace BuildToolsInstaller.Utilities
{
    internal sealed class NugetHelper
    {
        /// <nodoc />
        public static SourceRepository CreateSourceRepository(string feedUrl)
        {
            var packageSource = new PackageSource(feedUrl, "SourceFeed");

            // Because the feed is known (either the well-known mirror or the user-provided override),
            // we can simply use a PAT that we assume will grant the appropriate privileges instead of going through a credential provider.
            packageSource.Credentials = new PackageSourceCredential(feedUrl, "IrrelevantUsername", GetPatFromEnvironment(), true, string.Empty);
            return Repository.Factory.GetCoreV3(packageSource);
        }

        /// <summary>
        /// Get latest version of a package
        /// </summary>
        public static async Task<NuGetVersion?> GetLatestVersionAsync(SourceRepository repository, string packageName, ILogger logger)
        {
            var resource = await repository.GetResourceAsync<FindPackageByIdResource>();
            var versions = await resource.GetAllVersionsAsync(packageName, new SourceCacheContext(), new NugetLoggerAdapter(logger), CancellationToken.None);
            return versions?.Max(); // Relying on SemanticVersion comparison here
        }

        /// <summary>
        /// Construct the implicit source repository for installers, a well-known feed that should be installed in the organization
        /// </summary>
        public static string InferSourceRepository(IAdoService adoService)
        {
            if (!adoService.IsEnabled)
            {
                throw new InvalidOperationException("Automatic source repository inference is only supported when running on an ADO Build");
            }

            if (!adoService.TryGetOrganizationName(out var adoOrganizationName))
            {
                throw new InvalidOperationException("Could not retrieve organization name");
            }

            // This feed is installed in every organization as part of 1ESPT onboarding
            // TODO: Change Guardian feed to 1ESTools feed?
            return $"https://pkgs.dev.azure.com/{adoOrganizationName}/_packaging/Guardian1ESPTUpstreamOrgFeed/nuget/v3/index.json";
        }

        private static string GetPatFromEnvironment()
        {
            return Environment.GetEnvironmentVariable("BUILDTOOLSDOWNLOADER_NUGET_PAT") ?? Environment.GetEnvironmentVariable("SYSTEM_ACCESSTOKEN") ?? "";
        }
    }
}

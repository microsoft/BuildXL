// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using NuGet.Configuration;
using NuGet.Packaging.Signing;
using NuGet.Packaging;
using NuGet.Protocol.Core.Types;
using System.Diagnostics;
using NuGet.Versioning;

namespace BuildToolsInstaller.Utilities
{
    /// <nodoc />
    internal sealed class NugetDownloader : INugetDownloader
    {
        /// <summary>
        /// Attempts to download and extract a NuGet package to disk.
        /// </summary>
        public async Task<bool> TryDownloadNugetToDiskAsync(SourceRepository sourceRepository, string package, NuGetVersion version, string downloadLocation, ILogger logger)
        {
            logger.Info($"Download started for package '{package}' and version '{version}' to '{downloadLocation}'.");

            var stopwatch = Stopwatch.StartNew();
            var cache = new SourceCacheContext();

            bool found = false;
            NuGet.Common.ILogger nugetLogger = new NugetLoggerAdapter(logger);
            logger.Info($"Finding resource against source repository '{sourceRepository.PackageSource.Name}={sourceRepository.PackageSource.SourceUri}'. Authentication is on: {sourceRepository.PackageSource.Credentials != null}");

            FindPackageByIdResource resource = await sourceRepository.GetResourceAsync<FindPackageByIdResource>();

            if (resource == null)
            {
                logger.Info($"Resource under source repository '{sourceRepository.PackageSource.Name}={sourceRepository.PackageSource.SourceUri}' not found.");
                return false;
            }

            var destinationFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            using (var packageStream = new FileStream(
                destinationFilePath,
                FileMode.Create,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096))
            {
                if (await resource.CopyNupkgToStreamAsync(
                    package,
                    version,
                    packageStream,
                    cache,
                    nugetLogger,
                    CancellationToken.None))
                {
                    found = true;

                    await PackageExtractor.ExtractPackageAsync(
                        sourceRepository.PackageSource.Source,
                        packageStream,
                        new PackagePathResolver(downloadLocation),
                        new PackageExtractionContext(
                            PackageSaveMode.Files,
                            XmlDocFileSaveMode.None,
                            ClientPolicyContext.GetClientPolicy(NullSettings.Instance, nugetLogger),
                            nugetLogger),
                        CancellationToken.None);

                    File.Delete(destinationFilePath);
                }
            }

            if (!found)
            {
                logger.Error($"Could not find NuGet package '{package}' with version '{version}' under any of the provided repositories.");
                return false;
            }

            logger.Info($"Finished downloading package '{package}' with version '{version}' in {stopwatch.ElapsedMilliseconds}ms");

            return true;
        }
    }
}

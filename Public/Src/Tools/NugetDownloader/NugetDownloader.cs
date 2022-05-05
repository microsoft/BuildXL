// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Native.IO;
using BuildXL.ToolSupport;
using BuildXL.Utilities;
using BuildXL.Utilities.VstsAuthentication;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Packaging.Signing;
using NuGet.Protocol.Core.Types;
using NugetDownloader;

namespace Tool.Download
{
    /// <summary>
    /// Downloads a Nuget into a given directory from a collection of source repositories
    /// </summary>
    internal sealed class NugetDownloader : ToolProgram<NugetDownloaderArgs>
    {
        private NugetDownloader() : base("NugetDownloader")
        {
        }

        /// <nodoc />
        public static int Main(string[] arguments)
        {
            return new NugetDownloader().MainHandler(arguments);
        }

        /// <inheritdoc />
        public override bool TryParse(string[] rawArgs, out NugetDownloaderArgs arguments)
        {
            try
            {
                arguments = new NugetDownloaderArgs(rawArgs);
                return true;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.GetLogEventMessage());
                arguments = null;
                return false;
            }
        }

        /// <inheritdoc />
        public override int Run(NugetDownloaderArgs arguments)
        {
            return TryDownloadNugetToDiskAsync(arguments).GetAwaiter().GetResult();
        }

        /// <summary>
        /// Attempts to download and extract a NuGet package to disk.
        /// </summary>
        private async Task<int> TryDownloadNugetToDiskAsync(NugetDownloaderArgs arguments)
        {
            Console.WriteLine($"Download started for package '{arguments.Id}' and version '{arguments.Version}' to '{arguments.DownloadDirectory}'.");

            var stopwatch = Stopwatch.StartNew();

            SourceCacheContext cache = new SourceCacheContext();

            var authLogger = new StringBuilder();
            var maybeRepositories = await VSTSAuthenticationHelper.TryCreateSourceRepositories(arguments.Repositories.Select(kvp => (kvp.Key, kvp.Value)), CancellationToken.None, authLogger);

            // Informational data
            Console.WriteLine(authLogger.ToString());

            if (!maybeRepositories.Succeeded)
            {
                Console.Error.WriteLine($"Failed to access the specified repositories: " + maybeRepositories.Failure.Describe());
                return 1;
            }

            bool found = false;
            ILogger logger = new ConsoleLogger();
            foreach (var sourceRepository in maybeRepositories.Result)
            {
                Console.Write($"Finding resource against source repository '{sourceRepository.PackageSource.Name}={sourceRepository.PackageSource.SourceUri}'. Authentication is on: {sourceRepository.PackageSource.Credentials != null}");

                // TODO: maybe we need a retry here? or define a default retry in the DScript SDK?
                FindPackageByIdResource resource = await sourceRepository.GetResourceAsync<FindPackageByIdResource>();

                if (resource == null)
                {
                    Console.Write($"Resource under source repository '{sourceRepository.PackageSource.Name}={sourceRepository.PackageSource.SourceUri}' not found.");
                    continue;
                }

                var destinationFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

                // We don't really need to materialize the nupkg file on disk, but a memory stream has a 2GB limitation, and NuGet packages
                // can be larger than that. Just place it on disk and remove it after we are done extracting.
                using (var packageStream = new FileStream(
                    destinationFilePath,
                    FileMode.Create,
                    FileAccess.ReadWrite,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096))
                {
                    if (await resource.CopyNupkgToStreamAsync(
                        arguments.Id,
                        arguments.Version,
                        packageStream,
                        cache,
                        logger,
                        CancellationToken.None))
                    {
                        found = true;

                        await PackageExtractor.ExtractPackageAsync(
                            sourceRepository.PackageSource.Source,
                            packageStream,
                            new PackagePathResolver(arguments.DownloadDirectory),
                            new PackageExtractionContext(
                                PackageSaveMode.Files | PackageSaveMode.Nuspec, 
                                XmlDocFileSaveMode.None, 
                                ClientPolicyContext.GetClientPolicy(NullSettings.Instance, logger), 
                                logger),
                            CancellationToken.None);

                        FileUtilities.DeleteFile(destinationFilePath);

                        break;
                    }
                }
            }

            if (!found)
            {
                Console.Error.WriteLine($"Could not find NuGet package '{arguments.Id}' with version '{arguments.Version.ToNormalizedString()}' under any of the provided repositories.");
                return 1;
            }

            Console.WriteLine($"Finished downloading package '{arguments.Id}' with version '{arguments.Version}' in {stopwatch.ElapsedMilliseconds}ms");

            return 0;
        }
    }
}

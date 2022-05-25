// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.FrontEnd.Nuget.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Configuration;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.VstsAuthentication;
using NuGet.Configuration;
using NuGet.Packaging;
using NuGet.Protocol;

namespace BuildXL.FrontEnd.Nuget
{
    /// <summary>
    /// Retrieves NuGet package layouts and specs from a set of feeds
    /// </summary>
    /// <remarks>
    /// Packages are not necessarily downloaded in full in order to inspect them, and partial downloads are always attempted first
    /// </remarks>
    public class NugetPackageInspector
    {
        // A 5K chunk looks reasonable as the initial download: most package central directory fits in 5K, so no extra requests are needed
        private const long MinimalChunkSizeInBytes = 5_000;
        // On every partial download iteration we linearly grow using a 20K base (i.e. 20, 40, 60, 80, etc.)
        private const long IncrementChunkSizeInBytes = 20_000;

        private readonly CancellationToken m_cancellationToken;
        private readonly LoggingContext m_loggingContext;
        private readonly IEnumerable<(string repositoryName, Uri repositoryUri)> m_repositories;
        private readonly StringTable m_stringTable;
        private readonly Func<Possible<string>> m_discoverCredentialProvider;
        private IReadOnlyDictionary<Uri, PackageSourceCredential> m_packageBaseAddress;
        private readonly Lazy<Task<Possible<bool>>> m_initializationResult;

        /// <nodoc/>
        public NugetPackageInspector(
            IEnumerable<(string repositoryName, Uri repositoryUri)> repositories, 
            StringTable stringTable, 
            Func<Possible<string>> discoverCredentialProvider, 
            CancellationToken cancellationToken, 
            Utilities.Instrumentation.Common.LoggingContext loggingContext)
        {
            Contract.RequiresNotNull(repositories);
            Contract.RequiresNotNull(discoverCredentialProvider);

            m_cancellationToken = cancellationToken;
            m_loggingContext = loggingContext;
            m_repositories = repositories;
            m_stringTable = stringTable;
            m_discoverCredentialProvider = discoverCredentialProvider;

            m_initializationResult = new Lazy<Task<Possible<bool>>>(async () =>
            {

                var logger = new StringBuilder();

                var result = await TryInitializeAsync(isRetry: false, logger);
                if (result.Succeeded && result.Result)
                {
                    return result;
                }

                logger.AppendLine("Failed at initializing the specified source repositories. Retrying.");
                
                return await TryInitializeAsync(isRetry: true, logger);
            },
            LazyThreadSafetyMode.ExecutionAndPublication);
        }

        private async Task<Possible<bool>> TryInitializeAsync(bool isRetry, StringBuilder logger)
        {
            try
            {
                return (await VSTSAuthenticationHelper.TryCreateSourceRepositories(m_repositories, m_discoverCredentialProvider, m_cancellationToken, logger, isRetry))
                    .Then<bool>(sourceRepositories =>
                    {
                        var packageBaseAddressMutable = new Dictionary<Uri, PackageSourceCredential>();
                        foreach (var sourceRepository in sourceRepositories)
                        {
                            var serviceIndexResource = sourceRepository.GetResource<ServiceIndexResourceV3>(m_cancellationToken);

                            if (serviceIndexResource == null)
                            {
                                return new NugetFailure(NugetFailure.FailureType.NoBaseAddressForRepository, $"Cannot find index service for ${sourceRepository.PackageSource.SourceUri}");
                            }

                            foreach (Uri packageBaseAddress in serviceIndexResource.GetServiceEntryUris(ServiceTypes.PackageBaseAddress))
                            {
                                packageBaseAddressMutable[packageBaseAddress] = sourceRepository.PackageSource.Credentials;
                            }
                        }

                        m_packageBaseAddress = packageBaseAddressMutable;

                        return true;
                    });

            }
            catch (Exception e) when (e is AggregateException || e is HttpRequestException)
            {
                return new NugetFailure(NugetFailure.FailureType.NoBaseAddressForRepository, e);
            }
            finally
            {
                var log = logger.ToString();
                if (!string.IsNullOrWhiteSpace(log))
                {
                    Logger.Log.NuGetInspectionInitializationInfo(m_loggingContext, log);
                }
            }
        }

        /// <summary>
        /// Whether <see cref="TryInitAsync"/> has been succesfully called
        /// </summary>
        /// <remarks>Thread safe</remarks>
        public async Task<bool> IsInitializedAsync() => m_initializationResult.IsValueCreated && (await m_initializationResult.Value).Succeeded;

        /// <summary>
        /// Retrieves the index sources of the specified repositories and initializes the base addresses
        /// </summary>
        /// <remarks>Thread safe. Subsequents initializations have no effect and return the same result as the first one did.</remarks>
        public Task<Possible<bool>> TryInitAsync() => m_initializationResult.Value;

        /// <summary>
        /// Tries to retrieve the layout and nuspec of the given package
        /// </summary>
        /// <remarks>Thread safe</remarks>
        public async Task<Possible<NugetInspectedPackage>> TryInspectAsync(INugetPackage identity)
        {
            Contract.Assert(await IsInitializedAsync(), "TryInitAsync() must be succesfully called first");

            if (m_packageBaseAddress.Count == 0)
            {
                return new NugetFailure(NugetFailure.FailureType.NoBaseAddressForRepository);
            }

            // Build the URI for the requested package and try to inspect it
            var packageIdLowerCase = identity.Id.ToLowerInvariant();
            var version = new NuGet.Versioning.NuGetVersion(identity.Version).ToNormalizedString();

            Possible<NugetInspectedPackage> maybeInspectedPackage = default;
            foreach (var baseAddress in m_packageBaseAddress)
            {
                // URIs for retrieving the nuspec and the nupkg
                var packageUri = $"{baseAddress.Key.AbsoluteUri}{packageIdLowerCase}/{version}/{packageIdLowerCase}.{version}.nupkg";
                var nuspecUri = $"{baseAddress.Key.AbsoluteUri}{packageIdLowerCase}/{version}/{packageIdLowerCase}.nuspec";

                maybeInspectedPackage = await TryInspectPackageAsync(identity, new Uri(packageUri), new Uri(nuspecUri), baseAddress.Value);
                if (maybeInspectedPackage.Succeeded)
                {
                    return maybeInspectedPackage;
                }
            }

            return maybeInspectedPackage.Failure;
        }

        private async Task<Possible<NugetInspectedPackage>> TryInspectPackageAsync(INugetPackage identity, Uri nupkgUri, Uri nuspecUri, PackageSourceCredential packageSourceCredential)
        {
            AuthenticationHeaderValue authenticationHeader = null;

            // If the download URI is pointing to a VSTS feed and we get a valid auth token, make it part of the request
            // We only want to send the token over HTTPS and to a VSTS domain to avoid security issues
            if (packageSourceCredential != null)
            {
                authenticationHeader = VSTSAuthenticationHelper.GetAuthenticationHeaderFromPAT(packageSourceCredential.PasswordText);
            }

            // We want to be able to read the zip file central directory, where the layout of the package is. This is at the end of a zip file, 
            // so we'll start requesting partial chunks of the content starting from the end and increasingly request more until we can understand
            // the zip central directory

            int retries = 3;

            while (true)
            {
                try
                {
                    using (var httpClient = new HttpClient())
                    {
                        httpClient.Timeout = TimeSpan.FromMinutes(1);

                        // Use authentication if defined
                        if (authenticationHeader != null)
                        {
                            httpClient.DefaultRequestHeaders.Accept.Clear();
                            httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                            httpClient.DefaultRequestHeaders.Authorization = authenticationHeader;
                        }

                        // We need the nuspec file for analyzing the package
                        var response = await httpClient.GetAsync(nuspecUri, m_cancellationToken);

                        if (!response.IsSuccessStatusCode)
                        {
                            return NugetFailure.CreateNugetInvocationFailure(identity, response.ReasonPhrase);
                        }

                        var nuspec = await response.Content.ReadAsStringAsync();

                        // Now inspect the content of the nupkg
                        return (await InspectContentAsync(identity, nupkgUri, authenticationHeader, httpClient)).Then(content => new NugetInspectedPackage(nuspec, content));
                    }
                }
                catch (Exception e) when (e is HttpRequestException || e is AggregateException || e is TaskCanceledException)
                {
                    retries--;

                    if (retries == 0)
                    {
                        return NugetFailure.CreateNugetInvocationFailure(identity, e);
                    }
                }
            }
        }

        private async Task<Possible<IReadOnlyList<RelativePath>>> InspectContentAsync(INugetPackage identity, Uri nupkgUri, AuthenticationHeaderValue authenticationHeader, HttpClient httpClient)
        {
            long? chunkStart = null;
            HttpResponseMessage response;
            bool forceFullDownload = false;
            var partialPackage = new MemoryStream();

            try
            {
                // How many chunks we downloaded so far for the given package
                int chunkCount = 0;
                
                while (true)
                {
                    // Only set a download range if we are not forcing a full download
                    if (!forceFullDownload)
                    {
                        // Set the range header to retrieve a particular range of the content
                        if (!chunkStart.HasValue)
                        {
                            // We don't know the total size yet, this is the first request. Start with MinimalChunkSizeInBytes.
                            httpClient.DefaultRequestHeaders.Add("Range", "bytes=-" + MinimalChunkSizeInBytes);
                        }
                        else
                        {
                            // This is not the first time we request a chunk, and that means the content we retrieved son far is not enough to read the zip central directory.
                            // So we already know where the chunk starts (and ends)
                            httpClient.DefaultRequestHeaders.Add("Range", $"bytes={chunkStart}-{chunkStart + (chunkCount * IncrementChunkSizeInBytes) - 1}");
                        }
                    }

                    // TODO: redirect handling may be needed here
                    response = await httpClient.GetAsync(nupkgUri, HttpCompletionOption.ResponseHeadersRead, m_cancellationToken);

                    // In the rare case where the initial chunk is bigger than the package, we might get this. Just force full download and retry
                    if (response.StatusCode == System.Net.HttpStatusCode.RequestedRangeNotSatisfiable)
                    {
                        httpClient.DefaultRequestHeaders.Remove("Range");
                        forceFullDownload = true;
                        continue;
                    }

                    if (!response.IsSuccessStatusCode)
                    {
                        break;
                    }

                    // Check whether the service decided to ignore the partial request, and instead downloaded the whole thing
                    if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        forceFullDownload = true;
                    }

                    long totalLength = 0;
                    if (!forceFullDownload)
                    {
                        totalLength = response.Content.Headers.ContentRange.Length.Value;

                        if (!chunkStart.HasValue)
                        {
                            // We just did the first request, chunk start points to MinimalChunkSizeInBytes from the end
                            chunkStart = totalLength - MinimalChunkSizeInBytes;
                        }
                    }
#if NET_COREAPP_60
                    using (var chunk = await response.Content.ReadAsStreamAsync(m_cancellationToken))
#else
                    using (var chunk = await response.Content.ReadAsStreamAsync())
#endif
                    {
                        // Unfortunately the .net framework does not support prepending a stream, so we do it manually
                        // TODO: If this becomes a perf/footprint issue we could write a stream wrapper that knows how to compose streams. But
                        // the expectation is that we shouldn't need to iterate over the same package for very long until we can read it
                        partialPackage = await PrependToStreamAsync(chunk, partialPackage);
                    }

                    // We don't want to get in the business of decoding a zip file. For that we use ZipArchive and check whether we get an exception
                    // when we read it.
                    // However, ZipArchive expects a stream with the proper length, since it does some validations about the location of the central
                    // directory baed on that. To avoid creating a stream of the real size (some packages are GB big), we use a ZeroPaddedStream that
                    // wraps the original stream but pretends to have the required size
                    IReadOnlyCollection<ZipArchiveEntry> entries;
                    try
                    {
                        var zf = new ZipArchive(forceFullDownload
                            ? partialPackage 
                            : new ZeroLeftPaddedStream(partialPackage, totalLength), ZipArchiveMode.Read);
                        entries = zf.Entries;
                    }
                    catch (InvalidDataException ex)
                    {
                        // We downloaded the package in full but we cannot recognize it as a zip
                        if (forceFullDownload)
                        {
                            return NugetFailure.CreateNugetInvocationFailure(identity, $"Cannot inspect package layout: {ex.ToStringDemystified()} ");
                        }

                        // This check is just a heuristics for the rare case when we already downloaded more than 10% of the package total
                        // size but we can't still read the zip central directory. At this point we can rather download the whole package
                        if (partialPackage.Length < totalLength * .1)
                        {
                            chunkCount++;
                            // We were not able to read the package central directory with what we downloaded so far. Request another chunk and
                            // try again
                            chunkStart -= chunkCount * IncrementChunkSizeInBytes;
                            httpClient.DefaultRequestHeaders.Remove("Range");

                            continue;
                        }

                        httpClient.DefaultRequestHeaders.Remove("Range");
                        forceFullDownload = true;
                        continue;
                    }

                    return entries
                        .Select(entry => entry.FullName.Contains('%') ? System.Net.WebUtility.UrlDecode(entry.FullName) : entry.FullName)
                        .Where(entry => PackageHelper.IsPackageFile(entry, PackageSaveMode.Files | PackageSaveMode.Nuspec))
                        .Select(entry => RelativePath.Create(m_stringTable, entry)).ToArray();
                }
            }
            finally
            {
                partialPackage.Dispose();
            }

            return NugetFailure.CreateNugetInvocationFailure(identity, $"Cannot inspect package layout: {response.RequestMessage} ");
        }

        private async Task<MemoryStream> PrependToStreamAsync(Stream prefix, MemoryStream stream)
        {
            // If the destination stream is empty, just copy the prefix over
            if (stream.Length == 0)
            {
                await prefix.CopyToAsync(stream, m_cancellationToken);
                return stream;
            }

            var tempPackage = new MemoryStream();
            await prefix.CopyToAsync(tempPackage, m_cancellationToken);
            
            stream.Position = 0;
            await stream.CopyToAsync(tempPackage, m_cancellationToken);

            stream.Dispose();
            
            return tempPackage;
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using Azure.Core;
using Azure.ResourceManager;
using BuildXL.Cache.BuildCacheResource.Helper;
using BuildXL.Cache.BuildCacheResource.Model;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Core;

namespace BuildXL.Cache.ContentStore.Interfaces.Auth;

/// <summary>
/// Provides a way to retrieve a <see cref="BuildCacheConfiguration"/> from a build cache resource in Azure
/// </summary>
public class BuildCacheConfigurationProvider
{
    private const int SasFreshnessHoursThreshold = 12;
    private const string BuildCacheResourceType = "buildcaches";
    private const string CloudTestResourceProvider = "Microsoft.CloudTest";
    private const string ExpectedIdFormat = "Expected format: '/subscriptions/<subscription id>/resourceGroups/<resource group>/providers/Microsoft.CloudTest/buildcaches/<build cache name>'";
    private const string FailurePrefix = "Failed to retrieve build cache configuration. ";

    /// <summary>
    /// We keep a static cache of build cache configurations retrieved, keyed by their resource identifier
    /// </summary>
    /// <remarks>
    /// This cache will only survive across builds when server mode is on.
    /// Make it thread safe since in theory could have a cache configuration where multiple resources are defined, and might be accessed concurrently
    /// </remarks>
    private readonly static ConcurrentDictionary<ResourceIdentifier, BuildCacheConfiguration> _configurationCache = new ConcurrentDictionary<ResourceIdentifier, BuildCacheConfiguration>();

    /// <summary>
    /// Given a build cache resource id with the form '/subscriptions/{subscription id}/resourceGroups/{resource group}/providers/Microsoft.CloudTest/buildcaches/{build cache name}',
    /// retrieves the <see cref="BuildCacheConfiguration"/> associated with it
    /// </summary>
    public async static Task<Possible<BuildCacheConfiguration>> TryGetBuildCacheConfigurationAsync(Tracing.Context context, TokenCredential tokenCredential, string buildCacheResourceId, CancellationToken cancellationToken)
    {
        var armClient = new ArmClient(tokenCredential);

        var identifier = new ResourceIdentifier(buildCacheResourceId);
        try
        {
            // Validate the resource points to a build cache
            if (identifier.ResourceType != $"{CloudTestResourceProvider}/{BuildCacheResourceType}")
            {
                return new Failure<string>($"{FailurePrefix}Unexpected resource type and provider specified in resource id '{buildCacheResourceId}'. {ExpectedIdFormat}");
            }
        }
        // Catch all for other parsing errors
        catch (ArgumentOutOfRangeException e)
        {
            return new Failure<string>($"{FailurePrefix}Malformed resource id '{buildCacheResourceId}'. {ExpectedIdFormat}. {e.Message}");
        }

        // Let's check if we have the configuration cached already for this resource. This avoids unnecessary network calls for the case of dev builds close to each other
        if (_configurationCache.TryGetValue(identifier, out var cachedConfiguration))
        {
            // Verify that all SAS tokens in the configuration are still fresh enough in order to return the cached configuration
            // Otherwise we will just ping the cache endpoint again to get a fresh configuration
            if (cachedConfiguration.Shards
                .SelectMany(shard => shard.Containers)
                .Select(container => container.Signature)
                .All(signature => IsSasFreshEnough(signature)))
            {
                context.Info($"Using cached build cache configuration for resource id '{buildCacheResourceId}'", nameof(BuildCacheConfigurationProvider));
                return cachedConfiguration;
            }

            context.Info($"Cached build cache configuration for resource id '{buildCacheResourceId}' found, but Sas tokens expiration will happen before {SasFreshnessHoursThreshold} hours. Retrieving a fresh configuration.", nameof(BuildCacheConfigurationProvider));
        }
        else
        {
            context.Info($"Build cache configuration for resource id '{buildCacheResourceId}' not found in the cache. Retrieving a fresh configuration.", nameof(BuildCacheConfigurationProvider));
        }

        // Let's retrieve the default API version we should use for accessing the resource
        var cloudTestProvider = await armClient.GetTenantResourceProviderAsync(CloudTestResourceProvider, expand: null, cancellationToken);
        if (!cloudTestProvider.HasValue)
        {
            return new Failure<string>($"{FailurePrefix}{CloudTestResourceProvider} resource provider not found.");
        }

        var buildCacheResource = cloudTestProvider.Value.ResourceTypes.FirstOrDefault(resourceType => resourceType.ResourceType == BuildCacheResourceType);
        if (buildCacheResource == default)
        {
            return new Failure<string>($"{FailurePrefix}{CloudTestResourceProvider} resource provider does not have a '{BuildCacheResourceType}' resource type.");
        }

        var token = await tokenCredential.GetTokenAsync(new TokenRequestContext(new string[] { "https://management.azure.com//.default" }), cancellationToken);

        // Unfortunately the ResourceManager APIs don't support applying arbitrary actions on generic resources (open issue https://github.com/Azure/azure-rest-api-specs/issues/24706)
        // so we have to use a plain HttpClient to access the resource
        using (var client = new HttpClient())
        {
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);
            client.BaseAddress = new Uri("https://management.azure.com/");

            using (var response = await client.GetAsync($"{buildCacheResourceId}/access?api-version={buildCacheResource.DefaultApiVersion}", cancellationToken))
            {
                if (!response.IsSuccessStatusCode)
                {
                    return new Failure<string>($"{FailurePrefix}Failed to retrieve build cache configuration. Status code: [{response.StatusCode}]{response.ReasonPhrase}");
                }

#if NET6_0_OR_GREATER
                using var responseStream = await response.Content.ReadAsStreamAsync(cancellationToken);
#else
                using var responseStream = await response.Content.ReadAsStreamAsync();
#endif
                try
                {
                    var buildCacheConfiguration = await BuildCacheResourceHelper.LoadBuildCacheConfigurationFromJSONAsync(responseStream);

                    // Unconditionally add (or update) the cache with the retrieved configuration. We always want the latest one in the cache, since that one has the larger TTL
                    _configurationCache.AddOrUpdate(identifier, (identifier) => buildCacheConfiguration, (identifier, existingConf) => buildCacheConfiguration);

                    return buildCacheConfiguration;
                }
                catch (JsonException e)
                {
                    return new Failure<string>($"{FailurePrefix}Unexpected format in build cache configuration. {e.Message}");
                }
            }
        }
    }

    private static DateTimeOffset? GetSasExpiration(string signature)
    {
        // The signature is usually a query string, e.g. "?sv=...&se=2025-12-31T23:59:59Z&..."
        var query = signature.TrimStart('?');
        var parameters = HttpUtility.ParseQueryString(query.ToString());

        var se = parameters["se"];
        if (se != null && DateTimeOffset.TryParse(se, out var expiration))
        {
            return expiration;
        }
        return null; // Expiration not found or invalid
    }

    private static bool IsSasFreshEnough(string signature)
    {
        var expiration = GetSasExpiration(signature);
        if (expiration.HasValue)
        {
            // Consider fresh enough if within SasFreshnessHoursThreshold of expiration to account for potential long builds
            return DateTimeOffset.UtcNow.AddHours(SasFreshnessHoursThreshold) < expiration.Value;
        }

        // No expiration found, assume not fresh enough
        return false;
    }
}

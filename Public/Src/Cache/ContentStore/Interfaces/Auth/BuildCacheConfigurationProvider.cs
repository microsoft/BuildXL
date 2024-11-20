// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
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
    private const string BuildCacheResourceType = "buildcaches";
    private const string CloudTestResourceProvider = "Microsoft.CloudTest";
    private const string ExpectedIdFormat = "Expected format: '/subscriptions/<subscription id>/resourceGroups/<resource group>/providers/Microsoft.CloudTest/buildcaches/<build cache name>'";
    private const string FailurePrefix = "Failed to retrieve build cache configuration. ";

    /// <summary>
    /// Given a build cache resource id with the form '/subscriptions/{subscription id}/resourceGroups/{resource group}/providers/Microsoft.CloudTest/buildcaches/{build cache name}',
    /// retrieves the <see cref="BuildCacheConfiguration"/> associated with it
    /// </summary>
    public async static Task<Possible<BuildCacheConfiguration>> TryGetBuildCacheConfigurationAsync(TokenCredential tokenCredential, string buildCacheResourceId, CancellationToken cancellationToken)
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

        // Check if the resource exists, so we can provide a more specific error message if it doesn't
        if (!(await armClient.GetGenericResources().ExistsAsync(identifier, cancellationToken)))
        {
            return new Failure<string>($"{FailurePrefix}Resource '{buildCacheResourceId}' does not exist.");
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
                    return await BuildCacheResourceHelper.LoadBuildCacheConfigurationFromJSONAsync(responseStream);
                }
                catch (JsonException e)
                {
                    return new Failure<string>($"{FailurePrefix}Unexpected format in build cache configuration. {e.Message}");
                }
            }
        }
    }
}

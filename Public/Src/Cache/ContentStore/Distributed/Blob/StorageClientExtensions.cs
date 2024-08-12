// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using OperationContext = BuildXL.Cache.ContentStore.Tracing.Internal.OperationContext;

namespace BuildXL.Cache.ContentStore.Distributed.Blob
{
    public static class StorageClientExtensions
    {
        public static Task<Result<bool>> CheckContainerExistsAsync(Tracer tracer, OperationContext context, BlobContainerClient client, TimeSpan storageInteractionTimeout)
        {
            return context.PerformOperationWithTimeoutAsync(
                tracer,
                async context =>
                {
                    var exceptions = new List<Exception>(capacity: 2);
                    try
                    {
                        if (await client.ExistsAsync(context.Token))
                        {
                            return new Result<bool>(true);
                        }
                        else
                        {
                            return new Result<bool>(false);
                        }
                    }
                    catch (Exception exception)
                    {
                        exceptions.Add(exception);
                    }

                    try
                    {
                        await foreach (var entry in client.GetBlobsAsync(BlobTraits.None, BlobStates.None, prefix: null, cancellationToken: context.Token))
                        {
                            // Because this is an IAsyncEnumerable, we just fetched the first page here.
                            break;
                        }

                        return new Result<bool>(true);
                    }
                    catch (RequestFailedException exception) when (exception.ErrorCode == "ContainerNotFound")
                    {
                        return new Result<bool>(false);
                    }
                    catch (Exception exception)
                    {
                        exceptions.Add(exception);
                    }

                    var error = new AggregateException(exceptions);

                    return Result.FromException<bool>(error, message: $"Failed to check if container {client.Name} exists in account {client.AccountName}. It's possible the credentials we have don't have permission to do much.");
                },
                traceOperationStarted: false,
                extraEndMessage: r =>
                {
                    var msg = $"Container=[{client.Name}]";

                    if (!r.Succeeded)
                    {
                        return msg;
                    }

                    return $"{msg} Exists=[{r.Value}]";
                },
                timeout: storageInteractionTimeout);
        }

        public static Task<Result<bool>> EnsureContainerExistsAsync(Tracer tracer, OperationContext context, BlobContainerClient client, TimeSpan storageInteractionTimeout)
        {
            return context.PerformOperationWithTimeoutAsync(
                tracer,
                async context =>
                {
                    // There's a set of fallback options here, because we want to be able to handle different
                    // levels of permissions being granted to the storage credentials we're using. We may have
                    // credentials that allow:
                    //
                    // 1. Actually creating containers and listing them.
                    // 2. Listing containers but not creating them.
                    // 3. Not listing containers, but still using them.
                    //
                    // The fallbacks here allow all of these options to go through. In some cases, the containers
                    // will actually not exist and we're expected to create them, so we do need to try to.

                    // We prefer to check existence before doing the Create because it's possible we might have permission
                    // to check but not to create.
                    if (await CheckContainerExistsAsync(tracer, context, client, storageInteractionTimeout).ThrowIfFailureAsync())
                    {
                        return new Result<bool>(false);
                    }

                    try
                    {
                        // There is a CreateIfNotExistsAsync API, but it doesn't work in practice against the Azure
                        // Storage emulator.
                        var response = await client.CreateAsync(
                            publicAccessType: PublicAccessType.None,
                            cancellationToken: context.Token);

                        return new Result<bool>(true);
                    }
                    catch (RequestFailedException exception) when (exception.ErrorCode == "ContainerAlreadyExists")
                    {
                        return new Result<bool>(false);
                    }
                    catch (Exception exception)
                    {
                        return Result.FromException<bool>(exception, $"Failed to create container `{client.Name}` in account `{client.AccountName}`");
                    }
                },
                traceOperationStarted: false,
                extraEndMessage: r =>
                {
                    var msg = $"Container=[{client.Name}]";

                    if (!r.Succeeded)
                    {
                        return msg;
                    }

                    return $"{msg} Created=[{r.Value}]";
                },
                timeout: storageInteractionTimeout);
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using BuildXL.Cache.BuildCacheResource.Model;
using BuildXL.Cache.BlobLifetimeManager.Library;
using BuildXL.Cache.ContentStore.Distributed;
using BuildXL.Cache.ContentStore.Distributed.Blob;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Time;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

internal static class BlobLifetimeManagerHelpers
{
    private static readonly Tracer Tracer = new(nameof(BlobLifetimeManager));

    public static async Task HandleConfigAndAccountDifferencesAsync(
        OperationContext context,
        RocksDbLifetimeDatabase db,
        IBlobCacheAccountSecretsProvider secretsProvider,
        IReadOnlyList<BlobCacheStorageAccountName> accounts,
        BlobQuotaKeeperConfig config,
        string metadataMatrix,
        string contentMatrix,
        IClock clock,
        BuildCacheConfiguration? buildCacheConfiguration)
    {
        var configuredNamespaces = config.Namespaces.Select(config => (config.Universe, config.Namespace)).ToHashSet();
        var enumeratedNamespaces = new HashSet<(string Universe, string Namespace)>();

        // Start by enumerating all accounts and their containers, attempting to parse their namespaces.
        foreach (var account in accounts)
        {
            var cred = await secretsProvider.RetrieveAccountCredentialsAsync(context, account);
            var client = cred.CreateBlobServiceClient();

            DateTime? deletionThreshold = config.UntrackedNamespaceDeletionThreshold is null
                ? null : clock.UtcNow.Add(-config.UntrackedNamespaceDeletionThreshold.Value);

            await foreach (var container in client.GetBlobContainersAsync())
            {
                context.Token.ThrowIfCancellationRequested();

                try
                {
                    Contract.Assert(container is not null);

                    // Skip containers meant for checkpointing and ephemeral caches.
                    if (IsAdminContainer(container.Name))
                    {
                        continue;
                    }

                    BlobCacheContainerName name;
                    // No build cache configuration means we use the legacy inference to get a container name
                    if (buildCacheConfiguration == null)
                    {
                        name = LegacyBlobCacheContainerName.Parse(container.Name);
                    }
                    else
                    {
                        var containerConfiguration = buildCacheConfiguration
                            .Shards.FirstOrDefault(shard => shard.StorageUri.AbsoluteUri == account.AccountName)?
                            .Containers.FirstOrDefault(cont => cont.Name == container.Name);

                        if (containerConfiguration == null)
                        {
                            throw new Exception($"Account {account.AccountName} and container name '{container.Name}' are not present in the build cache resource configuration file");
                        }

                        name = new FixedCacheBlobContainerName(
                            containerConfiguration.Name,
                            containerConfiguration.Type.ToContainerPurpose());
                    }

                    var namespaceId = new BlobNamespaceId(name.Universe, name.Namespace);

                    if (!configuredNamespaces.Contains((name.Universe, name.Namespace)))
                    {
                        Tracer.Warning(context, $"Container {container.Name} in account {client.AccountName} has " +
                            $"(Universe=[{name.Universe}], Namespace=[{name.Namespace}]), which is not listed in the configuration.");

                        await deleteUntrackedContainerIfNeeded(context, db, client, deletionThreshold, container, name, namespaceId, clock);

                        continue;
                    }
                    else if (name.Purpose == BlobCacheContainerPurpose.Metadata)
                    {
                        if (name.Matrix != metadataMatrix)
                        {
                            Tracer.Warning(context, $"Container {container.Name} in account {client.AccountName} has Matrix=[{name.Matrix}], which does" +
                                $"not match current matrix=[{metadataMatrix}]. Resharding is likely to have occurred.");

                            await deleteUntrackedContainerIfNeeded(context, db, client, deletionThreshold, container, name, namespaceId, clock);

                            continue;
                        }
                    }
                    else if (name.Purpose == BlobCacheContainerPurpose.Content)
                    {
                        if (name.Matrix != contentMatrix)
                        {
                            Tracer.Warning(context, $"Container {container.Name} in account {client.AccountName} has Matrix=[{name.Matrix}], which does" +
                                $"not match current matrix=[{contentMatrix}]. Resharding is likely to have occurred.");

                            await deleteUntrackedContainerIfNeeded(context, db, client, deletionThreshold, container, name, namespaceId, clock);

                            continue;
                        }
                    }

                    enumeratedNamespaces.Add((name.Universe, name.Namespace));
                }
                catch (FormatException)
                {
                    Tracer.Error(context, $"Failed to parse container name {container.Name} in account {client.AccountName}");
                }
                catch (Exception e)
                {
                    Tracer.Debug(context, e, "Error ocurred while handling untracked container.");
                }
            }
        }

        // Now find all configured namespaces with no matching containers.
        foreach (var (universe, @namespace) in configuredNamespaces.Except(enumeratedNamespaces))
        {
            Tracer.Warning(context, $"(Universe=[{universe}], Namespace=[{@namespace}]) was found in configuration but no matching containers were found.");
        }

        static async Task deleteUntrackedContainerIfNeeded(
            OperationContext context,
            RocksDbLifetimeDatabase db,
            BlobServiceClient client,
            DateTime? deletionThreshold,
            BlobContainerItem container,
            BlobCacheContainerName name,
            BlobNamespaceId namespaceId,
            IClock clock)
        {
            if (deletionThreshold is null)
            {
                return;
            }

            var lastAccessTime = db.GetNamespaceLastAccessTime(namespaceId, name.Matrix);
            if (lastAccessTime is null)
            {
                // When this happens, we have no information about the container. This means it currently exists, but
                // there were no events processed during GC. Such cases can only happen when the container was created
                // after the threshold at which we no longer process events for GC.
                //
                // In these cases, we'll set the access time to now, which means that if the next GC run doesn't see
                // any events for this container, it will consider it for deletion.
                Tracer.Debug(context, $"Marking untracked container. Account=[{client.AccountName}], Container=[{container.Name}]");
                db.SetNamespaceLastAccessTime(namespaceId, name.Matrix, clock.UtcNow);
            }
            else if (lastAccessTime < deletionThreshold)
            {
                Tracer.Debug(context, $"Deleting old untracked container. Account=[{client.AccountName}], Container=[{container.Name}], LastAccessTime=[{lastAccessTime}]");

                await client.DeleteBlobContainerAsync(container.Name, cancellationToken: context.Token);

                // Clean up entry in DB.
                db.SetNamespaceLastAccessTime(namespaceId, name.Matrix, lastAccessTimeUtc: null);
            }
        }
    }

    /// <summary>
    /// These containers are not the typical blob containers, but are used for administrative purposes and should not be deleted.
    /// </summary>
    private static bool IsAdminContainer(string containerName) =>
        containerName.Equals("ephemeral", StringComparison.Ordinal) || containerName.StartsWith("checkpoints-", StringComparison.Ordinal);
}

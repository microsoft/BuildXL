// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Threading;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Secrets;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

#nullable enable

namespace BuildXL.Cache.Host.Service.Internal
{
    internal static class MemoryMappedFileHelper
    {
        private static readonly long FileSize = 70 * 1024; // using a file size less then 80K to avoid allocating objects in LOH.

        public static MemoryMappedFile CreateMemoryMappedFileWithContent(string fileName, string content)
        {
            var file = MemoryMappedFile.CreateNew(fileName, FileSize, MemoryMappedFileAccess.ReadWrite);

            try
            {
                using var stream = file.CreateViewStream();
                using var writer = new StreamWriter(stream);
                writer.WriteLine(content);
                return file;
            }
            catch
            {
                file.Dispose();
                throw;
            }
        }

        public static string? ReadContent(string fileName)
        {
            using var file = MemoryMappedFile.OpenExisting(fileName, MemoryMappedFileRights.Read);

            using var stream = file.CreateViewStream(0, 0, MemoryMappedFileAccess.Read);
            using var reader = new StreamReader(stream);
            return reader.ReadLine();
        }

        public static void UpdateContent(string fileName, string content)
        {
            using var file = MemoryMappedFile.OpenExisting(fileName, MemoryMappedFileRights.Write);

            using var stream = file.CreateViewStream();
            using var writer = new StreamWriter(stream);
            writer.WriteLine(content);
        }
    }

    /// <summary>
    /// Allows communicating secrets with their updates across process boundaries.
    /// </summary>
    internal sealed class InterProcessSecretsCommunicator : IDisposable
    {
        private readonly List<Action> _disposeActions;
        private static readonly Tracer Tracer = new Tracer(nameof(InterProcessSecretsCommunicator));

        internal const string SecretsFileName = "CaSaaS_Secrets";

        private InterProcessSecretsCommunicator(List<Action> disposeActions)
        {
            _disposeActions = disposeActions;
        }

        public void Dispose()
        {
            foreach (var action in _disposeActions)
            {
                action();
            }
        }

        /// <summary>
        /// Exposes the <paramref name="secrets"/> via a memory mapped file with <paramref name="fileName"/>.
        /// </summary>
        /// <remarks>
        /// The secrets are available for another process until the resulting value is disposed.
        /// </remarks>
        public static IDisposable Expose(OperationContext context, RetrievedSecrets secrets, string? fileName)
        {
            string memoryMappedFileName = fileName ?? SecretsFileName;

            var memoryMappedFile = context.PerformOperation(
                Tracer,
                () =>
                {
                    var serializedSecrets = RetrievedSecretsSerializer.Serialize(secrets);
                    return Result.Success(MemoryMappedFileHelper.CreateMemoryMappedFileWithContent(memoryMappedFileName, serializedSecrets));
                },
                extraStartMessage: $"Exposing secrets to '{memoryMappedFileName}'",
                messageFactory: _ => $"Exposed secrets to '{memoryMappedFileName}'"
                ).ThrowIfFailure();

            var disposeActions = new List<Action>();

            disposeActions.Add(
                () =>
                {
                    Tracer.Debug(context, $"Closing memory mapped file '{memoryMappedFileName}'");
                    memoryMappedFile.Dispose();
                });

            disposeActions.AddRange(trackSecretsLifetime());

            return new InterProcessSecretsCommunicator(disposeActions);

            List<Action> trackSecretsLifetime()
            {
                var actions = new List<Action>();
                foreach (var (_, secret) in secrets.Secrets)
                {
                    if (secret is UpdatingSasToken updating)
                    {
                        updating.TokenUpdated += tokenUpdated;

                        actions.Add(() => updating.TokenUpdated -= tokenUpdated);
                    }
                }

                if (actions.Count != 0)
                {
                    // It means that we have at least one 'updatable' token.
                    actions.Insert(
                        0,
                        () =>
                        {
                            Tracer.Debug(context, "Unsubscribing from secret updates.");
                        });
                }

                return actions;
            }

            void tokenUpdated(object? sender, SasToken e)
            {
                var newSerializedSecrets = RetrievedSecretsSerializer.Serialize(secrets);
                context.PerformOperation(
                        Tracer,
                        () =>
                        {
                            MemoryMappedFileHelper.UpdateContent(memoryMappedFileName, newSerializedSecrets);
                            return BoolResult.Success;
                        },
                        extraStartMessage: $"Updating secrets in '{memoryMappedFileName}'",
                        messageFactory: _ => $"Updated secrets in '{memoryMappedFileName}'",
                        caller: "tokenUpdated")
                    .IgnoreFailure(); // We traced the results
            }
        }

        /// <summary>
        /// Reads <see cref="MemoryMappedBasedRetrievedSecrets"/> from a memory mapped file and updates them automatically if the secrets get changed.
        /// </summary>
        public static MemoryMappedBasedRetrievedSecrets ReadExposedSecrets(OperationContext context, string? fileName, int pollingIntervalInSeconds = 10)
        {
            string memoryMappedFileName = fileName ?? SecretsFileName;

            var secrets = context.PerformOperation(
                Tracer,
                () =>
                {
                    var content = MemoryMappedFileHelper.ReadContent(memoryMappedFileName);
                    return RetrievedSecretsSerializer.Deserialize(content);
                },
                extraStartMessage: $"Obtaining secrets from '{memoryMappedFileName}'",
                messageFactory: _ => $"Obtained secrets from '{memoryMappedFileName}'").ThrowIfFailure();

            TimeSpan pollingInterval = TimeSpan.FromSeconds(pollingIntervalInSeconds);
            Timer? timer = null;

            var result = new MemoryMappedBasedRetrievedSecrets(secrets.Secrets, () => timer?.Dispose(), memoryMappedFileName);

            timer = new Timer(
                _ =>
                {
                    result.RefreshSecrets(context);

                    try
                    {
                        timer?.Change(pollingInterval, Timeout.InfiniteTimeSpan);
                    } catch(ObjectDisposedException) { }
                },
                state: null,
                dueTime: pollingInterval,
                period: Timeout.InfiniteTimeSpan);

            return result;
        }

        public record MemoryMappedBasedRetrievedSecrets(IReadOnlyDictionary<string, Secret> Secrets, Action DisposeAction, string MemoryMappedFileName)
            : RetrievedSecrets(Secrets), IDisposable
        {
            public void Dispose()
            {
                DisposeAction();
            }

            public void RefreshSecrets(OperationContext context)
            {
                var newSecrets = context.PerformOperation(
                    Tracer,
                    () =>
                    {
                        var content = MemoryMappedFileHelper.ReadContent(MemoryMappedFileName);
                        return RetrievedSecretsSerializer.Deserialize(content);
                    },
                    extraStartMessage: $"Refreshing secrets from '{MemoryMappedFileName}'",
                    messageFactory: _ => $"Refreshing secrets from '{MemoryMappedFileName}'");

                // Now we need to update the secrets originally read from file.
                if (newSecrets.Succeeded)
                {
                    context.PerformOperation(
                        Tracer,
                        () =>
                        {
                            UpdateSecrets(this, newSecrets.Value);
                            return BoolResult.Success;
                        }).IgnoreFailure(); // the error was already traced.
                }
            }

            private static void UpdateSecrets(RetrievedSecrets original, RetrievedSecrets @new)
            {
                foreach (var (name, secret) in @new.Secrets)
                {
                    if (secret is UpdatingSasToken updatingToken)
                    {
                        // The secret types can't change.
                        var originalSecret = (UpdatingSasToken)original.Secrets[name];
                        originalSecret.UpdateToken(updatingToken.Token);
                    }
                }
            }
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.IO;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Distributed.NuCache;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.Redis
{
    /// <summary>
    /// <see cref="CentralStorage"/> implementation that uses file system for storing the checkpoints and other data.
    /// </summary>
    internal sealed class LocalDiskCentralStorage : CentralStorage
    {
        private readonly AbsolutePath _workingDirectory;

        protected override Tracer Tracer { get; } = new Tracer(nameof(LocalDiskCentralStorage));

        /// <inheritdoc />
        public LocalDiskCentralStorage(LocalDiskCentralStoreConfiguration configuration)
        {
            Contract.Requires(configuration != null);

            _workingDirectory = configuration.WorkingDirectory;
            if (!string.IsNullOrEmpty(configuration.ContainerName))
            {
                _workingDirectory /= configuration.ContainerName;
            }
        }

        protected override Task<Result<string>> UploadFileCoreAsync(OperationContext context, AbsolutePath file, string blobName, bool garbageCollect)
        {
            var destination = _workingDirectory / blobName;
            Directory.CreateDirectory(Path.GetDirectoryName(destination.ToString()));

            // Copy checkpoint to working directory
            File.Copy(file.Path, destination.ToString(), overwrite: true);

            return Task.FromResult(new Result<string>(destination.ToString()));
        }

        /// <inheritdoc />
        protected override Task<BoolResult> TryGetFileCoreAsync(OperationContext context, string storageId, AbsolutePath targetFilePath, bool isImmutable)
        {
            if (File.Exists(storageId))
            {
                Directory.CreateDirectory(targetFilePath.Parent.Path);

                File.Copy(storageId, targetFilePath.Path);
                return Task.FromResult(BoolResult.Success);
            }

            return Task.FromResult(new BoolResult($"File with blob name '{storageId}' does not exist and hence can't be placed into {targetFilePath}``"));
        }

        protected override Task<BoolResult> TouchBlobCoreAsync(OperationContext context, AbsolutePath file, string blobName, bool isUploader, bool isImmutable)
        {
            var destination = _workingDirectory / blobName;
            if (File.Exists(destination.ToString()))
            {
                return BoolResult.SuccessTask;
            }
            else
            {
                return Task.FromResult(new BoolResult($"File `{file}` with blob name `{blobName}` does not exist and hence can't be touched"));
            }
        }
    }
}

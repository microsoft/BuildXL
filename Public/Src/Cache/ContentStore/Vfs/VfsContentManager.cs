// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Cache.ContentStore.Vfs.Provider;
using BuildXL.Native.IO;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Cache.ContentStore.Vfs
{
    using FullPath = Interfaces.FileSystem.AbsolutePath;
    using VirtualPath = System.String;

    /// <summary>
    /// A store which virtualizes calls to an underlying content store (i.e. content will
    /// be lazily materialized using the projected file system filter driver)
    /// </summary>
    internal class VfsContentManager : StartupShutdownBase, IVfsFilePlacer, IDisposable
    {
        // TODO: Track stats about file materialization (i.e. how much content was hydrated)
        // On BuildXL side, track how much requested total requested file content size would be.

        public CounterCollection<VfsCounters> Counters { get; } = new CounterCollection<VfsCounters>();

        /// <summary>
        /// Unique integral id for files under vfs cas root
        /// </summary>
        private int _nextVfsCasTargetFileUniqueId = 0;

        internal VfsTree Tree { get; }

        internal VfsProvider Provider { get; }
        protected override Tracer Tracer { get; } = new Tracer(nameof(VfsContentManager));

        private readonly VfsCasConfiguration _configuration;
        private readonly ILogger _logger;

        private readonly IReadOnlyContentSession _placer;
        private readonly DisposableDirectory _tempDirectory;
        private readonly PassThroughFileSystem _fileSystem;

        /// <nodoc />
        public VfsContentManager(ILogger logger, VfsCasConfiguration configuration, IReadOnlyContentSession placer)
        {
            Tree = new VfsTree(configuration);

            _logger = logger;
            _configuration = configuration;
            _placer = placer;
            _fileSystem = new PassThroughFileSystem();
            _tempDirectory = new DisposableDirectory(_fileSystem, configuration.DataRootPath / "temp");

            Provider = new VfsProvider(logger, configuration, this);
        }

        /// <inheritdoc />
        protected override async Task<BoolResult> StartupCoreAsync(OperationContext context)
        {
            if (!Provider.StartVirtualization())
            {
                return new BoolResult("Unable to start virtualizing");
            }

            return await base.StartupCoreAsync(context);
        }

        /// <inheritdoc />
        protected override Task<BoolResult> ShutdownCoreAsync(OperationContext context)
        {
            _tempDirectory.Dispose();
            Provider.StopVirtualization();

            return BoolResult.SuccessTask;
        }

        /// <summary>
        /// Converts the VFS root relative path to a full path
        /// </summary>
        internal FullPath ToFullPath(VirtualPath relativePath)
        {
            return _configuration.VfsRootPath / relativePath;
        }

        /// <summary>
        /// Converts the full path to a VFS root relative path
        /// </summary>
        internal VirtualPath ToVirtualPath(VfsFilePlacementData data, FullPath path)
        {
            // Use the same index (0) for ReadOnly/Hardlink files
            var shouldHardlink = FileSystemContentStoreInternal.ShouldAttemptHardLink(path, data.AccessMode, data.RealizationMode);

            // All hardlinks of a hash share the same file under the vfs root so just
            // use index 0 to represent that file. Otherwise, create a unique index so
            // that copies get unique files
            var index = shouldHardlink
                ? 0
                : Interlocked.Increment(ref _nextVfsCasTargetFileUniqueId);

            VirtualPath casRelativePath = VfsUtilities.CreateCasRelativePath(data, index);

            var virtualPath = _configuration.VfsCasRelativeRoot / casRelativePath;
            return virtualPath.Path;
        }

        public Task<StreamWithLength> OpenStreamAsync(ContentHash hash, uint bufferSize, CancellationToken token)
        {
            return WithOperationContext(new Context(_logger), token,
                context =>
                {
                    return context.PerformOperationAsync(
                        Tracer,
                        async () =>
                        {

                            var openStreamResult = await _placer.OpenStreamAsync(
                                context,
                                hash,
                                context.Token).ThrowIfFailure();

                            var streamWithLength = openStreamResult.StreamWithLength.Value;
                            if (streamWithLength.Length >= 0)
                            {
                                Counters[VfsCounters.PlaceHydratedFileBytes].Add(streamWithLength.Length);
                            }

                            return Result.Success(streamWithLength);
                        },
                        extraEndMessage: r => $"Hash={hash}").ThrowIfFailureAsync();
                });
        }

        /// <inheritdoc />
        public Task<PlaceFileResult> PlaceFileAsync(Context context, FullPath path, VfsFilePlacementData placementData, CancellationToken token)
        {
            return WithOperationContext(context, token, async operationContext =>
            {
                var result = await operationContext.PerformOperationAsync(
                    Tracer,
                    async () =>
                    {
                        var virtualPath = ToVirtualPath(placementData, path);

                        var node = Tree.AddFileNode(virtualPath, placementData, path.Path);

                        var vfsPath = _configuration.VfsRootPath / virtualPath;

                        // TODO: Faster path for getting size of file. Also would be good to batch somehow.
                        // Maybe have FileContentManager batch pin files
                        var pinResult = await _placer.PinAsync(context, node.Hash, operationContext.Token);
                        if (pinResult.ContentSize > 0)
                        {
                            node.Size = pinResult.ContentSize;
                        }
                        else
                        {
                            return new PlaceFileResult($"Pin file size = {pinResult.ContentSize}");
                        }

                        if (placementData.AccessMode == FileAccessMode.ReadOnly)
                        {
                            // NOTE: This should cause the placeholder to get created under vfs root
                            _fileSystem.DenyFileWrites(vfsPath);
                        }

                        var result = _fileSystem.CreateHardLink(vfsPath, path, replaceExisting: true);
                        if (result != CreateHardLinkResult.Success)
                        {
                            if (result == CreateHardLinkResult.FailedDestinationDirectoryDoesNotExist)
                            {
                                _fileSystem.CreateDirectory(path.Parent);
                                result = _fileSystem.CreateHardLink(vfsPath, path, replaceExisting: true);
                            }

                            if (result != CreateHardLinkResult.Success)
                            {
                                return new PlaceFileResult($"Failed to create hardlink: {result}");
                            }
                        }

                        return PlaceFileResult.CreateSuccess(GetPlaceResultCode(placementData.RealizationMode, placementData.AccessMode), fileSize: pinResult.ContentSize /* Unknown */, source: PlaceFileResult.Source.LocalCache);
                    },
                    caller: "PlaceVirtualFile",
                    extraEndMessage: r => $"Hash={placementData.Hash}");

                if (!result.Succeeded)
                {
                    return await _placer.PlaceFileAsync(context, path, placementData, token);
                }

                return result;
            });
        }

        private PlaceFileResult.ResultCode GetPlaceResultCode(FileRealizationMode realizationMode, FileAccessMode accessMode)
        {
            if (realizationMode == FileRealizationMode.Copy
                || realizationMode == FileRealizationMode.CopyNoVerify
                || accessMode == FileAccessMode.Write)
            {
                return PlaceFileResult.ResultCode.PlacedWithCopy;
            }

            return PlaceFileResult.ResultCode.PlacedWithHardLink;
        }
    }
}

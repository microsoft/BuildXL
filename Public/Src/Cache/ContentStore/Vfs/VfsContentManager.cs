// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Logging;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Logging;
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
        private int _nextVfsCasTargetFileUniqueId;

        internal VfsTree Tree { get; }

        internal VfsProvider Provider { get; }

        protected override Tracer Tracer { get; } = new Tracer(nameof(VfsContentManager));

        private readonly VfsCasConfiguration _configuration;
        private readonly ILogger _logger;

        private readonly IVfsFilePlacer _placer;
        private readonly DisposableDirectory _tempDirectory;
        private readonly PassThroughFileSystem _fileSystem;

        /// <nodoc />
        public VfsContentManager(ILogger logger, VfsCasConfiguration configuration, IVfsFilePlacer placer)
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
        /// Places a hydrated file at the given VFS root relative path
        /// </summary>
        /// <param name="relativePath">the vfs root relative path</param>
        /// <param name="data">the content and placement data for the file</param>
        /// <param name="token">the cancellation token</param>
        /// <returns>a task which completes when the operation is complete or throws an exception if error is encountered during operation</returns>
        internal Task PlaceHydratedFileAsync(VirtualPath relativePath, VfsFileNode node, CancellationToken token)
        {
            var fullPath = ToFullPath(relativePath);

            return PlaceHydratedFileAsync(fullPath, node, token);
        }

        /// <summary>
        /// See <see cref="PlaceHydratedFileAsync(VirtualPath, VfsFilePlacementData, CancellationToken)"/>
        /// </summary>
        public Task<BoolResult> PlaceHydratedFileAsync(FullPath fullPath, VfsFileNode node, CancellationToken token)
        {
            var data = node.Data;
            var realPath = node.RealPath.ToString(Tree.PathTable);

            return WithOperationContext(new Context(_logger), token, context => 
                context.PerformOperationAsync(
                    Tracer,
                    async () =>
                    {
                        // Replace the file at the real path
                        var result = await _placer.PlaceFileAsync(
                            context,
                            new FullPath(realPath),
                            data,
                            token).ThrowIfFailure();

                        // Place the file in the VFS path as well
                        await _placer.PlaceFileAsync(
                            context,
                            fullPath,
                            data,
                            token).ThrowIfFailure();

                        if (result.FileSize >= 0)
                        {
                            Counters[VfsCounters.PlaceHydratedFileBytes].Add(result.FileSize);
                        }
                        else
                        {
                            Counters[VfsCounters.PlaceHydratedFileUnknownSizeCount].Increment();
                        }

                        return BoolResult.Success;
                    },
                    extraStartMessage: $"VfsPath={fullPath}, RealPath={realPath}, Hash={data.Hash}",
                    counter: Counters[VfsCounters.PlaceHydratedFile]));
        }

        /// <summary>
        /// Converts the full path to a VFS root relative path
        /// </summary>
        internal VirtualPath ToVirtualPath(FullPath path)
        {
            foreach (var mount in _configuration.VirtualizationMounts)
            {
                if (path.Path.TryGetRelativePath(mount.Value.Path, out var mountRelativePath))
                {
                    RelativePath relativePath = _configuration.VfsMountRelativeRoot / mount.Key / mountRelativePath;
                    return relativePath.Path;
                }
            }

            if (path.Path.TryGetRelativePath(_configuration.VfsRootPath.Path, out var rootRelativePath))
            {
                return rootRelativePath;
            }

            return null;
        }

        /// <summary>
        /// Attempts to create a symlink which points to a virtualized file materialized with the given data
        /// </summary>
        public Result<VirtualPath> TryCreateSymlink(OperationContext context, AbsolutePath sourcePath, VfsFilePlacementData data, bool replaceExisting)
        {
            return context.PerformOperation(
                Tracer,
                () =>
                {
                    _fileSystem.CreateDirectory(sourcePath.Parent);

                    if (replaceExisting)
                    {
                        FileUtilities.DeleteFile(sourcePath.Path);
                    }

                    var index = Interlocked.Increment(ref _nextVfsCasTargetFileUniqueId);
                    VirtualPath casRelativePath = VfsUtilities.CreateCasRelativePath(data, index);

                    var virtualPath = _configuration.VfsCasRelativeRoot / casRelativePath;

                    var fullTargetPath = _configuration.VfsCasRootPath / casRelativePath;

                    var now = DateTime.UtcNow;

                    var tempFilePath = _tempDirectory.CreateRandomFileName();

                    var result = FileUtilities.TryCreateSymbolicLink(symLinkFileName: tempFilePath.Path, targetFileName: fullTargetPath.Path, isTargetFile: true);
                    if (result.Succeeded)
                    {
                        var attributes = FileUtilities.GetFileAttributes(tempFilePath.Path);
                        attributes |= FileAttributes.Offline;
                        FileUtilities.SetFileAttributes(tempFilePath.Path, attributes);

                        _fileSystem.MoveFile(tempFilePath, sourcePath, replaceExisting: replaceExisting);
                        return Result.Success(virtualPath.Path);
                    }
                    else
                    {
                        return Result.FromErrorMessage<VirtualPath>(result.Failure.DescribeIncludingInnerFailures());
                    }
                },
                extraStartMessage: $"SourcePath={sourcePath}, Hash={data.Hash}",
                messageFactory: r => $"SourcePath={sourcePath}, Hash={data.Hash}, TargetPath={r.GetValueOrDefault()}",
                counter: Counters[VfsCounters.TryCreateSymlink]);
        }

        /// <inheritdoc />
        public Task<PlaceFileResult> PlaceFileAsync(Context context, FullPath path, VfsFilePlacementData placementData, CancellationToken token)
        {
            return WithOperationContext(context, token, async operationContext =>
            {
                var virtualPath = _configuration.UseSymlinks
                    ? TryCreateSymlink(operationContext, path, placementData, replaceExisting: true).ThrowIfFailure()
                    : ToVirtualPath(path);

                if (virtualPath == null)
                {
                    return await _placer.PlaceFileAsync(context, path, placementData, token);
                }

                Tree.AddFileNode(virtualPath, placementData, path.Path);
                return new PlaceFileResult(GetPlaceResultCode(placementData.RealizationMode, placementData.AccessMode), fileSize: -1 /* Unknown */);
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

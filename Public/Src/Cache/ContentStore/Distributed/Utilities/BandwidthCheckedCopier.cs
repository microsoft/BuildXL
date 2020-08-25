// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;

namespace BuildXL.Cache.ContentStore.Distributed.Utilities
{
    /// <summary>
    /// File copier that times out copies based on their bandwidths.
    /// </summary>
    public class BandwidthCheckedCopier : IAbsolutePathRemoteFileCopier
    {
        private readonly IAbsolutePathRemoteFileCopier _inner;
        private readonly BandwidthChecker _checker;

        /// <nodoc />
        public BandwidthCheckedCopier(IAbsolutePathRemoteFileCopier inner, BandwidthChecker.Configuration config)
        {
            _inner = inner;
            _checker = new BandwidthChecker(config);
        }

        /// <inheritdoc />
        public Task<FileExistenceResult> CheckFileExistsAsync(AbsolutePath path, TimeSpan timeout, CancellationToken cancellationToken) => _inner.CheckFileExistsAsync(path, timeout, cancellationToken);

        /// <inheritdoc />
        public Task<CopyFileResult> CopyToAsync(OperationContext context, AbsolutePath sourcePath, Stream destinationStream, long expectedContentSize, CopyToOptions options)
        {
            // The bandwidth checker needs to have an options instance, because it is used for tracking the copy progress as well.
            options ??= new CopyToOptions();
            return _checker.CheckBandwidthAtIntervalAsync(
                context,
                // NOTE: We need to pass through the token from bandwidth checker to ensure copy cancellation for insufficient bandwidth gets triggered.
                token => _inner.CopyToAsync(context.WithCancellationToken(token), sourcePath, destinationStream, expectedContentSize, options),
                options);
        }
    }
}

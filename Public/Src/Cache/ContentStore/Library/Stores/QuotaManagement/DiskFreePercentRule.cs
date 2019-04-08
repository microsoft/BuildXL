// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Quota rule limiting size to leave a specified amount of disk free space.
    /// </summary>
    public class DiskFreePercentRule : QuotaRule
    {
        /// <summary>
        /// Name of rule for logging
        /// </summary>
        public const string DiskFreePercentRuleName = "DiskFreePercent";

        // We don't remove linked content as doing so does not make any progress against
        // the disk free percent (ignoring MFT).
        private const bool OnlyUnlinkedValue = true;

        private readonly IAbsFileSystem _fileSystem;
        private readonly AbsolutePath _rootPath;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DiskFreePercentRule"/> class.
        /// </summary>
        public DiskFreePercentRule(
            DiskFreePercentQuota quota,
            EvictAsync evictAsync,
            IAbsFileSystem fileSystem,
            AbsolutePath rootPath,
            DistributedEvictionSettings distributedEvictionSettings = null)
            : base(evictAsync, OnlyUnlinkedValue, distributedEvictionSettings)
        {
            Contract.Requires(quota != null);
            Contract.Requires(evictAsync != null);
            Contract.Requires(fileSystem != null);
            Contract.Requires(rootPath != null);

            _quota = quota;
            _fileSystem = fileSystem;
            _rootPath = rootPath;
        }

        /// <summary>
        ///     Check if reserve count is inside limit.
        /// </summary>
        public override BoolResult IsInsideLimit(long limit, long reserveCount)
        {
            var vi = _fileSystem.GetVolumeInfo(_rootPath);
            var freeSpace = Math.Max(vi.FreeSpace - reserveCount, 0);

            // Without casting to double, freeSpacePercent will always be 0 because freeSpace <= vi.Size.
            var freeSpacePercent = 100.0 * ((double)freeSpace / vi.Size);

            if (freeSpacePercent < limit)
            {
                return
                    new BoolResult(
                        $"Exceeds {limit}% when adding {reserveCount} bytes. Configuration: [Rule={DiskFreePercentRuleName} {_quota}]");
            }

            return BoolResult.Success;
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Synchronization;
using BuildXL.Cache.ContentStore.Tracing;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    ///     Elastic quota rule.
    /// </summary>
    public sealed class ElasticSizeRule : QuotaRule
    {
        /// <summary>
        ///     Name of rule for logging
        /// </summary>
        private const string ElasticSizeRuleName = "ElasticSize";

        /// <summary>
        ///     Default value for history window size.
        /// </summary>
        public const int DefaultHistoryWindowSize = 10;

        /// <summary>
        ///     Default value for calibration coefficient.
        /// </summary>
        public const double DefaultCalibrationCoefficient = 1.25;

        /// <summary>
        ///     Ratio between current size and hard limit.
        /// </summary>
        public const double CurrentSizeHardLimitRatio = 0.99;

        /// <summary>
        ///     Binary file name for saving elastic size rule.
        /// </summary>
        public const string BinaryFileName = "ElasticQuota.bin";

        /// <summary>
        ///     Whether or not to remove only unlinked content.
        /// </summary>
        /// <remarks>
        ///     Removing linked content does make progress toward the cache size.
        /// </remarks>
        private const bool OnlyUnlinkedValue = false;

        private static readonly MaxSizeQuota SmallQuota = new MaxSizeQuota(10);

        private readonly Func<long> _getCurrentSizeFunc;
        private readonly Func<int, PinSizeHistory.ReadHistoryResult> _getPinnedSizeHistoryFunc;
        private readonly IAbsFileSystem _fileSystem;
        private readonly AbsolutePath _rootPath;
        private readonly int _historyWindowSize;
        private readonly AsyncReaderWriterLock _locker = new AsyncReaderWriterLock();
        private readonly double _calibrationCoefficient;
        private readonly MaxSizeQuota _initialQuota;

        private long _historyTimestampInTick;
        private bool _isQuotaEnabled;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ElasticSizeRule" /> class.
        /// </summary>
        public ElasticSizeRule(
            int? historyWindowSize,
            MaxSizeQuota initialElasticSize,
            EvictAsync evictAsync,
            Func<long> getCurrentSizeFunc,
            Func<int, PinSizeHistory.ReadHistoryResult> getPinnedSizeHistoryFunc,
            IAbsFileSystem fileSystem,
            AbsolutePath rootPath,
            double? calibrationCoefficient = default(double?),
            DistributedEvictionSettings distributedEvictionSettings = null)
            : base(evictAsync, OnlyUnlinkedValue, distributedEvictionSettings)
        {
            Contract.Requires(!historyWindowSize.HasValue || historyWindowSize.Value >= 0);
            Contract.Requires(evictAsync != null);
            Contract.Requires(getCurrentSizeFunc != null);
            Contract.Requires(getPinnedSizeHistoryFunc != null);
            Contract.Requires(fileSystem != null);
            Contract.Requires(rootPath != null);
            Contract.Requires(!calibrationCoefficient.HasValue || calibrationCoefficient.Value > 1.0);

            _historyWindowSize = historyWindowSize ?? DefaultHistoryWindowSize;
            _calibrationCoefficient = calibrationCoefficient ?? DefaultCalibrationCoefficient;
            _getPinnedSizeHistoryFunc = getPinnedSizeHistoryFunc;
            _getCurrentSizeFunc = getCurrentSizeFunc;
            _fileSystem = fileSystem;
            _rootPath = rootPath;
            _initialQuota = initialElasticSize ?? SmallQuota;

            var loadQuotaResult = LoadOrCreateNewAsync(_fileSystem, _initialQuota, _rootPath).GetAwaiter().GetResult();
            _quota = loadQuotaResult.Quota;
            _historyTimestampInTick = loadQuotaResult.HistoryTimestampInTick;

            // TODO: CalibrateAsync method fails in tests all the time. Bug #1331905
            CalibrateAsync().GetAwaiter().GetResult().IgnoreFailure();
            Contract.Assert(IsInsideHardLimit(0, checkIfQuotaEnabled: false).Succeeded);
        }

        private static async Task<BoolResult> SaveQuotaAsync(IAbsFileSystem fileSystem, AbsolutePath rootPath, MaxSizeQuota quota, long historyTimestampInTick)
        {
            var filePath = rootPath / BinaryFileName;
            try
            {
                fileSystem.DeleteFile(filePath);

                using (
                    var stream =
                        await fileSystem.OpenSafeAsync(filePath, FileAccess.Write, FileMode.CreateNew, FileShare.Delete))
                {
                    using (var writer = new BinaryWriter(stream))
                    {
                        writer.Write(quota.Hard);
                        writer.Write(quota.Soft);
                        writer.Write(historyTimestampInTick);
                    }
                }

                return BoolResult.Success;
            }
            catch (Exception e)
            {
                // When failed, clean up so that it is not used in the next load.
                fileSystem.DeleteFile(filePath);

                return new BoolResult(e);
            }
        }

        private async Task<LoadQuotaResult> LoadOrCreateNewAsync(IAbsFileSystem fileSystem, MaxSizeQuota initialElasticQuota, AbsolutePath rootPath)
        {
            var filePath = rootPath / BinaryFileName;

            try
            {
                if (!fileSystem.FileExists(filePath))
                {
                    return await CreateNewAsync(fileSystem, initialElasticQuota, rootPath);
                }

                using (var stream = await fileSystem.OpenReadOnlySafeAsync(filePath, FileShare.Delete))
                {
                    using (var reader = new BinaryReader(stream))
                    {
                        return new LoadQuotaResult(new MaxSizeQuota(reader.ReadInt64(), reader.ReadInt64()), reader.ReadInt64());
                    }
                }
            }
            catch (IOException)
            {
                return await CreateNewAsync(fileSystem, initialElasticQuota, rootPath);
            }
        }

        private async Task<LoadQuotaResult> CreateNewAsync(IAbsFileSystem fileSystem, MaxSizeQuota initialElasticQuota, AbsolutePath rootPath)
        {
            var currentSize = _getCurrentSizeFunc();

            if (initialElasticQuota.Hard < currentSize)
            {
                // If current size is larger than the initial quota, then throw away initial quota, and use current size.
                // The quota is set by taking into account the current size and hard limit ratio used during shrinkage.
                // If the quota is set to the current size, then the very first reservation may potentially be blocking.
                initialElasticQuota = new MaxSizeQuota((long)(currentSize / CurrentSizeHardLimitRatio));
            }

            // Set the timestamp tick to -1 so that it can consume pre-existing history window upon calibration during construction.
            var loadQuotaResult = new LoadQuotaResult(initialElasticQuota, -1);
            await SaveQuotaAsync(fileSystem, rootPath, loadQuotaResult.Quota, loadQuotaResult.HistoryTimestampInTick).ThrowIfFailure();

            return loadQuotaResult;
        }

        /// <inheritdoc />
        public override BoolResult IsInsideHardLimit(long reserveSize = 0)
        {
            return _locker.WithReadLock(() => IsInsideHardLimit(reserveSize, true));
        }

        /// <inheritdoc />
        public override BoolResult IsInsideSoftLimit(long reserveSize = 0)
        {
            return _locker.WithReadLock(() => IsInsideSoftLimit(reserveSize, true));
        }

        /// <inheritdoc />
        public override BoolResult IsInsideTargetLimit(long reserveSize = 0)
        {
            return _locker.WithReadLock(() => IsInsideTargetLimit(reserveSize, true));
        }

        /// <inheritdoc />
        public override bool CanBeCalibrated => true;

        /// <inheritdoc />
        public override Task<CalibrateResult> CalibrateAsync()
        {
            // Calibration should be done in atomic way.
            return _locker.WithWriteLockAsync(async () =>
            {
                /*
                 *                o (Case A)
                 *
                 * ---------------------------------------------  Hard limit
                 *
                 *                o (Case B)
                 *
                 * ---------------------------------------------  Soft limit
                 *
                 *                o (Case C)
                 */

                bool isCalibrated = false;
                string reason = string.Empty;

                if (!IsInsideHardLimit(0, false).Succeeded)
                {
                    // Case A.
                    _quota = new MaxSizeQuota((long)(_getCurrentSizeFunc() * _calibrationCoefficient));
                    isCalibrated = true;
                }
                else
                {
                    // Case B & C.
                    // Try bring hard limit closer to the current size based on history of pinned sizes.

                    // Constraint 1: newHardLimit >= currentSize.
                    long newHardLimit = (long)(_getCurrentSizeFunc() / CurrentSizeHardLimitRatio);

                    if (newHardLimit < _initialQuota.Hard)
                    {
                        newHardLimit = _initialQuota.Hard;
                    }

                    // Constraint 2: window.Max * CalibrateUpCoefficient <= newHardLimit.
                    var history = _getPinnedSizeHistoryFunc(_historyWindowSize);

                    if (newHardLimit >= _quota.Hard)
                    {
                        // Candidate new hard limit is larger than or equal to the current hard limit.
                        reason = $"New hard limit, {newHardLimit}, is larger than or equal to the current hard limit, {_quota.Hard}";
                    }
                    else if (history.Window.Count <= 0)
                    {
                        // No entries in the history window.
                        // Note that we allow to consume history which is smaller than the specified history window size.
                        // This allows smaller history to cause calibration downward.
                        reason = "History window is empty";
                    }
                    else if (history.TimestampInTick <= _historyTimestampInTick)
                    {
                        // History timestamp is older than the current timestamp.
                        reason = $"History timestamp, {history.TimestampInTick}, is older than the current timestamp, {_historyTimestampInTick}";
                    }
                    else if (history.Window.Max() * _calibrationCoefficient >= newHardLimit)
                    {
                        // Max value in the calibration window, times the calibration coefficient, is larger than the new hard limit.
                        reason =
                            $"Max history window, {history.Window.Max()}, with calibration coefficient {_calibrationCoefficient}, exceeds candidate hard limit {newHardLimit}";
                    }
                    else
                    {
                        _quota = new MaxSizeQuota(newHardLimit);
                        _historyTimestampInTick = history.TimestampInTick;
                        isCalibrated = true;
                    }
                }

                _isQuotaEnabled = true;

                if (!isCalibrated)
                {
                    return new CalibrateResult(reason);
                }

                var saveResult = await SaveQuotaAsync();

                return !saveResult.Succeeded ? new CalibrateResult(saveResult.ErrorMessage) : new CalibrateResult(_quota.Hard);
            });
        }

        /// <inheritdoc />
        public override bool IsEnabled
        {
            get { return _locker.WithReadLock(() => _isQuotaEnabled); }

            set
            {
                _locker.WithWriteLock(() =>
                {
                    return _isQuotaEnabled = value;
                });
            }
        }

        /// <summary>
        ///     Gets a copy of current quota.
        /// </summary>
        public MaxSizeQuota CurrentQuota
        {
            get { return _locker.WithReadLock(() => new MaxSizeQuota(_quota.Hard)); }
        }

        /// <summary>
        ///     Check if reserve count is inside limit.
        /// </summary>
        public override BoolResult IsInsideLimit(long limit, long reserveCount)
        {
            var currentSize = _getCurrentSizeFunc();
            var finalSize = currentSize + reserveCount;

            return finalSize > limit
                ? new BoolResult(
                    $"Exceeds {limit} bytes when adding {reserveCount} bytes. Configuration: [Rule={ElasticSizeRuleName} {_quota}]")
                : BoolResult.Success;
        }

        private Task<BoolResult> SaveQuotaAsync()
        {
            return SaveQuotaAsync(_fileSystem, _rootPath, _quota as MaxSizeQuota, _historyTimestampInTick);
        }

        private BoolResult IsInsideHardLimit(long reserveSize, bool checkIfQuotaEnabled)
        {
            if (checkIfQuotaEnabled && !_isQuotaEnabled)
            {
                return BoolResult.Success;
            }

            return IsInsideLimit(_quota.Hard, reserveSize);
        }

        private BoolResult IsInsideSoftLimit(long reserveSize, bool checkIfQuotaEnabled)
        {
            if (checkIfQuotaEnabled && !_isQuotaEnabled)
            {
                return BoolResult.Success;
            }

            return IsInsideLimit(_quota.Soft, reserveSize);
        }

        private BoolResult IsInsideTargetLimit(long reserveSize, bool checkIfQuotaEnabled)
        {
            if (checkIfQuotaEnabled && !_isQuotaEnabled)
            {
                return BoolResult.Success;
            }

            return IsInsideLimit(_quota.Target, reserveSize);
        }

        /// <summary>
        ///     Result of loading elastic quota.
        /// </summary>
        public class LoadQuotaResult
        {
            /// <summary>
            ///     Gets quota.
            /// </summary>
            public MaxSizeQuota Quota { get; }

            /// <summary>
            ///     Gets history timestamp in ticks.
            /// </summary>
            public long HistoryTimestampInTick { get; }

            /// <summary>
            ///     Initializes a new instance of the <see cref="LoadQuotaResult"/> class.
            /// </summary>
            public LoadQuotaResult(MaxSizeQuota quota, long historyTimestampInTick)
            {
                Quota = quota;
                HistoryTimestampInTick = historyTimestampInTick;
            }
        }
    }
}

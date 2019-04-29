// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Utilities.Tasks;

namespace BuildXL.Cache.ContentStore.Stores
{
    /// <summary>
    /// Base class that represents a request that is processed asynchronously by <see cref="QuotaKeeperV2"/>.
    /// </summary>
    internal abstract class QuotaRequest
    {
        private readonly TaskSourceSlim<BoolResult> _taskSource = TaskSourceSlim.Create<BoolResult>();

        /// <nodoc />
        public static QuotaRequest Calibrate() => new CalibrateQuotaRequest();

        /// <nodoc />
        public static ReserveSpaceRequest Reserve(long size) => new ReserveSpaceRequest(size);

        /// <nodoc />
        public static ReserveSpaceRequest Purge() => Reserve(0);

        /// <nodoc />
        public static SynchronizationRequest Synchronize() => new SynchronizationRequest();

        /// <nodoc />
        public Task<BoolResult> CompletionAsync() => _taskSource.Task;

        /// <nodoc />
        public virtual void Success() => _taskSource.SetResult(BoolResult.Success);

        /// <nodoc />
        public void Failure(string error) => _taskSource.SetResult(new BoolResult(error));
    }

    /// <summary>
    /// Request to <see cref="QuotaKeeperV2"/> for reserving the disk space.
    /// </summary>
    internal sealed class ReserveSpaceRequest : QuotaRequest
    {
        /// <summary>
        /// Returns true if the request was completed because <see cref="QuotaKeeperV2"/> evicted enough content.
        /// </summary>
        public bool IsReservedFromEviction { get; set; }

        /// <summary>
        /// The requested size.
        /// </summary>
        public long ReserveSize { get; }

        /// <inheritdoc />
        public ReserveSpaceRequest(long reserveSize) => ReserveSize = reserveSize;

        /// <inheritdoc />
        public override string ToString() => $"Reservation request for {ReserveSize} bytes";
    }

    /// <summary>
    /// Request to <see cref="QuotaKeeperV2"/> for processing all pending requests.
    /// </summary>
    internal sealed class SynchronizationRequest : QuotaRequest
    {
        /// <inheritdoc />
        public override string ToString() => $"Synchronization";
    }

    /// <summary>
    /// Request to <see cref="QuotaKeeperV2"/> to calibrate all the rules.
    /// </summary>
    internal sealed class CalibrateQuotaRequest : QuotaRequest
    {
        /// <inheritdoc />
        public override string ToString() => "Calibration request";
    }
}

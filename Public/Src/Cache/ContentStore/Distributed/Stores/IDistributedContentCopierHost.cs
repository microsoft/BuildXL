// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Interfaces.Tracing;
using BuildXL.Cache.ContentStore.Tracing.Internal;

namespace BuildXL.Cache.ContentStore.Distributed.Stores
{
    /// <summary>
    /// Host information and callbacks for <see cref="DistributedContentCopier"/>
    /// </summary>
    public interface IDistributedContentCopierHost
    {
        /// <summary>
        /// The staging folder where copies are placed before putting into CAS
        /// </summary>
        AbsolutePath WorkingFolder { get; }

        /// <summary>
        /// Reports about new reputation for a given location.
        /// </summary>
        void ReportReputation(Context context, MachineLocation location, MachineReputation reputation);
    }

    /// <summary>
    /// Additional callbacks for <see cref="DistributedContentCopier"/>
    /// </summary>
    public interface IDistributedContentCopierHost2 : IDistributedContentCopierHost
    {
        /// <summary>
        /// Notifies the host of a distributed copy result and allows host to annotate the copy message with additional data. 
        /// </summary>
        string ReportCopyResult(OperationContext context, ContentLocation info, CopyFileResult result);
    }

    /// <nodoc />
    public static class DistributedContentCopierHostExtensions
    {
        /// <inheritdoc cref="IDistributedContentCopierHost2.ReportCopyResult" />
        public static string ReportCopyResult(
            this IDistributedContentCopierHost host,
            OperationContext context,
            ContentLocation info,
            CopyFileResult result)
        {
            if (host is IDistributedContentCopierHost2 host2)
            {
                return host2.ReportCopyResult(context, info, result);
            }
            else
            {
                return string.Empty;
            }
        }
    }
}

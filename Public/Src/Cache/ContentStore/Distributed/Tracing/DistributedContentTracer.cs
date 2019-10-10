// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Cache.ContentStore.Stats;
using BuildXL.Cache.ContentStore.Tracing;

// disable 'Missing XML comment for publicly visible type' warnings.
#pragma warning disable 1591
#pragma warning disable SA1600 // Elements must be documented

namespace BuildXL.Cache.ContentStore.Distributed.Tracing
{
    public class DistributedContentTracer : ContentSessionTracer
    {
        public const string Component = "DistributedContent";

        public const string RemoteCopyFileCallName = "RemoteCopyFileCall";
        private const string RemoteBytesCopiedCountName = "RemoteBytesCount";
        private const string RemoteCopyFileSuccessCountName = "RemoteCopyFileSuccessCount";
        private const string RemoteCopyFileFailCountName = "RemoteCopyFileFailedCount";

        private readonly Counter _remoteBytesCounter;
        private readonly Counter _remoteFilesCopiedCounter;
        private readonly Counter _remoteFilesFailedCopyCounter;

        public DistributedContentTracer()
            : base(Component)
        {
            Counters.Add(_remoteBytesCounter = new Counter(RemoteBytesCopiedCountName));
            Counters.Add(_remoteFilesCopiedCounter = new Counter(RemoteCopyFileSuccessCountName));
            Counters.Add(_remoteFilesFailedCopyCounter = new Counter(RemoteCopyFileFailCountName));

            CallCounters.Add(new CallCounter(RemoteCopyFileCallName));
        }

        public void UpdateBytesCopiedRemotely(long size)
        {
            _remoteBytesCounter.Add(size);
        }

        public void UpdateFilesCopiedRemotely()
        {
            _remoteFilesCopiedCounter.Increment();
        }

        public void UpdateFilesFailedToCopyFromRemote()
        {
            _remoteFilesFailedCopyCounter.Increment();
        }
    }
}

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.UtilitiesCore;

#nullable enable

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Times when a checkpoint should be restored for a given machine, so that machines across a stamp will download
    /// checkpoints in a way that probabilistically guarantees a certain number of replicas will be there for files
    /// included in the checkpoint.
    /// </summary>
    /// <remarks>
    /// Class is static to isolate the behavior and help testing it.
    /// </remarks>
    public static class RestoreCheckpointPacemaker
    {
        /// <nodoc />
        public static (uint Buckets, uint Bucket, TimeSpan RestoreTime) ShouldRestoreCheckpoint(byte[] primaryMachineLocation, uint numberOfBuckets, int activeMachines, DateTime checkpointCreationTime, TimeSpan createCheckpointInterval)
        {
            Contract.Assert(activeMachines >= 1);

            // Number of buckets needed to reach the number of machines over time
            var buckets = numberOfBuckets;
            if (buckets <= 0)
            {
                // 10 is the default because it's enough to accommodate 1024 machines, which is a rough upper bound on
                // the number of machines we keep on stamps.
                buckets = activeMachines > 1 ? (uint)Math.Ceiling(Math.Log(activeMachines, 2)) : 10;
            }

            var uniformRandomSample = SampleIdentifier(primaryMachineLocation, checkpointCreationTime);
            var bucket = SampleBucket(buckets, uniformRandomSample);
            var restoreTimeBucket = ComputeRestoreTime(createCheckpointInterval, buckets, bucket);

            return (Buckets: buckets, Bucket: bucket, RestoreTime: restoreTimeBucket);
        }

        /// <nodoc />
        internal static TimeSpan ComputeRestoreTime(TimeSpan createCheckpointInterval, uint buckets, uint bucket)
        {
            Contract.Requires(buckets > 0);
            Contract.Requires(0 <= bucket && bucket < buckets);

            // Delta after checkpoint is created by when we are supposed to start restoring the checkpoint ourselves
            return TimeSpan.FromMilliseconds((createCheckpointInterval.TotalMilliseconds / buckets) * bucket);
        }

        /// <nodoc />
        internal static double SampleIdentifier(byte[] primaryMachineLocation, DateTime checkpointCreationTime)
        {
            // Create a deterministic identifier for the node. This identifier won't change throughout the heartbeats
            // as long as the checkpoint doesn't change. Hence, a node may end up in a different bucket when the
            // checkpoint changes. Tricky part is this needs to be uniformly distributed between 0 and 1
            var hash = MurmurHash3.Create(
                key: primaryMachineLocation,
                seed: (uint)(checkpointCreationTime.Ticks % ((long)uint.MaxValue)));
            return Convert.ToDouble(hash.High) / Convert.ToDouble(ulong.MaxValue);
        }

        /// <nodoc />
        internal static uint SampleBucket(uint buckets, double identifier)
        {
            Contract.Assert(buckets > 0);
            Contract.Assert(0 <= identifier && identifier < 1);
            // We need to generate a distribution such that the probability that a given machine is in a given bucket
            // increases exponentially as the bucket number increases. Usual way would be to count leading zeros of the
            // hash, but this limits the number of buckets to the size of the binary representation, which can be a
            // problem if the checkpoint creation frequency is too low.
            // Given the number of buckets N, this generates a discrete distribution X with support over {0, ..., N-1}
            // such that P(X = i) = 2^{-(N - i)} and P(X = N - 1) = 2^{-1} + 2{-N}. This guarantees that we spread
            // exponentially because P(X = i + 1) >= 2 P(X = i). The weird corner case of X = N - 1 is because it's the
            // easy way to turn the idea into a proper distribution. Since we are doing inverse transform sampling, the
            // identifier needs to be uniformly distributed between [0, 1).
            uint bucket = (uint)Math.Min(buckets - 1, buckets + Math.Floor(Math.Log(identifier + Math.Pow(2, -buckets), 2)));
            Contract.Assert(0 <= bucket && bucket <= buckets - 1);
            return bucket;
        }
    }
}

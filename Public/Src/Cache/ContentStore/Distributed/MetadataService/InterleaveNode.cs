// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BuildXL.Cache.ContentStore.Distributed.MetadataService;
using BuildXL.Cache.ContentStore.Hashing;
using BuildXL.Cache.ContentStore.Interfaces.Extensions;
using BuildXL.Cache.ContentStore.Interfaces.Results;
using BuildXL.Cache.ContentStore.Stores;
using BuildXL.Cache.ContentStore.Tracing.Internal;
using BuildXL.Cache.ContentStore.Utils;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Serialization;
using RocksDbSharp;

namespace BuildXL.Cache.ContentStore.Distributed.NuCache
{
    /// <summary>
    /// Represents a node in a directed acyclic dataflow graph used by <see cref="BlobContentLocationRegistry"/> whereby partitions of content entries are
    /// progressively combined and then split where each transform shrinks the partition but
    /// simultaneously captures data from more machines. This allows dramatically decreasing the number of operations
    /// required to update the <see cref="BlobContentLocationRegistry"/>. For instance, in a stamp with 
    /// 1000 machines and 256 partitions. This would normally require at least 256 * 1000 = 256000 operations
    /// whereas with this strategy only (4 + 1) * (1000 / 4) * (log4(256) + 1) = ~6500 operations are required with a Factor of 4.
    /// </summary>
    /// <param name="Stage">the level of the node in the graph</param>
    /// <param name="MachineStart">the start machine of the range of machines covered by this node</param>
    /// <param name="MachineEnd">the end machine of the range of machines covered by this node</param>
    /// <param name="PartitionSpan">the range of partitions covered by this node</param>
    /// <param name="Factor">the factor by which the machin range is increased and the partition range is decreased on each stage</param>
    public record struct InterleaveNode(int Stage, int MachineStart, int MachineEnd, PartitionSpan PartitionSpan, int Factor)
    {
        public bool IsTerminal => PartitionSpan.Length == 1;

        public PartitionId PartitionId => PartitionSpan.PartitionId;

        /// <summary>
        /// Gets the child to which data from this node will be divided amongst. Child nodes encompass <see cref="Factor"/> times more machines
        /// build partition extent is divided by <see cref="Factor"/>.
        /// </summary>
        public IEnumerable<InterleaveNode> GetChildren()
        {
            if (IsTerminal)
            {
                yield break;
            }

            var childMachineCount = (MachineEnd - MachineStart + 1) * Factor;
            var machineStart = (MachineStart / childMachineCount) * childMachineCount;
            var machineEnd = machineStart + childMachineCount - 1;

            foreach (var partitionSpan in GetChildPartitions())
            {
                yield return new InterleaveNode(
                    Stage: Stage + 1,
                    MachineStart: machineStart,
                    MachineEnd: machineEnd,
                    PartitionSpan: partitionSpan,
                    Factor: Factor);
            }
        }

        /// <summary>
        /// Gets the child partitions. This takes the <see cref="PartitionSpan"/> for the current node
        /// and divides it into <see cref="Factor"/> equal partition spans.
        /// </summary>
        private IEnumerable<PartitionSpan> GetChildPartitions()
        {
            var childSpanLength = Math.Max(PartitionSpan.Length / Factor, 1);
            for (int i = 0; i < PartitionSpan.Length; i += childSpanLength)
            {
                yield return PartitionSpan.Partitions.GetSubView(i, childSpanLength);
            }
        }

        public string GetBlockName()
        {
            return $"M{Pad(MachineStart)}+{Pad(MachineEnd)}/P{Pad(PartitionId.StartValue, 3)}+{Pad(PartitionId.EndValue, 3)}/".PadLeft(24, '0');
        }

        public string GetBlobName()
        {
            return $"F{Factor}x-{Stage}/M[{Pad(MachineStart)}-{Pad(MachineEnd)}].P[{Pad(PartitionId.StartValue, 3)}-{Pad(PartitionId.EndValue, 3)}].bin";
        }

        private string Pad(int value, int totalWidth = 5)
        {
            return value.ToString().PadLeft(totalWidth, '0');
        }

        /// <summary>
        /// Gets whether the machine is contained in the machine range represented by this node
        /// </summary>
        public bool Contains(MachineId machineId)
        {
            return MachineStart <= machineId.Index && MachineEnd >= machineId.Index;
        }

        /// <summary>
        /// Computes the set of <see cref="InterleaveNode"/>s in stages where each subsequent stage is composed of the
        /// child nodes of the prior stage.
        /// </summary>
        public static InterleaveNode[][] ComputeNodes(int factor, int maxMachineId, ReadOnlyArray<PartitionId> partitions)
        {
            factor = PartitionId.CoerceToPowerOfTwoInRange(factor, minValue: 2);
            var stages = new List<List<InterleaveNode>>();

            // Get the root nodes
            var stage = new List<InterleaveNode>();
            stages.Add(stage);
            for (int startMachineId = 0; startMachineId <= maxMachineId; startMachineId += factor)
            {
                var node = new InterleaveNode(0, startMachineId, startMachineId + (factor - 1), new PartitionSpan(partitions), factor);
                stage.Add(node);
            }

            // Produce subsequent stages by aggregating all the unique child of the prior stage
            var set = new HashSet<(int machineStart, PartitionId partition)>();
            while (true)
            {
                var nextStage = new List<InterleaveNode>();
                foreach (var child in stage.SelectMany(s => s.GetChildren()))
                {
                    if (set.Add((child.MachineStart, child.PartitionId)))
                    {
                        nextStage.Add(child);
                    }
                }

                if (nextStage.Count == 0)
                {
                    break;
                }

                stages.Add(nextStage);
                stage = nextStage;
            }

            return stages.Select(s => s.ToArray()).ToArray();
        }
    }

    /// <summary>
    /// Helper type representing a contiguous set of partition ids
    /// </summary>
    public record struct PartitionSpan(ArrayView<PartitionId> Partitions)
    {
        public PartitionId PartitionId => new PartitionId(Partitions[0].StartValue, Partitions[Partitions.Length - 1].EndValue);

        public int Length => Partitions.Length;

        public override string ToString()
        {
            return $"(#{Partitions.Length}){PartitionId}";
        }

        public static implicit operator PartitionSpan(ArrayView<PartitionId> partitions)
        {
            return new(partitions);
        }
    }
}

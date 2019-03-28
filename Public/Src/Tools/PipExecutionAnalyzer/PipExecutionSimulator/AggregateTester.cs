// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Scheduler.Graph;

namespace PipExecutionSimulator
{
    public class AggregateTester
    {
        public DirectedGraph Graph;
        public ConcurrentNodeDictionary<long> AggregateEstimate = new ConcurrentNodeDictionary<long>(false);
        public ConcurrentNodeDictionary<long> AggregateActual = new ConcurrentNodeDictionary<long>(false);

        public void Test()
        {
        }

        public void ComputeEstimate()
        {
            ConcurrentNodeDictionary<long> maxSamples = new ConcurrentNodeDictionary<long>(false);
            var samples = GenerateSamples();
            foreach (var node in Graph.Nodes)
            {
                AggregateEstimate[node] += ComputeEstimateHelper(node, maxSamples, samples);
            }
        }

        private ConcurrentNodeDictionary<long> GenerateSamples()
        {
            ConcurrentNodeDictionary<long> samples = new ConcurrentNodeDictionary<long>(false);



            return samples;
        }

        private long ComputeEstimateHelper(NodeId node, ConcurrentNodeDictionary<long> maxSamples, ConcurrentNodeDictionary<long> samples)
        {
            long maxSample = maxSamples[node];
            if (maxSample == 0)
            {
                maxSample = samples[node];

                foreach (var outgoingEdge in Graph.GetOutgoingEdges(node))
                {
                    var childMaxSample = ComputeEstimateHelper(outgoingEdge.OtherNode, maxSamples, samples);
                    childMaxSample.Max(ref maxSample);
                }

                maxSamples[node] = maxSample;
            }

            return maxSample;
        }

        public void ComputeActual()
        {
            HashSet<NodeId> transitiveNodes = new HashSet<NodeId>();
            foreach (var node in Graph.Nodes)
            {
                ComputeActualHelper(node, transitiveNodes);
                AggregateActual[node] = transitiveNodes.Count;
                transitiveNodes.Clear();
            }
        }

        private void ComputeActualHelper(NodeId node, HashSet<NodeId> nodes)
        {
            if (nodes.Add(node))
            {
                foreach (var outgoingEdge in Graph.GetOutgoingEdges(node))
                {
                    ComputeActualHelper(outgoingEdge.OtherNode, nodes);
                }
            }
        }
    }
}

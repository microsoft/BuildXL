// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Utilities.Collections;

namespace BuildXL.Native.Streams
{
    internal sealed class OverlappedPool : IDisposable
    {
        private OverlappedPoolNode[] m_nodes = CollectionUtilities.EmptyArray<OverlappedPoolNode>();

        public unsafe TaggedOverlapped* ReserveOverlappedWithTarget(IIOCompletionTarget target)
        {
            int nodeIndex = 0;
            while (true)
            {
                OverlappedPoolNode[] nodes = Volatile.Read(ref m_nodes);

                Contract.Assume(nodes != null, "Attempting to reserve an overlapped on a disposed overlapped pool.");

                if (nodeIndex == nodes.Length)
                {
                    lock (this)
                    {
                        Contract.Assume(m_nodes != null, "Attempting to reserve an overlapped on a disposed overlapped pool.");

                        if (nodeIndex == m_nodes.Length)
                        {
                            var newNodes = new OverlappedPoolNode[nodeIndex + 1];
                            Array.Copy(m_nodes, newNodes, nodeIndex);
                            newNodes[nodeIndex] = new OverlappedPoolNode(nodeIndex);
                            m_nodes = newNodes;
                        }

                        Contract.Assume(nodeIndex < m_nodes.Length);
                        nodes = m_nodes;
                    }
                }

                for (; nodeIndex < nodes.Length; nodeIndex++)
                {
                    TaggedOverlapped* reserved = nodes[nodeIndex].TryReserveOverlappedWithTarget(target);
                    if (reserved != null)
                    {
                        return reserved;
                    }
                }

                // All nodes exhausted; maybe grow the array and continue from the present index.
            }
        }

        public unsafe IIOCompletionTarget ReleaseOverlappedAndGetTarget(TaggedOverlapped* overlapped)
        {
            int nodeIndex = overlapped->PoolNodeId;
            OverlappedPoolNode[] nodes = Volatile.Read(ref m_nodes);
            Contract.Assume(nodes != null, "Attempting to release an overlapped on a disposed overlapped pool.");
            Contract.Assume(nodeIndex >= 0 && nodeIndex < nodes.Length, "Invalid node ID; note only add node ID (no shrinking)");
            return nodes[nodeIndex].ReleaseOverlappedAndGetTarget(overlapped);
        }

        public void Dispose()
        {
            lock (this)
            {
                Contract.Assume(m_nodes != null, "Double-dispose of an overlapped pool");
                foreach (OverlappedPoolNode node in m_nodes)
                {
                    node.Dispose();
                }

                m_nodes = null;
            }
        }
    }
}

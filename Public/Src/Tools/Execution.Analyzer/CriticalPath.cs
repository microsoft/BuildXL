// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using BuildXL.Scheduler.Graph;

namespace BuildXL.Execution.Analyzer
{
    public class CriticalPath
    {
        public CriticalPath(Func<NodeId, TimeSpan> getElapsed, Func<NodeId, IEnumerable<NodeId>> getOutgoingNodes)
        {
            m_getElapsed = getElapsed;
            m_getOutgoingNodes = getOutgoingNodes;
        }

        private readonly Func<NodeId, TimeSpan> m_getElapsed;

        private readonly Func<NodeId, IEnumerable<NodeId>> m_getOutgoingNodes;

        private readonly IDictionary<NodeId, CriticalPathStats> m_nodeToCriticalPath = new Dictionary<NodeId, CriticalPathStats>();

        public CriticalPathStats ComputeCriticalPath(NodeId node)
        {
            CriticalPathStats criticalPath;
            bool hasCriticalPath = m_nodeToCriticalPath.TryGetValue(node, out criticalPath);
            if (hasCriticalPath)
            {
                return criticalPath;
            }

            TimeSpan maxOutputgoingCriticalPath = TimeSpan.Zero;
            NodeId maxOutputgoingNode = node;
            foreach (var outgoingNode in m_getOutgoingNodes(node))
            {
                var dependencyCriticalPath = ComputeCriticalPath(outgoingNode);
                if (dependencyCriticalPath.CriticalPathTime > maxOutputgoingCriticalPath)
                {
                    maxOutputgoingCriticalPath = dependencyCriticalPath.CriticalPathTime;
                    maxOutputgoingNode = outgoingNode;
                }
            }
            criticalPath = new CriticalPathStats
            {
                CriticalPathTime = m_getElapsed(node) + maxOutputgoingCriticalPath,
                NextNodeInCriticalPath = maxOutputgoingNode
            };
            m_nodeToCriticalPath[node] = criticalPath;
            return criticalPath;
        }
    }

    public struct CriticalPathStats
    {
        public TimeSpan CriticalPathTime;
        public NodeId NextNodeInCriticalPath;
    }
}
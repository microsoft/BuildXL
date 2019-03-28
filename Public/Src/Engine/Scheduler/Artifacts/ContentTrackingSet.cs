// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using BuildXL.Pips;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Scheduler.Artifacts
{
    /// <summary>
    /// Tracks availability of statically declared content on a machine
    /// </summary>
    public sealed class ContentTrackingSet
    {
        private readonly ConcurrentBitArray m_contentSet;
        private readonly PipGraph m_graph;
        private readonly ConcurrentBigSet<PipId> m_serviceContentSet;

        /// <summary>
        /// Creates a content tracking set for the pip graph
        /// </summary>
        public ContentTrackingSet(PipGraph graph)
        {
            m_graph = graph;
            m_contentSet = new ConcurrentBitArray(graph.ContentCount);
            m_serviceContentSet = new ConcurrentBigSet<PipId>();
        }

        /// <summary>
        /// Gets whether the content is available
        /// </summary>
        public bool Contains(in FileOrDirectoryArtifact artifact)
        {
            int? contentIndex = m_graph.GetContentIndex(artifact);
            return contentIndex != null && m_contentSet[contentIndex.Value];
        }

        /// <summary>
        /// Gets whether the service input content is available
        /// </summary>
        public bool Contains(PipId servicePipId)
        {
            int? contentIndex = m_graph.GetServiceContentIndex(servicePipId);
            return contentIndex != null && m_contentSet[contentIndex.Value];
        }

        /// <summary>
        /// Adds the content and returns whether the value was changed
        /// Returns true if the key is not found and it is now added.
        /// Returns false if the key was already added.
        /// Returns null if the key is ineligible to be added.
        /// </summary>
        public bool? Add(in FileOrDirectoryArtifact artifact)
        {
            int? contentIndex = m_graph.GetContentIndex(artifact);
            if (contentIndex != null)
            {
                return m_contentSet.TrySet(contentIndex.Value, true);
            }

            return null;
        }

        /// <summary>
        /// Adds the service input content marker and returns whether the value was changed
        /// Returns true if the key is not found and it is now added.
        /// Returns false if the key was already added.
        /// Returns null if the key is ineligible to be added.
        /// </summary>
        public bool? Add(PipId servicePipId)
        {
            int? contentIndex = m_graph.GetServiceContentIndex(servicePipId);
            if (contentIndex != null)
            {
                return m_contentSet.TrySet(contentIndex.Value, true);
            }

            return null;
        }
    }
}

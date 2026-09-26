// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Pips.DirectedGraph;

namespace BuildXL.Engine
{
    /// <summary>
    /// Owns a disposable directed graph until ownership is transferred.
    /// </summary>
    internal sealed class DirectedGraphOwnership : IDisposable
    {
        private IReadonlyDirectedGraph m_graph;

        /// <summary>
        /// Creates an ownership wrapper.
        /// </summary>
        internal DirectedGraphOwnership(IReadonlyDirectedGraph graph, bool ownsGraph)
        {
            m_graph = ownsGraph && graph is IDisposable ? graph : null;
        }

        /// <summary>
        /// Gets whether this wrapper owns the graph.
        /// </summary>
        internal bool IsOwned => m_graph != null;

        /// <summary>
        /// Takes ownership of a disposable graph.
        /// </summary>
        internal void TakeOwnership(IReadonlyDirectedGraph graph)
        {
            Contract.Requires(!IsOwned);
            Contract.Requires(graph is IDisposable);

            m_graph = graph;
        }

        /// <summary>
        /// Relinquishes ownership without disposing the graph. This is a no-op when the wrapper does not own a graph.
        /// </summary>
        internal void RelinquishOwnership()
        {
            m_graph = null;
        }

        /// <summary>
        /// Relinquishes ownership when this wrapper owns the specified graph.
        /// </summary>
        internal bool TryRelinquishOwnership(IReadonlyDirectedGraph graph)
        {
            Contract.Requires(graph != null);

            if (!ReferenceEquals(m_graph, graph))
            {
                return false;
            }

            RelinquishOwnership();
            return true;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            var graph = m_graph;
            m_graph = null;
            (graph as IDisposable)?.Dispose();
        }
    }
}

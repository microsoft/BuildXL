// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace Test.BuildXL.TestUtilities
{
    /// <summary>
    /// A simple representation of a directed graph with no parallel edges. This graph
    /// is determined by the total number of nodes (<see cref="NodeCount"/>) and a set
    /// of directed edges between them (<see cref="Edges"/>).
    /// </summary>
    public sealed class SimpleGraph
    {
        /// <summary>
        /// A directed edge from <see cref="Src"/> to <see cref="Dest"/>.
        /// </summary>
        public readonly struct Edge : IEquatable<Edge>
        {
            /// <summary>Source node index.</summary>
            public int Src { get; }

            /// <summary>Source node index.</summary>
            public int Dest { get; }

            /// <nodoc/>
            public Edge(int src, int dest)
            {
                Src = src;
                Dest = dest;
            }

            /// <nodoc/>
            public bool Equals(Edge other) => other.Src == Src && other.Dest == Dest;

            /// <inheritdoc/>
            public override bool Equals(object obj) => obj is Edge && Equals((Edge)obj);

            /// <inheritdoc/>
            public override int GetHashCode() => HashCodeHelper.Combine(Src, Dest);

            /// <nodoc/>
            public static bool operator ==(Edge lhs, Edge rhs)
            {
                return lhs.Equals(rhs);
            }

            /// <nodoc/>
            public static bool operator !=(Edge lhs, Edge rhs) => !(lhs == rhs);
        }

        private readonly Lazy<IReadOnlyCollection<Edge>> m_transitiveClosureLazy;
        private readonly Lazy<IReadOnlyCollection<Edge>> m_transitiveClosureInverseLazy;

        /// <summary>
        /// Number of nodes in the graph.
        /// </summary>
        [Pure]
        public int NodeCount { get; }

        /// <summary>
        /// Immutable set of nodes (all consecutive numbers from 0 to and not including <see cref="NodeCount"/>).
        /// </summary>
        [Pure]
        public IReadOnlyCollection<int> Nodes => Enumerable.Range(0, NodeCount).ToList();

        /// <summary>
        /// Immutable set of edges.
        /// </summary>
        [Pure]
        public IReadOnlyCollection<Edge> Edges { get; }

        /// <summary>
        /// Number of edges in the graph.
        /// </summary>
        [Pure]
        public int EdgeCount => Edges.Count;

        /// <summary>
        /// Identity Node -> Node relation (represented as a set of edges).
        /// </summary>
        [Pure]
        public IReadOnlyCollection<Edge> Identity { get; }

        /// <summary>
        /// Returns the transitive closure of all edges in the graph.
        /// </summary>
        [Pure]
        public IReadOnlyCollection<Edge> TransitiveClosure => m_transitiveClosureLazy.Value;

        /// <summary>
        /// Returns the reflexive transitive closure of all edges in the graph.
        /// </summary>
        [Pure]
        public IReadOnlyCollection<Edge> ReflexiveTransitiveClosure => TransitiveClosure.Union(Identity).ToList();

        /// <summary>
        /// The inverse of <see cref="TransitiveClosure"/>.
        /// </summary>
        [Pure]
        public IReadOnlyCollection<Edge> TransitiveClosureInverse => m_transitiveClosureInverseLazy.Value;

        /// <summary>
        /// The inverse of <see cref="ReflexiveTransitiveClosure"/>.
        /// </summary>
        [Pure]
        public IReadOnlyCollection<Edge> ReflexiveTransitiveClosureInverse => TransitiveClosureInverse.Union(Identity).ToList();

        [ContractInvariantMethod]
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Method is not empty only when CONTRACTS_LIGHT_INVARIANTS symbol is defined.")]
        private void Invariant()
        {
            Contract.Invariant(NodeCount > 0);
            Contract.Invariant(Edges != null);
            Contract.Invariant(Contract.ForAll(Edges, e => InRange(e, NodeCount)));
            Contract.Invariant(NoParallelEdges(Edges), "Multigraph not allowed");
        }

        /// <nodoc/>
        public SimpleGraph(int numNodes, IEnumerable<Edge> edges)
        {
            Contract.Requires(numNodes > 0);
            Contract.Requires(edges != null);
            Contract.RequiresForAll(edges, e => InRange(e, numNodes));
            Contract.Requires(NoParallelEdges(edges), "Multigraph not allowed");

            NodeCount = numNodes;
            Edges = edges.ToList();
            Identity = IdentityRelation(NodeCount);
            m_transitiveClosureLazy = Lazy.Create(ComputeEdgeClosure);
            m_transitiveClosureInverseLazy = Lazy.Create(() => Inverse(TransitiveClosure));

            // Calling invariant method explicitely because this is the only way to check it at least once.
            Invariant();
        }

        /// <summary>
        /// Returns whether this graph is a DAG (i.e., whether it has no cycles).
        /// </summary>
        [Pure]
        public bool IsDAG() => !TransitiveClosure.Intersect(Identity).Any();

        /// <summary>
        /// Computes the strict 'downstream' of <paramref name="nodes"/>, i.e., the set of all nodes reachable from
        /// <paramref name="nodes"/> by following the reverse edges of this graph.  The result does not include the
        /// nodes in <paramref name="nodes"/>.
        /// </summary>
        [Pure]
        public IEnumerable<int> ComputeDownstream(IEnumerable<int> nodes) => Join(nodes, TransitiveClosureInverse);

        /// <summary>
        /// Computes the reflexive 'downstream' of <paramref name="nodes"/>, i.e., the nodes returned by
        /// <see cref="ComputeDownstream(IEnumerable{int})"/> union <paramref name="nodes"/>.
        /// </summary>
        [Pure]
        public IEnumerable<int> ComputeReflexiveDownstream(IEnumerable<int> nodes) => Join(nodes, ReflexiveTransitiveClosureInverse);

        /// <summary>
        /// Computes the strict 'upstream' of <paramref name="nodes"/>, i.e., the set of all nodes reachable from
        /// <paramref name="nodes"/> by following the edges of this graph.  The result does not include the
        /// nodes in <paramref name="nodes"/>.
        /// </summary>
        [Pure]
        public IEnumerable<int> ComputeUpstream(IEnumerable<int> nodes) => Join(nodes, TransitiveClosure);

        /// <summary>
        /// Computes the reflexive 'upstream' of <paramref name="nodes"/>, i.e., the nodes returned by
        /// <see cref="ComputeUpstream(IEnumerable{int})"/> union <paramref name="nodes"/>.
        /// </summary>
        [Pure]
        public IEnumerable<int> ComputeReflexiveUpstream(IEnumerable<int> nodes) => Join(nodes, ReflexiveTransitiveClosure);

        /// <summary>
        /// Returns whether all edges are unique.
        /// </summary>
        [Pure]
        public static bool NoParallelEdges(IEnumerable<Edge> edges) => edges.Count() == edges.Distinct().Count();

        /// <summary>
        /// Returns whether both <see cref="Edge.Src"/> and <see cref="Edge.Dest"/> of an <see cref="Edge"/> are
        /// between 0 (inclusive) and <paramref name="upperBound"/> (exclusive).
        /// </summary>
        [Pure]
        public static bool InRange(Edge e, int upperBound) => InRange(e.Src, upperBound) && InRange(e.Dest, upperBound);

        /// <summary>
        /// Returns all edges in this graph whose destination node (<see cref="Edge.Dest"/>) is equal to <paramref name="node"/>.
        /// </summary>
        [Pure]
        public IEnumerable<Edge> IncomingEdges(int node) => Edges.Where(e => e.Dest == node);

        /// <summary>
        /// Returns all edges in this graph whose source node (<see cref="Edge.Src"/>) is equal to <paramref name="node"/>.
        /// </summary>
        [Pure]
        public IEnumerable<Edge> OutgoingEdges(int node) => Edges.Where(e => e.Src == node);

        /// <summary>
        /// Renders this graph's edges that can be passed as 'value' to the <see cref="Parse"/> method.
        /// </summary>
        [Pure]
        public string Render()
        {
            return string.Join(
                ", ",
                Edges.Select(e => I($"{e.Src} -> {e.Dest}")));
        }

        /// <inheritdoc/>
        public override string ToString() => Render();

        /// <summary>
        /// Parses a graph from a string representation.
        ///
        /// The <paramref name="value"/> string must be a comma separated list of binary
        /// tuples, where every tuple contains two integers (each of which must be positive
        /// and less than <paramref name="numNodes"/>) separated by either &lt;- or -&gt;.
        ///
        /// Example value for <paramref name="value"/>: 1 -&gt; 2, 2 &lt;- 3, 2 -&gt; 5
        /// </summary>
        [Pure]
        public static SimpleGraph Parse(int numNodes, string value)
        {
            Contract.Requires(numNodes >= 0);
            Contract.Requires(value != null);

            const string ReverseEdgeString = "<-";
            const string ForwardEdgeString = "->";

            if (value.Trim().Length == 0)
            {
                return new SimpleGraph(numNodes, new Edge[0]);
            }

            var edges = value
                .Split(',')
                .Select(tupleStr =>
                {
                    var isReverseEdge = tupleStr.Contains(ReverseEdgeString);
                    var separator = new[] { isReverseEdge ? ReverseEdgeString : ForwardEdgeString };
                    var nodeIndexes = tupleStr.Trim()
                        .Split(separator, StringSplitOptions.None)
                        .Select(s =>
                        {
                            int i;
                            Check(int.TryParse(s, out i), I($"Not an int '{s}' in '{tupleStr}'"));
                            return i;
                        })
                        .ToArray();
                    Check(nodeIndexes.Count() == 2, I($"Every edge must be between 2 nodes, instead, got: '{tupleStr}'"));
                    var outOfBoundNodes = nodeIndexes.Where(i => !InRange(i, numNodes));
                    Check(!outOfBoundNodes.Any(), "Nodes out of bounds: " + string.Join(", ", outOfBoundNodes));
                    return isReverseEdge
                        ? new Edge(nodeIndexes[1], nodeIndexes[0])
                        : new Edge(nodeIndexes[0], nodeIndexes[1]);
                })
                .ToArray();

            return new SimpleGraph(numNodes, edges);
        }

        /// <summary>
        /// Calls <see cref="Parse(int, string)"/> first, then <see cref="IsDAG"/> on the returned graph; if
        /// <see cref="IsDAG"/> returns <code>false</code>, throws <see cref="ArgumentException"/>, otherwise
        /// returns the graph.
        /// </summary>
        [Pure]
        public static SimpleGraph ParseDAG(int numNodes, string value)
        {
            Contract.Requires(numNodes > 0);
            Contract.Requires(value != null);

            var graph = Parse(numNodes, value);
            Check(graph.IsDAG(), I($"graph '{value}' is not a DAG"));
            return graph;
        }

        /// <summary>
        /// Standard relational closure operator.
        /// </summary>
        [Pure]
        public static IReadOnlyCollection<Edge> Closure(IReadOnlyCollection<Edge> relation)
        {
            var oldSize = 0;
            var closure = new HashSet<Edge>(relation);
            while (closure.Count > oldSize)
            {
                oldSize = closure.Count;
                closure.UnionWith(Join(closure, relation));
            }

            return closure.ToList();
        }

        /// <summary>
        /// Standard relational join operator between 2 binary relations.
        /// </summary>
        [Pure]
        public static IReadOnlyCollection<Edge> Join(IEnumerable<Edge> lhs, IEnumerable<Edge> rhs)
        {
            return lhs.Join(
                rhs,
                e1 => e1.Dest,
                e2 => e2.Src,
                (e1, e2) => new Edge(e1.Src, e2.Dest)).ToList();
        }

        /// <summary>
        /// Standard relational join operator between a set and a binary relation.
        /// </summary>
        [Pure]
        public static IReadOnlyCollection<int> Join(IEnumerable<int> lhs, IEnumerable<Edge> rhs)
        {
            return lhs.Join(
                rhs,
                node => node,
                edge => edge.Src,
                (node, edge) => edge.Dest).ToList();
        }

        /// <summary>
        /// Standard relational cross product operator betwen two sets.
        /// </summary>
        public static IEnumerable<Edge> Product(IEnumerable<int> lhs, IEnumerable<int> rhs)
        {
            return lhs.SelectMany(i => rhs.Select(j => new Edge(i, j)));
        }

        /// <summary>
        /// Standard relational inverse operator.
        /// </summary>
        [Pure]
        public static IReadOnlyCollection<Edge> Inverse(IEnumerable<Edge> relation)
        {
            return relation.Select(e => new Edge(src: e.Dest, dest: e.Src)).ToList();
        }

        /// <summary>
        /// Creates an identity binary relation which contains all '(i, i)' tuples, where 0 &lt;= i &lt; numNodes
        /// </summary>
        [Pure]
        public static IReadOnlyCollection<Edge> IdentityRelation(int numNodes) => Enumerable.Range(0, numNodes).Select(i => new Edge(i, i)).ToList();

        [Pure]
        private IReadOnlyCollection<Edge> ComputeEdgeClosure() => Closure(Edges);

        [Pure]
        private static bool InRange(int i, int upperBound) => i >= 0 && i < upperBound;

        private static void Check(bool condition, string errorMessage)
        {
            if (!condition)
            {
                throw new ArgumentException(errorMessage);
            }
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Pips.DirectedGraph
{
    /// <summary>
    /// Identifies a one-way edge within a DirectedGraph.
    /// </summary>
    public readonly struct Edge : IEquatable<Edge>
    {
        private const byte EdgeTypeShift = 31;

        private const uint LightEdgeBit = 1U << EdgeTypeShift;

        private const uint NodeIdMask = (1U << EdgeTypeShift) - 1;

        /// <summary>
        /// An invalid Node.
        /// </summary>
        public static readonly Edge Invalid = default(Edge);

        /// <summary>
        /// Creates a Node ID for some underlying integer value.
        /// </summary>
        /// <remarks>
        /// Since the value must have some meaning to a DirectedGraph, this constructor should primarily be called by NodeTables.
        /// The only other reasonable usage would be for temporary serialization (e.g. to a child process).
        /// </remarks>
        public Edge(NodeId other, bool isLight = false)
        {
            Contract.Requires(other.IsValid);
            Contract.Requires((other.Value & ~NodeIdMask) == 0);

            Value = other.Value | (isLight ? LightEdgeBit : 0);
        }

        /// <summary>
        /// Creates an edge representing the given underlying value.
        /// </summary>
        /// <remarks>
        /// This should be used only as the deserialization path for <see cref="Value"/>
        /// </remarks>
        internal Edge(uint value)
        {
            Value = value;
        }

        /// <summary>
        /// Source or target node represented by this edge
        /// </summary>
        public NodeId OtherNode => new NodeId(Value & NodeIdMask);

        /// <summary>
        /// Indicates if this is a 'light' (vs. heavy) edge.
        /// </summary>
        public bool IsLight => (Value & LightEdgeBit) != 0;

        /// <summary>
        /// Packed representation of an edge. The top bit indicates the edge type. The remaining bits form a <see cref="NodeId"/>.
        /// </summary>
        /// <remarks>
        /// This is intended for round-trip serialization. The representation is complete.
        /// </remarks>
        public uint Value { get; }

        /// <summary>
        /// Indicates if this edge and the one given represent the same underlying value. Note that it is only meaningful
        /// to compare edges generated from the same NodeTable, but that that condition is not enforced.
        /// </summary>
        public bool Equals(Edge other)
        {
            return other.Value == Value;
        }

        /// <summary>
        /// Indicates if a given object is an <see cref="Edge"/> equal to this one.
        /// </summary>
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return unchecked((int)Value);
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return this == Invalid ? "{Invalid}" : I($"{{Edge (id: {OtherNode.Value:x}, light: {IsLight})}}");
        }

        /// <summary>
        /// Equality operator for two edges
        /// </summary>
        public static bool operator ==(Edge left, Edge right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator for two NodeIds
        /// </summary>
        public static bool operator !=(Edge left, Edge right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Deserializes the edges from the current position in the reader
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public static Edge Deserialize(BuildXLReader reader)
        {
            return new Edge(reader.ReadUInt32());
        }

        /// <summary>
        /// Writes the edge to the current position in the writer
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011:ConsiderPassingBaseTypesAsParameters")]
        public void Serialize(BuildXLWriter writer)
        {
            writer.Write(Value);
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.Pips.DirectedGraph
{
    /// <summary>
    /// Identifies a unique Node within a DirectedGraph.
    /// </summary>
    [DebuggerTypeProxy(typeof(NodeIdDebugView))]
    public readonly struct NodeId : IEquatable<NodeId>
    {
        /// <summary>
        /// Maximum valid node ID value.
        /// </summary>
        public const uint MaxValue = int.MaxValue;

        /// <summary>
        /// Minimum valid node ID value.
        /// </summary>
        public const uint MinValue = 1;

        /// <summary>
        /// An invalid Node.
        /// </summary>
        public static readonly NodeId Invalid = default(NodeId);

        /// <summary>
        /// Maximum valid node ID
        /// </summary>
        public static readonly NodeId Max = new NodeId(MaxValue);

        /// <summary>
        /// Minimum valid node ID
        /// </summary>
        public static readonly NodeId Min = new NodeId(MinValue);

        /// <summary>
        /// Identifier of this Node as understood by the owning DirectedGraph.
        /// Higher values correspond to nodes added later to the graph. These values are guaranteed to be dense.
        /// </summary>
        /// <remarks>
        /// Node IDs are a single integer in memory. However, we wrap these integers in a struct to get a new type identity
        /// and the ability to customize the debugger representation.
        /// </remarks>
        public readonly uint Value;

        /// <summary>
        /// Creates a Node ID for some underlying integer value.
        /// </summary>
        /// <remarks>
        /// Since the value must have some meaning to a DirectedGraph, this constructor should primarily be called by NodeTables.
        /// The only other reasonable usage would be for temporary serialization (e.g. to a child process).
        /// </remarks>
        public NodeId(uint value)
        {
            Contract.Requires(value <= MaxValue);
            Contract.Requires(value >= MinValue);

            Value = value;
        }

        /// <summary>
        /// Indicates if this Node ID and the one given represent the same underlying value. Note that it is only meaningful
        /// to compare Node IDs generated from the same NodeTable, but that condition is not enforced.
        /// </summary>
        public bool Equals(NodeId other)
        {
            return other.Value == Value;
        }

        /// <summary>
        /// Determines whether a Node id is valid or not.
        /// </summary>
        public bool IsValid => this != Invalid;

        /// <summary>
        /// Indicates if a given object is a NodeId equal to this one.
        /// </summary>
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return (int)Value;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return this == Invalid ? "{Invalid}" : I($"{{Node (id: {Value:x})}}");
        }

        /// <summary>
        /// Equality operator for two NodeIds
        /// </summary>
        public static bool operator ==(NodeId left, NodeId right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator for two NodeIds
        /// </summary>
        public static bool operator !=(NodeId left, NodeId right)
        {
            return !left.Equals(right);
        }
    }

}

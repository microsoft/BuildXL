// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Optimized table of hierarchical names.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Individual names are inserted into the table and the caller receives a HierarchicalNameId as a handle
    /// to the name. Later, the HierarchicalNameId can be turned back into a textual name.
    /// </para>
    ///
    /// <para>
    /// This data structure only ever grows, names are never removed. The entire abstraction is completely
    /// thread-safe with the exception of the Serialize function.
    /// </para>
    ///
    /// <para>
    /// When all insertions have been done, the table can be frozen, which discards some transient state and
    /// cuts heap consumption to a minimum. Trying to add a new name once the table has been frozen will crash.
    /// </para>
    ///
    /// <para>
    /// Note that the root node symbol is represented with UnixPathRootSentinel on Unix based system, because this enables the
    /// existing logic to continue working with the Windows cases that model it as 'c:' or any other volume identifier.
    /// A HierarchicalNameId with a UnixPathRootSentinel value is regarded as invalid, so is the node acquired for it with GetContainer().
    /// </para>
    /// </remarks>
    public class HierarchicalNameTable
    {
        // The implementation is designed to be thread-safe on reads. This is achieved by
        // carefully controlling the way new data is inserted into the table. We never reallocate
        // any buffers for example.
        //
        // There is no acquire fence in the code that reads from the table since it is assumed
        // the CLR does all the right fencing automatically before publishing arrays. If it
        // weren't doing that, it would lead to state potentially leaking through uninitialized
        // heap objects which could directly lead to type safety violations.

        /// <summary>
        /// This is the value used to indicating the root entry of a HierarchicalNameTable representing Unix based file system entries.
        /// Just like 'c:' - or any volume identifier is inserted as root entry on Windows systems - this entry gets treated
        /// as '/' in the functions that generate string representations or enumerations of the HierarchicalNameTable etc.
        /// </summary>
        public static readonly string UnixPathRootSentinel = string.Empty;

        /// <summary>
        /// A single component within the graph
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
        protected struct Node
        {
            /// <summary>
            /// StringId for this component
            /// </summary>
            public readonly StringId Component;

            /// <summary>
            /// ID of the parent node in the owning path table. Since this ID is defined to be in the table owning the node,
            /// we re-use the tag bit part of <see cref="HierarchicalNameId"/> as the node's externally-settable <see cref="NameFlags"/> (rather
            /// than as the debug ID of the owning table; that adds little value here since nodes are private to the table).
            /// </summary>
            /// <remarks>
            /// Mutability: The flags part is mutable. See <see cref="SetFlags"/>. The container part is immutable.
            /// </remarks>
            public int ContainerAndFlags;

            /// <summary>
            /// Depth of this node, i.e., the number of components.
            /// </summary>
            public int Depth => GetIndexFromValue(DepthAndExtendedFlags);

            /// <summary>
            /// Depth and ad
            /// </summary>
            public int DepthAndExtendedFlags;

            /// <summary>
            /// ID of the next sibling (node sharing the same container). Nodes directly under a container form a singly linked list.
            /// </summary>
            /// <remarks>
            /// Mutability: Immutable. Siblings are inserted at the head (<see cref="FirstChild"/> of the container is modified).
            /// </remarks>
            public readonly HierarchicalNameId NextSibling;

            /// <summary>
            /// Integer representation of <see cref="FirstChild"/>; needed as a mutable int for <see cref="InterlockedSetFirstChild"/>.
            /// </summary>
            private int m_firstChildValue;

            /// <summary>
            /// Creates a new node with the given string component, containing name, and <see cref="NameFlags.None"/>.
            /// </summary>
            public Node(StringId component, HierarchicalNameId container, int depth)
                : this(component, container.Value & ~DebugTagShiftedMask, depth)
            {
                Contract.Assume(
                    !container.IsValid || (container.Value & ~DebugTagShiftedMask) != 0,
                    "Only the null name ID may map to buffer 0 / index 0");
            }

            /// <nodoc />
            private Node(StringId component, int containerAndFlags, int depthAndExtendedFlags, HierarchicalNameId nextSibling = default)
            {
                Component = component;
                ContainerAndFlags = containerAndFlags;
                NextSibling = nextSibling;
                DepthAndExtendedFlags = depthAndExtendedFlags;
                m_firstChildValue = default(HierarchicalNameId).Value;
            }

            /// <summary>
            /// Updates the node to have the given next sibling.
            /// </summary>
            /// <remarks>
            /// Node cannot already have a next sibling.
            /// </remarks>
            public Node WithSibling(HierarchicalNameId nextSibling)
            {
                Contract.Assert(!NextSibling.IsValid);
                return new Node(Component, ContainerAndFlags, Depth, nextSibling);
            }

            /// <summary>
            /// Returns the <see cref="HierarchicalNameId"/> of this node's container.
            /// </summary>
            /// <remarks>
            /// Name IDs encode have room for a 'debug tag' that points to the owning table. But we use those bits differently in a <see cref="Node"/>;
            /// so, we reconstitute it here.
            /// </remarks>
            public HierarchicalNameId GetContainer(int ownerDebugTag)
            {
                int containerWithoutOwner = ContainerAndFlags & ~DebugTagShiftedMask;

                // The null node has a special owner-less ID (and we don't allow flags on it). Don't add an owner tag or equality would break.
                return containerWithoutOwner == 0
                    ? new HierarchicalNameId(0)
                    : new HierarchicalNameId(containerWithoutOwner | (ownerDebugTag << DebugTagShift));
            }

            /// <summary>
            /// ID of the first child (if this node is an non-empty container). All children can be found by following the <see cref="NextSibling"/>
            /// pointers of the children, starting from this first child.
            /// </summary>
            /// <remarks>
            /// Mutability: Mutable; changes on every addition of a child (points to the newest child; linked list insert at head).
            /// </remarks>
            public HierarchicalNameId FirstChild => new HierarchicalNameId(m_firstChildValue);

            /// <summary>
            /// Gets the node's container's index.
            /// </summary>
            public int ContainerIndex => GetIndexFromValue(ContainerAndFlags);

            /// <summary>
            /// Returns the flags that have been set externally via <see cref="SetFlags"/>
            /// </summary>
            public NameFlags Flags
            {
                get
                {
                    int containerAndFlags = Volatile.Read(ref ContainerAndFlags);
                    return ComputeFlagsFromContainerAndFlags(containerAndFlags);
                }
            }

            /// <summary>
            /// Returns the flags that have been set externally via <see cref="SetExtendedFlags"/>
            /// </summary>
            public ExtendedNameFlags ExtendedFlags
            {
                get
                {
                    int depthAndExtendedFlags = Volatile.Read(ref DepthAndExtendedFlags);
                    return ComputeExtendedFlagsFromDepthAndFlags(depthAndExtendedFlags);
                }
            }

            /// <summary>
            /// Returns the flags from a specified container and flags.
            /// </summary>
            public static NameFlags ComputeFlagsFromContainerAndFlags(int containerAndFlags)
            {
                return (NameFlags)((containerAndFlags >> DebugTagShift) & DebugTagValueMask);
            }

            /// <summary>
            /// Returns the extended flags from a specified depth and extended flags.
            /// </summary>
            public static ExtendedNameFlags ComputeExtendedFlagsFromDepthAndFlags(int depthAndExtendedFlags)
            {
                return (ExtendedNameFlags)((depthAndExtendedFlags >> DebugTagShift) & DebugTagValueMask);
            }

            /// <summary>
            /// Updates <see cref="FirstChild"/> with a release fence.
            /// </summary>
            public bool InterlockedSetFirstChild(HierarchicalNameId firstChild, HierarchicalNameId expectedFirstChild)
            {
                return Interlocked.CompareExchange(ref m_firstChildValue, firstChild.Value, expectedFirstChild.Value)
                    == expectedFirstChild.Value;
            }

            /// <summary>
            /// Serializes a node
            /// </summary>
            internal void Serialize(BuildXLWriter writer)
            {
                Contract.Requires(writer != null);
                writer.Write(Component);
                writer.Write(ContainerAndFlags);
                writer.WriteCompact(DepthAndExtendedFlags);
                writer.Write(NextSibling.Value);
                writer.Write(FirstChild.Value);
            }

            /// <summary>
            /// Deserializes a Node
            /// </summary>
            internal static Node Deserialize(BuildXLReader reader)
            {
                Contract.Requires(reader != null);
                Node result = new Node(
                    reader.ReadStringId(),
                    containerAndFlags: reader.ReadInt32(),
                    depthAndExtendedFlags: reader.ReadInt32Compact(),
                    nextSibling: new HierarchicalNameId(reader.ReadInt32()));

                result.m_firstChildValue = reader.ReadInt32();
                return result;
            }
        }

        /// <summary>
        /// The default name expander which does no replacement of hierarchical name segments.
        /// </summary>
        public readonly NameExpander DefaultExpander = new NameExpander();

        /// <summary>
        /// Whether the table is valid
        /// </summary>
        public bool IsValid = true;

        /// <summary>
        /// Invalidates the PathTable to ensure it is no longer used
        /// </summary>
        public void Invalidate()
        {
            IsValid = false;
        }

        /// <summary>
        /// Defines behavior when expanding hierarchical name IDs.
        /// </summary>
        public class NameExpander
        {
            // Using lossy cache to cache path expansions. The same paths are commonly expanded multiple times
            // in a row for a number of reasons. This gives a high probability of avoiding an allocation
            // when redundant path expansion requests come in.
            internal readonly ObjectCache<HierarchicalNameId, string> ExpansionCache;

            internal readonly char ExpansionCacheChar;

            /// <summary>
            /// Creates a new name expander
            /// </summary>
            /// <param name="expansionCacheSize">the size of the expansion cache</param>
            public NameExpander(int expansionCacheSize = 7013)
            {
                ExpansionCache = expansionCacheSize <= 0 ? null : new ObjectCache<HierarchicalNameId, string>(expansionCacheSize);
                ExpansionCacheChar = PathFormatter.GetPathSeparator(PathFormat.HostOs);
            }

            /// <summary>
            /// Gets the length of the current string in the hierarchical name
            /// </summary>
            /// <param name="name">the name id</param>
            /// <param name="stringTable">the string table to use to expand strings</param>
            /// <param name="stringId">the string id of the current name</param>
            /// <param name="nameFlags">the name flags of the current name</param>
            /// <param name="expandContainer">indicates whether the container name should be expanded into the destination string</param>
            /// <returns>the length of the current name</returns>
            public virtual int GetLength(HierarchicalNameId name, StringTable stringTable, StringId stringId, NameFlags nameFlags, out bool expandContainer)
            {
                Contract.Requires(stringTable != null);
                Contract.Requires(stringId.IsValid);

                expandContainer = true;
                return stringTable.GetLength(stringId);
            }

            /// <summary>
            /// Copies the current hierarchical name string to the character buffer and returns the copied length
            /// </summary>
            /// <param name="name">the name id</param>
            /// <param name="stringTable">the string table to use to expand strings</param>
            /// <param name="stringId">the string id of the current name</param>
            /// <param name="nameFlags">the name flags of the current name</param>
            /// <param name="buffer">the character buffer for storing the name</param>
            /// <param name="endIndex">the end index in the buffer to end copying characters</param>
            /// <returns>the number of characters copied into the buffer</returns>
            public virtual int CopyString(HierarchicalNameId name, StringTable stringTable, StringId stringId, NameFlags nameFlags, char[] buffer, int endIndex)
            {
                Contract.Requires(stringTable != null);
                Contract.Requires(stringId.IsValid);
                Contract.Requires(buffer != null);
                Contract.Requires(endIndex >= 0);

                return stringTable.CopyString(stringId, buffer, endIndex, isEndIndex: true);
            }
        }

        /// <summary>
        /// Comparer for sorting name IDs as if they were expanded (but without actually expanding them).
        /// Comparison performs at most one string-comparison of name components.
        /// </summary>
        public sealed class ExpandedHierarchicalNameComparer : IComparer<HierarchicalNameId>
        {
            /// <summary>
            /// Associated name table.
            /// </summary>
            public readonly HierarchicalNameTable NameTable;

            /// <nodoc />
            internal ExpandedHierarchicalNameComparer(HierarchicalNameTable table)
            {
                Contract.Requires(table != null);
                NameTable = table;
            }

            /// <inheritdoc />
            public int Compare(HierarchicalNameId x, HierarchicalNameId y)
            {
                // Fast path for trivial equality
                if (x == y)
                {
                    return 0;
                }

                if (!x.IsValid)
                {
                    return -1;
                }

                if (!y.IsValid)
                {
                    return 1;
                }

                // We have two names x and y of potentially different depths, i.e.,
                // x = a\b\c\d
                // y = d\e
                // We observe that there are two cases to handle:
                // - x is a prefix of y (or y is a prefix of x):
                //   There exists some suffix of y (or x) to chop off, at which point we have two equal IDs at the same depth.
                //   In this case, we don't actually have to ask the string table to compare any component strings.
                //   Example: (a\b\c ; a\b\c\d)
                // - x and y share a (possibly empty) prefix, but then diverge on some component:
                //   We need to find a single pair of components to compare with the string table; these are always at the same depth,
                //   and immediately follow the common prefix.
                //   Examples (comparison in {}): (a\b\{c} ; a\b\{d}), (a\b\{c}\d ; a\b\{e}\f)
                // So, we proceed by lining up the two paths to the same depth (first case) and then walk both back to find the (maybe empty) common prefix.
                Node xn = NameTable.GetNode(x);
                Node yn = NameTable.GetNode(y);

                // First case: Chop off a suffix of either X or Y, and see if one is a prefix of the other.
                if (xn.Depth > yn.Depth)
                {
                    do
                    {
                        HierarchicalNameId parentOfX = xn.GetContainer(NameTable.DebugTag);
                        Contract.Assume(parentOfX.IsValid, "We do not go past the depth of the other (valid) node.");

                        if (parentOfX == y)
                        {
                            // X has Y as a prefix.
                            return 1;
                        }

                        x = parentOfX;
                        xn = NameTable.GetNode(x);
                    }
                    while (xn.Depth > yn.Depth);
                }
                else if (yn.Depth > xn.Depth)
                {
                    do
                    {
                        HierarchicalNameId parentOfY = yn.GetContainer(NameTable.DebugTag);
                        Contract.Assume(parentOfY.IsValid, "We do not go past the depth of the other (valid) node.");

                        if (parentOfY == x)
                        {
                            // Y has X as a prefix.
                            return -1;
                        }

                        y = parentOfY;
                        yn = NameTable.GetNode(y);
                    }
                    while (yn.Depth > xn.Depth);
                }

                // Second case: Given the equal-depth paths, find the components to compare (the one right after any common prefix).
                //              Note that the common prefix may be the empty-path (invalid name ID).
                while (true)
                {
                    HierarchicalNameId parentOfX = xn.GetContainer(NameTable.DebugTag);
                    HierarchicalNameId parentOfY = yn.GetContainer(NameTable.DebugTag);

                    if (parentOfX == parentOfY)
                    {
                        return NameTable.m_comparer.Compare(xn.Component, yn.Component);
                    }

                    xn = NameTable.GetNode(parentOfX);
                    yn = NameTable.GetNode(parentOfY);
                }
            }
        }

        /// <summary>
        /// Enumerator for walking up from a name to the root, finding each name with the given flags set.
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
        public struct ContainerEnumerator : IEnumerator<HierarchicalNameId>, IEnumerable<HierarchicalNameId>
        {
            private readonly HierarchicalNameTable m_table;
            private HierarchicalNameId m_next;
            private readonly NameFlags m_flagsFilter;
            private bool m_rootNodeReached;

            internal ContainerEnumerator(HierarchicalNameTable table, HierarchicalNameId leaf, NameFlags flagsFilter)
            {
                m_table = table;
                Current = HierarchicalNameId.Invalid;
                m_next = leaf;
                m_flagsFilter = flagsFilter;
                m_rootNodeReached = false;
            }

            /// <inheritdoc/>
            public HierarchicalNameId Current { get; private set; }

            /// <inheritdoc/>
            public void Dispose()
            {
            }

            /// <inheritdoc/>
            object System.Collections.IEnumerator.Current => Current;

            /// <inheritdoc/>
            public bool MoveNext()
            {
                while (m_next.IsValid && !m_rootNodeReached)
                {
                    Current = m_next;
                    Node currentNode = m_table.GetNode(Current);
                    m_next = currentNode.GetContainer(m_table.DebugTag);

                    // On Unix based systems we want to omit the root volume symbol when enumerating a container
                    m_rootNodeReached = 
                        OperatingSystemHelper.IsUnixOS && 
                        (!m_next.IsValid || m_table.ExpandName(m_next) == Path.VolumeSeparatorChar.ToString());

                    if ((currentNode.Flags & m_flagsFilter) == m_flagsFilter)
                    {
                        return true;
                    }
                }

                return false;
            }

            /// <inheritdoc/>
            public void Reset()
            {
                throw new NotSupportedException();
            }

            /// <summary>
            /// Gets the enumerator
            /// </summary>
            public ContainerEnumerator GetEnumerator()
            {
                return this;
            }

            IEnumerator<HierarchicalNameId> IEnumerable<HierarchicalNameId>.GetEnumerator()
            {
                return this;
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return this;
            }
        }

        /// <summary>
        /// Enumerator for visiting the immediate children of a name (all names with it as a container).
        /// </summary>
        [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
        public struct ImmediateChildEnumerator : IEnumerator<HierarchicalNameId>, IEnumerable<HierarchicalNameId>
        {
            private readonly HierarchicalNameTable m_table;
            private HierarchicalNameId m_next;

            internal ImmediateChildEnumerator(HierarchicalNameTable table, HierarchicalNameId container)
            {
                Contract.Requires(container.IsValid);
                m_table = table;
                Current = HierarchicalNameId.Invalid;
                m_next = table.GetNode(container).FirstChild;
            }

            /// <inheritdoc/>
            public HierarchicalNameId Current { get; private set; }

            /// <inheritdoc/>
            public void Dispose()
            {
            }

            /// <inheritdoc/>
            object System.Collections.IEnumerator.Current => Current;

            /// <inheritdoc/>
            public bool MoveNext()
            {
                if (m_next.IsValid)
                {
                    Current = m_next;
                    m_next = m_table.GetNode(Current).NextSibling;
                    return true;
                }

                return false;
            }

            /// <inheritdoc/>
            public void Reset()
            {
                throw new NotSupportedException();
            }

            /// <nodoc />
            public ImmediateChildEnumerator GetEnumerator()
            {
                return this;
            }

            IEnumerator<HierarchicalNameId> IEnumerable<HierarchicalNameId>.GetEnumerator()
            {
                return this;
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return this;
            }
        }

        /// <summary>
        /// Enumerator for visiting all descendants of a name (all names with this name as a prefix).
        /// </summary>
        /// <remarks>
        /// We traverse recursively down with a constant amount of state (unlike a recursive visit function with many stack frames).
        /// We can do so since each node has a FirstChild pointer, NextSibling, and parent pointer.
        /// - Leaf case: For a node with only leaf children, we can visit all children by moving to the first child and then walking NextSibling.
        /// - Two-level case: We can extend the leaf case if, for each non-leaf child visited, we eventually traverse that non-leaf child's NextSibling pointer.
        ///                   After visiting the leaf grandchildren and stopping at a null NextSibling, we can move to Parent->NextSibling of the grandchild.
        /// This extends to deeper trees by induction.
        /// </remarks>
        [SuppressMessage("Microsoft.Naming", "CA1710:IdentifiersShouldHaveCorrectSuffix")]
        public struct RecursiveChildEnumerator : IEnumerator<HierarchicalNameId>, IEnumerable<HierarchicalNameId>
        {
            private readonly HierarchicalNameTable m_table;
            private readonly HierarchicalNameId m_start;
            private HierarchicalNameId m_next;

            internal RecursiveChildEnumerator(HierarchicalNameTable table, HierarchicalNameId container)
            {
                Contract.Requires(container.IsValid);
                m_table = table;
                m_start = container;
                Current = HierarchicalNameId.Invalid;
                m_next = table.GetNode(container).FirstChild;
            }

            /// <inheritdoc/>
            public HierarchicalNameId Current { get; private set; }

            /// <inheritdoc/>
            public void Dispose()
            {
            }

            /// <inheritdoc/>
            object System.Collections.IEnumerator.Current => Current;

            /// <inheritdoc/>
            public bool MoveNext()
            {
                if (m_next.IsValid)
                {
                    // Visit this node.
                    Current = m_next;

                    Node currentNode = m_table.GetNode(Current);
                    if (currentNode.FirstChild.IsValid)
                    {
                        // After visiting this node, we must visit all of its descendants (this is depth-first).
                        m_next = currentNode.FirstChild;
                    }
                    else if (currentNode.NextSibling.IsValid)
                    {
                        // For leaves, we are done with descendants immediately and so end up at the next sibling.
                        m_next = currentNode.NextSibling;
                    }
                    else
                    {
                        // We are done with a level. Walk up to the first level that needs to be resumed, if any.
                        Node parentNode = currentNode;

                        while (true)
                        {
                            if (parentNode.NextSibling.IsValid)
                            {
                                m_next = parentNode.NextSibling;
                                break;
                            }
                            else
                            {
                                HierarchicalNameId nextParentId = parentNode.GetContainer(m_table.DebugTag);
                                Contract.Assume(nextParentId.IsValid, "We started from a valid name, and should stop when we find it.");

                                if (nextParentId == m_start)
                                {
                                    m_next = HierarchicalNameId.Invalid;
                                    break;
                                }
                                else
                                {
                                    parentNode = m_table.GetNode(nextParentId);
                                }
                            }
                        }
                    }

                    return true;
                }
                else
                {
                    return false;
                }
            }

            /// <inheritdoc/>
            public void Reset()
            {
                throw new NotSupportedException();
            }

            /// <nodoc />
            public RecursiveChildEnumerator GetEnumerator()
            {
                return this;
            }

            IEnumerator<HierarchicalNameId> IEnumerable<HierarchicalNameId>.GetEnumerator()
            {
                return this;
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return this;
            }
        }

        /// <summary>
        /// Flags that may be set on a name. See <see cref="SetFlags"/>.
        /// </summary>
        /// <remarks>
        /// Since a <see cref="HierarchicalNameTable"/> is shared, these flags may be used for
        /// faster queries (i.e., finding the first parent name with some flag set, hinting at set membership),
        /// but not e.g. definite set membership. See <see cref="SetFlags"/>.
        /// </remarks>
        [SuppressMessage("Microsoft.Usage", "CA2217:DoNotMarkEnumsWithFlags", Justification = "These are intended as flags.")]
        [Flags]
        public enum NameFlags : byte
        {
            /// <summary>
            /// No flags are set
            /// </summary>
            None = 0,

            /// <summary>
            /// The name has been marked as significant.
            /// </summary>
            Marked = 1,

            /// <summary>
            /// The name has been marked as (semantic) root container.
            /// </summary>
            Root = 2,

            /// <summary>
            /// The name has been sealed such that further children should not be added.
            /// </summary>
            Sealed = 4,

            /// <summary>
            /// The name has been marked as a container.
            /// </summary>
            Container = 8,

            // No more flags may be added without increasing the size of the DebugTag part of a name ID.

            /// <summary>
            /// All flags are set
            /// </summary>
            All = DebugTagValueMask,
        }

        /// <summary>
        /// Extended flags that users can use for other purposes.
        /// </summary>
        [Flags]
        public enum ExtendedNameFlags : byte
        {
            /// <summary>
            /// No flags are set
            /// </summary>
            None = 0,

            /// <summary>
            /// Flag with value 1.
            /// </summary>
            Flag1 = 1,

            /// <summary>
            /// Flag with value 2.
            /// </summary>
            Flag2 = 2,

            /// <summary>
            /// Flag with value 4.
            /// </summary>
            Flag3 = 4,

            /// <summary>
            /// Flag with value 8.
            /// </summary>
            Flag4 = 8,

            // No more flags may be added without increasing the size of the DebugTag part of a name ID.

            /// <summary>
            /// All flags are set.
            /// </summary>
            All = DebugTagValueMask,
        }

        /// <summary>
        /// Max value of a debug tag
        /// </summary>
        private const int MaxDebugTagValue = NumHierarchicalNameTableIds;

        /// <summary>
        /// Bit mask for valid debug tags (these are low bits; masking value should be already shifted right)
        /// </summary>
        private const int DebugTagValueMask = NumHierarchicalNameTableIdsMask;

        /// <summary>
        /// Bit mask for the debug tag part of an ID (these are higher bits; the masking value should not be shifted).
        /// </summary>
        private const int DebugTagShiftedMask = DebugTagValueMask << DebugTagShift;

        /// <summary>
        /// How many bits to shift within an id to find the debug tag.
        /// </summary>
        internal const int DebugTagShift = 32 - NumHierarchicalNameTableIdsBits;

        /// <summary>
        /// Bit width of node entries per entry buffer in the node buffer (m_nodes)
        /// Number of node entries by entry buffer is 2^NodesPerEntryBufferBitWidth
        /// Value should be such that entry buffer doesn't have to go to large object heap.
        /// </summary>
        private const int NodesPerEntryBufferBitWidth = 12;

        // We allocate the bits of a 32-bit HierarchicalNameId value as follows:
        // - hierarchical name table ID (used for debugging only; see below) OR node tag (see Node) [4 bits]
        // - Buffer index [28 bits]
        private const int NumHierarchicalNameTableIdsBits = 4;
        private const int NumHierarchicalNameTableIds = 1 << NumHierarchicalNameTableIdsBits;
        private const int NumHierarchicalNameTableIdsMask = NumHierarchicalNameTableIds - 1;

        // sentinel value
        private static readonly HierarchicalNameId s_nullNode = HierarchicalNameId.Invalid;

        // The following static fields provide debugging support for HierarchicalNameId, without actually embedding HierarchicalNameTable references in the IDs.
        // We stamp the top few bits with an ID for the owning table, so that the debug viewer for HierarchicalNameId can possibly look it up
        // and expand the name into something human-readable. Note that each table's ID is used only once, to remove any possibility
        // that a HierarchicalNameId is ever correlated to the wrong table. The highest table ID (NumHierarchicalTableIdsMask) is a catch-all for
        // tables for which we will not be able to expand ids.
        // There are two key cases in which this limitation is just fine:
        // - Debugging the bxl.exe executable (likely only a few tables constructed)
        // - Debugging a single test (likely one or few tables constructed)
        // When debugging a larger test run, it is possible that later-constructed HierarchicalNameIds will be unable to show their expanded names.
        // A lock must be held on s_tableInstancesForDebug when modifying it or s_nextDebugTag.
        //
        // Notes on debugTag serialization:
        // Currently the debug tag is preserved through serialization since it is also embedded in HierarchicalNameIds belonging to the table.
        // The debug information could easily be stripped when serializing. But then it would need to be added back when deserializing. This is
        // certainly possible, but it is impractical since most of the deserialization of table members is decentralized. It would require waiting
        // on the owning table's deserialization to start to allow it to acquire a debugTag and then passing that to all sites where a member is
        // deserialized. Instead we take the easier road of just preserving the debug tag.
        //
        // This strategy works well as long as tables created in the domain don't clash with deserialized tables in the same domain. If a table
        // in the current domain is created and then a separate table that happened to use the same debug ids is deserialized (or vice versa), and
        // both tables are used at the same time, there will be crosstalk and the table returned by DebugTryGetTableForId() will be wrong. This
        // will result in the debug strings being wrong.
        private static WeakReference<HierarchicalNameTable>[] s_tableInstancesForDebug = new WeakReference<HierarchicalNameTable>[MaxDebugTagValue - 1];

        // We start the debug tags at 1 (and lose one table slot) to flush out code that doesn't mask properly (a debug tag of zero is special in that respect).
        private static int s_nextDebugTag = 1;

        /// <summary>
        /// Guards the concurrent changes to s_tableInstancesForDebug and s_nextDebugTag.
        /// </summary>
        private static readonly object s_tableInstanceLock = new object();

        // a value between 0..15 to OR into produced id values for the sake of debugging

        private ConcurrentBigSet<Node> m_nodeMap;
        private readonly BigBuffer<Node> m_nodes;
        private int m_frozenNodeCount;

        private readonly StringTable m_stringTable;

        private IEqualityComparer<StringId> m_equalityComparer;
        private readonly IComparer<StringId> m_comparer;
        private readonly bool m_ignoreCase;

        private readonly char m_separator;

        /// <summary>
        /// Whether it is being serialized
        /// </summary>
        private volatile bool m_isSerializationInProgress;

        /// <summary>
        /// Comparer of <see cref="HierarchicalNameId"/>s, as if they were expanded.
        /// </summary>
        public readonly ExpandedHierarchicalNameComparer ExpandedNameComparer;

        /// <summary>
        /// Initializes a new hierarchical name table.
        /// </summary>
        /// <param name="stringTable">The string table to use to managed text strings.</param>
        /// <param name="ignoreCase">Whether this table should be case-sensitive.</param>
        /// <param name="separator">Character used to separate components within hierarchical names</param>
        public HierarchicalNameTable(StringTable stringTable, bool ignoreCase, char separator)
            : this(stringTable, ignoreCase, separator, disableDebugTag: false)
        {
            Contract.Requires(stringTable != null);
        }

        internal static IEqualityComparer<StringId> CreateEqualityComparer(StringTable stringTable, bool ignoreCase)
        {
            if (ignoreCase)
            {
                return new CaseInsensitiveStringIdEqualityComparer(stringTable);
            }

            return new OrdinalStringIdEqualityComparer();
        }

        internal static IComparer<StringId> CreateComparer(StringTable stringTable, bool ignoreCase)
        {
            if (ignoreCase)
            {
                return new CaseInsensitiveStringIdComparer(stringTable);
            }
            else
            {
                return new OrdinalStringIdComparer(stringTable);
            }
        }

        /// <summary>
        /// This is a cache for insertions into the table. This is owned by the table for sake of having a place to
        /// reference the cache, but insertion and retrieval is handled by supporting structures (AbsolutePath, FullSymbol)
        /// </summary>
        /// <remarks>
        /// 15331 was chosen as a prime number that gave a reasonably good return on insertions as experienced when
        /// building a medium sized directory in OneCore
        /// </remarks>
        internal readonly ObjectCache<StringSegment, HierarchicalNameId> InsertionCache = new ObjectCache<StringSegment, HierarchicalNameId>(15331);

        /// <summary>
        /// Constructor used for deserialization
        /// </summary>
        protected HierarchicalNameTable(SerializedState state, StringTable stringTable, bool ignoreCase, char separator)
            : this(stringTable, ignoreCase, separator, disableDebugTag: true)
        {
            Contract.Requires(state != null);
            Contract.Requires(stringTable != null);

            // We passed disabledDebugTag: true so now we re-instate the deserialized debug tag (instead of allocating one).
            // TODO:409239: This is broken. On deserialization, allocate a tag and then remap deserialized paths.
            DebugTag = state.DebugTag;
            lock (s_tableInstanceLock)
            {
                if (state.DebugTag < s_tableInstancesForDebug.Length)
                {
                    s_tableInstancesForDebug[state.DebugTag] = new WeakReference<HierarchicalNameTable>(this);
                }
            }

            m_frozenNodeCount = state.NodeCount;
            m_nodeMap = state.NodeMap;
            m_nodes = m_nodeMap.GetItemsUnsafe();
        }

        /// <summary>
        /// Initializes a new hierarchical name table.
        /// </summary>
        /// <param name="stringTable">The string table to use to managed text strings.</param>
        /// <param name="ignoreCase">Whether this table should be case-sensitive.</param>
        /// <param name="separator">Character used to separate components within hierarchical names</param>
        /// <param name="disableDebugTag">Whether the debugTag should be incremented and used to publish this table for debugging.
        /// This should only be set to false when deserializing an existing table since the debugTag will be set back to the deserialized value
        /// TODO:409239: We also disable this when reloading multiple path tables, as a workaround for debug tag collisions. Need to properly serialize and re-map debug tags.
        /// </param>
        protected HierarchicalNameTable(StringTable stringTable, bool ignoreCase, char separator, bool disableDebugTag = false)
        {
            Contract.Requires(stringTable != null, "StringTable can't be null.");
            m_stringTable = stringTable;
            m_ignoreCase = ignoreCase;
            m_equalityComparer = CreateEqualityComparer(m_stringTable, ignoreCase);
            m_comparer = CreateComparer(m_stringTable, ignoreCase);
            ExpandedNameComparer = new ExpandedHierarchicalNameComparer(this);
            m_separator = separator;

            // debugging support.
            if (!disableDebugTag)
            {
                lock (s_tableInstanceLock)
                {
                    while (s_nextDebugTag < DebugTagValueMask && s_tableInstancesForDebug[s_nextDebugTag] != null)
                    {
                        ++s_nextDebugTag;
                    }

                    // if s_nextDebugTag reaches the highest Id, i.e., DebugTagValueMask, then it highest id is a catchall, and id's stamped with it
                    // cannot be expanded at debug time.
                    DebugTag = s_nextDebugTag;

                    if (s_nextDebugTag < DebugTagValueMask)
                    {
                        Contract.Assert(s_tableInstancesForDebug[DebugTag] == null, I($"Should have had an empty entry in debug instance table for tag {DebugTag}."));
                        s_tableInstancesForDebug[DebugTag] = new WeakReference<HierarchicalNameTable>(this);
                        ++s_nextDebugTag;
                    }
                }
            }
            else
            {
                // Debug support disabled.
                DebugTag = DebugTagValueMask;
            }

            m_nodeMap = new ConcurrentBigSet<Node>(itemsPerEntryBufferBitWidth: NodesPerEntryBufferBitWidth);
            m_nodes = m_nodeMap.GetItemsUnsafe();

            // Add Invalid as first item
            m_nodeMap.AddItem(new NodePendingItem(m_equalityComparer, new Node(StringId.Invalid, HierarchicalNameId.Invalid, depth: 0)));
        }

        /// <summary>
        /// Resets the static debugging state.
        /// </summary>
        public static void ResetStaticDebugState()
        {
            lock (s_tableInstanceLock)
            {
                s_nextDebugTag = 1;
                s_tableInstancesForDebug = new WeakReference<HierarchicalNameTable>[MaxDebugTagValue - 1];
            }
        }

        /// <summary>
        /// Looks for a name in the table and returns its id if found.
        /// </summary>
        public bool TryGetName(StringId[] components, out HierarchicalNameId hierarchicalNameId)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(components != null);
            Contract.Ensures(Contract.Result<bool>() == Contract.ValueAtReturn(out hierarchicalNameId).IsValid);

            return TryGetName(HierarchicalNameId.Invalid, components, out hierarchicalNameId);
        }

        /// <summary>
        /// Looks for a name in the table and returns its id if found.
        /// </summary>
        public bool TryGetName(HierarchicalNameId parent, StringId[] components, out HierarchicalNameId hierarchicalNameId)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(components != null);
            Contract.Ensures(Contract.Result<bool>() == Contract.ValueAtReturn(out hierarchicalNameId).IsValid);

            int componentIndex = 0;
            HierarchicalNameId currentNode = parent;

            while (true)
            {
                if (componentIndex == components.Length)
                {
                    hierarchicalNameId = currentNode;
                    return hierarchicalNameId.IsValid; // don't return true for string.Empty, as there's no actual HierarchicalNameId for it
                }

                var findResult = m_nodeMap.GetOrAddItem(new NodePendingItem(m_equalityComparer, currentNode, components[componentIndex]), allowAdd: false);

                if (!findResult.IsFound)
                {
                    hierarchicalNameId = HierarchicalNameId.Invalid;
                    return false;
                }

                currentNode = GetIdFromIndex(findResult.Index);
                componentIndex++;
            }
        }

        /// <summary>
        /// Looks for a name in the table and returns its id if found.
        /// </summary>
        public bool TryGetName(HierarchicalNameId parent, StringId component, out HierarchicalNameId hierarchicalNameId)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(component.IsValid);
            Contract.Ensures(Contract.Result<bool>() == Contract.ValueAtReturn(out hierarchicalNameId).IsValid);

            var findResult = m_nodeMap.GetOrAddItem(new NodePendingItem(m_equalityComparer, parent, component), allowAdd: false);

            if (!findResult.IsFound)
            {
                hierarchicalNameId = HierarchicalNameId.Invalid;
                return false;
            }

            hierarchicalNameId = GetIdFromIndex(findResult.Index);
            return true;
        }

        /// <summary>
        /// Determines whether a name is within another name hierarchy.
        /// </summary>
        /// <remarks>
        /// This method is thread-safe without the need for any locking.
        /// </remarks>
        [Pure]
        public bool IsWithin(HierarchicalNameId potentialContainer, HierarchicalNameId value)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(potentialContainer.IsValid);
            Contract.Requires(value.IsValid);

            // Parent paths must be added before child paths. Therefore we can bail out early if that isn't true
            // Need to use HierarchicalNameId.Index rather than HierarchicalNameId.Value
            // because HierarchicalNameId.Value has top bits set to debug tag which can make value negative
            var potentialContainerIndex = potentialContainer.Index;
            if (potentialContainerIndex > value.Index)
            {
                return false;
            }

            for (HierarchicalNameId currentNode = value;
                currentNode != s_nullNode;
                currentNode = GetNode(currentNode).GetContainer(DebugTag))
            {
                if (currentNode == potentialContainer)
                {
                    return true;
                }

                if (potentialContainerIndex > currentNode.Index)
                {
                    return false;
                }
            }

            return false;
        }

        /// <summary>
        /// Returns the container of the given name.
        /// </summary>
        /// <remarks>
        /// If the given name is a root, this method returns HierarchicalNameId.Invalid
        /// This method is thread-safe without the need for any locking.
        /// </remarks>
        /// <param name="name">The name for which to retrieve the container.</param>
        /// <returns>The id of the container of HierarchicalNameId.Invalid.</returns>
        public HierarchicalNameId GetContainer(HierarchicalNameId name)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(name.IsValid);

            // note that we don't need to acquire any locks in here
            Node node = GetNode(name);
            return node.GetContainer(DebugTag);
        }

        /// <summary>
        /// Gets the parent index of a path using its index.
        /// </summary>
        public int GetContainerIndex(int index)
        {
            Contract.Requires(index >= 0);
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");

            return m_nodes[index].ContainerIndex;
        }

        /// <summary>
        /// Visits <paramref name="leaf"/> and each of its parents in the hierarchy in turn. Each visited name
        /// that has all of <paramref name="flagsFilter"/> set is returned.
        /// </summary>
        public ContainerEnumerator EnumerateHierarchyBottomUp(HierarchicalNameId leaf, NameFlags flagsFilter = NameFlags.None)
        {
            try
            {
                Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
                Contract.Requires(leaf.IsValid);

                return new ContainerEnumerator(this, leaf, flagsFilter);
            }
            catch (Exception ex) when (ExceptionUtilities.HandleUnexpectedException(ex))
            {
                // Unreachable. Exception filter handles the logic.
                throw Contract.AssertFailure("Unreachable code");
            }
        }

        /// <summary>
        /// Visits all descendants of <paramref name="container"/>. A valid container ID must be provided
        /// (the null / invalid container does not enumerate the entire hierarchy).
        /// </summary>
        public RecursiveChildEnumerator EnumerateHierarchyTopDown(HierarchicalNameId container)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(container.IsValid);

            return new RecursiveChildEnumerator(this, container);
        }

        /// <summary>
        /// Visits immediate children of <paramref name="container"/>. A valid container ID must be provided
        /// (the null / invalid container does not enumerate top-level names).
        /// </summary>
        public ImmediateChildEnumerator EnumerateImmediateChildren(HierarchicalNameId container)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(container.IsValid);

            return new ImmediateChildEnumerator(this, container);
        }

        /// <summary>
        /// Checks if <paramref name="container"/> has a child.
        /// </summary>
        public bool HasChild(HierarchicalNameId container)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(container.IsValid);

            return GetNode(container).FirstChild.IsValid;
        }

        /// <summary>
        /// Add or find a name in the table.
        /// </summary>
        /// <param name="container">
        /// The container for the components or HierarchicalNameId.Invalid to add the components as a root name.
        /// </param>
        /// <param name="components">Components to add to the container.</param>
        /// <returns>HierarchicalNameId of the name just added or found.</returns>
        public HierarchicalNameId AddComponents(HierarchicalNameId container, params StringId[] components)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(components != null);
            Contract.RequiresForAll(components, id => id.IsValid);

            for (int i = 0; i < components.Length; i++)
            {
                container = AddComponent(container, components[i]);
            }

            return container;
        }

        private readonly struct NodeComparer : IEqualityComparer<Node>
        {
            private readonly IEqualityComparer<StringId> m_comparer;

            public NodeComparer(IEqualityComparer<StringId> comparer)
            {
                m_comparer = comparer;
            }

            public bool Equals(Node x, Node y)
            {
                return Equals(m_comparer, x, y);
            }

            public static bool Equals(IEqualityComparer<StringId> comparer, Node x, Node y)
            {
                // Only allow comparisons between valid nodes. Otherwise, return false.
                return x.Component.IsValid &&
                       y.Component.IsValid &&
                       x.ContainerIndex == y.ContainerIndex &&
                       comparer.Equals(x.Component, y.Component);
            }

            public int GetHashCode(Node obj)
            {
                return GetHashCode(m_comparer, obj);
            }

            public static int GetHashCode(IEqualityComparer<StringId> comparer, Node node)
            {
                // For invalid HierarchicalNameId (ie Component is invalid) just return 0.
                return !node.Component.IsValid ? 0 : HashCodeHelper.Combine(
                    node.ContainerIndex,
                    comparer.GetHashCode(node.Component));
            }
        }

        /// <summary>
        /// Used to add nodes to the node map directly (during deserialization)
        /// </summary>
        private readonly struct NodePendingItem : IPendingSetItem<Node>
        {
            private readonly Node m_node;
            private readonly IEqualityComparer<StringId> m_comparer;

            public NodePendingItem(IEqualityComparer<StringId> comparer, HierarchicalNameId container, StringId component)
                : this(comparer, new Node(component, container, depth: 0))
            {
            }

            public NodePendingItem(IEqualityComparer<StringId> comparer, Node node)
            {
                m_comparer = comparer;
                m_node = node;
            }

            public int HashCode => NodeComparer.GetHashCode(m_comparer, m_node);

            public bool Equals(Node other)
            {
                return NodeComparer.Equals(m_comparer, m_node, other);
            }

            public Node CreateOrUpdateItem(Node oldItem, bool hasOldItem, out bool remove)
            {
                remove = false;
                return m_node;
            }
        }

        internal HierarchicalNameId GetIdFromIndex(int index)
        {
            // The null node has a special owner-less ID (and we don't allow flags on it). Don't add an owner tag or equality would break.
            return index == 0
                    ? new HierarchicalNameId(0)
                    : new HierarchicalNameId(index | (DebugTag << DebugTagShift));
        }

        private static int GetIndexFromId(HierarchicalNameId id)
        {
            return GetIndexFromValue(id.Value);
        }

        internal static int GetIndexFromValue(int value)
        {
            return value & ~DebugTagShiftedMask;
        }

        /// <summary>
        /// Add or find a component in the table.
        /// </summary>
        /// <param name="container">
        /// The container for the component or HierarchicalNameId.Invalid to indicate the component is attached to the root.
        /// </param>
        /// <param name="component">Components to add to the container.</param>
        /// <returns>HierarchicalNameId of the component just added or found.</returns>
        public HierarchicalNameId AddComponent(HierarchicalNameId container, StringId component)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(component.IsValid);

            var getOrAddResult = m_nodeMap.GetOrAddItem(new NodePendingItem(m_equalityComparer, container, component), allowAdd: false);

            if (getOrAddResult.IsFound)
            {
                // TODO: The returned ID might not be linked into the containers list properly yet.
                return GetIdFromIndex(getOrAddResult.Index);
            }

            Contract.Assert(!m_isSerializationInProgress, "HierarchicalNameTable is being serialized. No new entry can be added.");

            var containerIndex = GetIndexFromId(container);
            m_nodes.GetEntryBuffer(containerIndex, out int containerBufferIndex, out Node[] containerBuffer);

            // We only need to look at the container's depth once (to know the depth of the new node);
            // depth never changes, whereas FirstChild (below) does.
            int containerDepth = containerBuffer[containerBufferIndex].Depth;

            // Now we can add a real item (it has a proper depth set, now that we looked at its container).
            getOrAddResult = m_nodeMap.GetOrAddItem(new NodePendingItem(m_equalityComparer, new Node(component, container, containerDepth + 1)), allowAdd: true);

            // But maybe we lost a race, and someone else added the real item already.
            if (getOrAddResult.IsFound)
            {
                // TODO: The returned ID might not be linked into the containers list properly yet.
                return GetIdFromIndex(getOrAddResult.Index);
            }

            // getOrAddResult.Item is the node that was added.
            Node newChildNode = getOrAddResult.Item;

            // A real node has been added (by this thread).
            // We now need to link it into the container's child list (FirstChild and NextSibling pointers).
            HierarchicalNameId newChildId = GetIdFromIndex(getOrAddResult.Index);

            HierarchicalNameId nextSibling;
            do
            {
                var containerNode = containerBuffer[containerBufferIndex];
                nextSibling = containerNode.FirstChild;
                var updatedChildNode = newChildNode.WithSibling(nextSibling);
                m_nodes[getOrAddResult.Index] = updatedChildNode;
            }
            while (!containerBuffer[containerBufferIndex].InterlockedSetFirstChild(newChildId, nextSibling));

            // all done
            Contract.Assert(newChildId.IsValid);
            return newChildId;
        }

        /// <summary>
        /// Attempts to set or clear one or more flags for the given name. The return value indicates if all flags were newly set or newly cleared.
        /// </summary>
        /// <remarks>
        /// The motivation for embedded flags on names is to speed up queries / searches in the table where names match sparsely.
        /// (without flags, a caller would still be free to have a set of HierarchicalNameIds and query that set for each name when e.g.
        /// walking from a leaf name to the root; but that is wasteful assuming very sparse matches).
        /// However, we try to satisfy the following constraints with this design:
        /// - The path table may be shared globally and live for process lifetime.
        /// - Multiple (unbounded) unrelated consumers can use flags without concern of interference.
        /// Consequently, we do not provide a reservation scheme for a particular consumer to have its own unique flag; instead, consumers
        /// must keep state to know if a set flags is actually meaningful (i.e., this implementation has 'false positives' from a single
        /// consumer's perspective). See <see cref="FlaggedHierarchicalNameSet"/>.
        /// </remarks>
        public bool SetFlags(HierarchicalNameId name, NameFlags flags, bool clear = false)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(name.IsValid);
            Contract.Requires(flags != NameFlags.None);

            Contract.Assert((int)flags > 0 && (int)flags <= DebugTagValueMask);

            int shiftedFlags = (int)flags << DebugTagShift;

            int nodeIndex = GetIndexFromId(name);
            m_nodes.GetEntryBuffer(nodeIndex, out int index, out Node[] buffer);

            int originalContainerAndFlags;
            int newContainerAndFlags;
            do
            {
                originalContainerAndFlags = buffer[index].ContainerAndFlags;

                newContainerAndFlags = clear
                    ? originalContainerAndFlags & ~shiftedFlags
                    : originalContainerAndFlags | shiftedFlags;

                if (newContainerAndFlags == originalContainerAndFlags)
                {
                    // The value may keep changing out from under us, but we have no semantic change to make to it anymore (all flags set already).
                    return false;
                }
            }
            while (Interlocked.CompareExchange(
                ref buffer[index].ContainerAndFlags,
                newContainerAndFlags,
                comparand: originalContainerAndFlags) != originalContainerAndFlags);

            // At this point originalContainerAndFlags gives the flags that we just replaced;
            // from this we can determine if we set or cleared all flags (vs. some or none).
            var originalFlags = Node.ComputeFlagsFromContainerAndFlags(originalContainerAndFlags);

            return clear ? (originalFlags & flags) == flags : (originalFlags & flags) == 0;
        }

        /// <summary>
        /// Retrieves flags (as set with <see cref="SetExtendedFlags"/>) for the given name
        /// </summary>
        /// <remarks>
        /// If the given name is a root, this method returns HierarchicalNameId.Invalid
        /// This method is thread-safe without the need for any locking.
        /// </remarks>
        public ExtendedNameFlags GetExtendedFlags(HierarchicalNameId name)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(name.IsValid);

            return GetNode(name).ExtendedFlags;
        }

        /// <summary>
        /// Attempts to set or clear one or more extended flags for the given name. The return value indicates if all extended flags were newly set or newly cleared.
        /// </summary>
        public bool SetExtendedFlags(HierarchicalNameId name, ExtendedNameFlags flags, bool clear = false)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(name.IsValid);
            Contract.Requires(flags != ExtendedNameFlags.None);

            Contract.Assert((int)flags > 0 && (int)flags <= DebugTagValueMask);

            int shiftedFlags = (int)flags << DebugTagShift;

            int nodeIndex = GetIndexFromId(name);
            m_nodes.GetEntryBuffer(nodeIndex, out int index, out Node[] buffer);

            int originalDepthAndExtendedFlags;
            int newDepthAndExtendedFlags;
            do
            {
                originalDepthAndExtendedFlags = buffer[index].DepthAndExtendedFlags;

                newDepthAndExtendedFlags = clear
                    ? originalDepthAndExtendedFlags & ~shiftedFlags
                    : originalDepthAndExtendedFlags | shiftedFlags;

                if (newDepthAndExtendedFlags == originalDepthAndExtendedFlags)
                {
                    // The value may keep changing out from under us, but we have no semantic change to make to it anymore (all flags set already).
                    return false;
                }
            }
            while (Interlocked.CompareExchange(
                ref buffer[index].DepthAndExtendedFlags,
                newDepthAndExtendedFlags,
                comparand: originalDepthAndExtendedFlags) != originalDepthAndExtendedFlags);

            // At this point originalContainerAndFlags gives the flags that we just replaced;
            // from this we can determine if we set or cleared all flags (vs. some or none).
            var originalFlags = Node.ComputeExtendedFlagsFromDepthAndFlags(originalDepthAndExtendedFlags);

            return clear ? (originalFlags & flags) == flags : (originalFlags & flags) == 0;
        }

        /// <summary>
        /// Retrieves flags (as set with <see cref="SetFlags"/>) and container for the given name
        /// </summary>
        /// <remarks>
        /// If the given name is a root, this method returns HierarchicalNameId.Invalid
        /// This method is thread-safe without the need for any locking.
        /// </remarks>
        public (HierarchicalNameId container, NameFlags flags) GetContainerAndFlags(HierarchicalNameId name)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(name.IsValid);

            Node node = GetNode(name);
            HierarchicalNameId container = node.GetContainer(DebugTag);
            return (container, node.Flags);
        }

        private Node GetNode(HierarchicalNameId name)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(name.IsValid);
            Contract.Ensures(Contract.Result<Node>().Component.IsValid);

            int index = GetIndexFromId(name);
            return m_nodes[index];
        }

        /// <summary>
        /// Given a root name and a possible descendant name (which can be <paramref name="root" /> itself if root is valid), returns a
        /// non-empty string that represents traversal between the two name.
        /// </summary>
        /// <remarks>
        /// If there is no such name since the given <paramref name="name" /> is not a descendant of
        /// <paramref name="root" />, false is returned.
        /// <example>
        /// For root=C:\src\ and possibleDescendant=C:\src\bin\somefile, this function returns true with the path string
        /// bin\somefile.
        /// For root=C:\src\ and possibleDescendant=C:\bin\, this function returns false.
        /// </example>
        /// Note that if the <paramref name="root" /> is the invalid name (<see cref="HierarchicalNameId.Invalid" />), 'true' will always be
        /// returned since all names are a descendant of it.
        /// This method is thread-safe without the need for any locking.
        /// </remarks>
        public bool TryExpandNameRelativeToAnother(HierarchicalNameId root, HierarchicalNameId name, out string result, NameExpander expander = null, char separator = char.MinValue)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(name.IsValid);
            Contract.Ensures(Contract.Result<bool>() == (Contract.ValueAtReturn(out result) != null));
            Contract.Ensures(
                name.IsValid || Contract.Result<bool>(),
                "TryExpandNameRelativeToAnother should always succeed when the limit is the null path.");

            if (TryExpandNameRelativeToAnother(root, name, out char[] chars, expander, separator))
            {
                if (chars.Length == 0)
                {
                    result = string.Empty;
                    return true;
                }

                result = new string(chars);
                return true;
            }

            result = null;
            return false;
        }

        /// <summary>
        /// Given a root name and a possible descendant name, which can be <paramref name="root" />
        /// itself if root is valid, adds a non-empty string that represents traversal between the
        /// two names into a <paramref name="result"/>.
        /// </summary>
        public bool TryExpandNameRelativeToAnother(HierarchicalNameId root, HierarchicalNameId name, StringBuilder result, NameExpander expander = null, char separator = char.MinValue)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(name.IsValid);
            Contract.Ensures(Contract.Result<bool>() == (Contract.ValueAtReturn(out result) != null));
            Contract.Ensures(
                name.IsValid || Contract.Result<bool>(),
                "TryExpandNameRelativeToAnother should always succeed when the limit is the null path.");

            if (TryExpandNameRelativeToAnother(root, name, out char[] chars, expander, separator))
            {
                result.Append(chars);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Given a root name and a possible descendant name, which can be <paramref name="root" />
        /// itself if root is valid, returns a non-empty string that represents traversal between
        /// the two names.
        /// </summary>
        /// <remarks>
        /// If there is no such name since the given <paramref name="name" /> is not a descendant of
        /// <paramref name="root" />, false is returned.
        /// <example>
        /// For root=C:\src\ and possibleDescendant=C:\src\bin\someFile, this function returns true with the path string
        /// bin\someFile.
        /// For root=C:\src\ and possibleDescendant=C:\bin\, this function returns false.
        /// </example>
        /// Note that if the <paramref name="root" /> is the invalid name (<see cref="HierarchicalNameId.Invalid" />), 'true' will always be
        /// returned since all names are a descendant of it.
        /// This method is thread-safe without the need for any locking.
        /// </remarks>
        public bool TryExpandNameRelativeToAnother(HierarchicalNameId root, HierarchicalNameId name, out char[] result, NameExpander expander = null, char separator = char.MinValue)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(name.IsValid);
            Contract.Ensures(Contract.Result<bool>() == (Contract.ValueAtReturn(out result) != null));
            Contract.Ensures(
                name.IsValid || Contract.Result<bool>(),
                "TryExpandNameRelativeToAnother should always succeed when the limit is the null path.");

            expander = expander ?? DefaultExpander;

            // note that we don't need to acquire any locks in here

            // count the length of the resultant path
            HierarchicalNameId currentNode = name;
            int length = -1;
            bool continueExpansion = true;
            while (currentNode != root && continueExpansion)
            {
                Node node = GetNode(currentNode);
                length += expander.GetLength(currentNode, m_stringTable, node.Component, node.Flags, out continueExpansion) + 1;

                currentNode = node.GetContainer(DebugTag);

                // Handle the case where only the UnixPathRootSentinel is inside the node and make room for one symbol (the slash)
                if (OperatingSystemHelper.IsUnixOS && length == 0 && m_separator == Path.DirectorySeparatorChar)
                {
                    length++;
                }

                // Maybe we ran out of nodes before finding a (non-null) prefix.
                if (root != s_nullNode && currentNode == s_nullNode)
                {
                    result = null;
                    return false;
                }
            }

            if (length == -1)
            {
                result = CollectionUtilities.EmptyArray<char>();
                return true;
            }

            Contract.Assume(length >= 0);

            if (separator == char.MinValue)
            {
                separator = m_separator;
            }

            // now copy the path components into a char buffer
            currentNode = name;
            var buffer = new char[length];
            int bufferIndex = buffer.Length;
            while (true)
            {
                Node node = GetNode(currentNode);
                StringId component = node.Component;

                length = expander.CopyString(currentNode, m_stringTable, component, node.Flags, buffer, bufferIndex);
                Contract.Assume(length <= bufferIndex);
                bufferIndex -= length;

                if (bufferIndex == 0)
                {
                    if (buffer.Length > (bufferIndex + 1) && buffer[bufferIndex] == Path.DirectorySeparatorChar && buffer[bufferIndex + 1] == Path.DirectorySeparatorChar)
                    {
                        // UNC paths need to be handled differently because they are stored differently.
                        buffer[bufferIndex] = buffer[bufferIndex + 1] = separator;
                    }

                    break;
                }

                buffer[--bufferIndex] = separator;
                currentNode = node.GetContainer(DebugTag);

                // We check if it is the Unix root-case and just break as we adjusted the buffer length previously and end up with
                // the Path.VolumeSeparatorChar written out - current node is invalid at this point so no use in iterating any further
                if (OperatingSystemHelper.IsUnixOS && !currentNode.IsValid)
                {
                    break;
                }
            }

            result = buffer;
            return true;
        }

        /// <summary>
        /// Returns a string representing the given id.
        /// </summary>
        /// <remarks>
        /// This method is thread-safe without the need for any locking.
        /// </remarks>
        public string ExpandName(HierarchicalNameId name, NameExpander expander = null, char separator = char.MinValue)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(name.IsValid);
            Contract.Ensures(Contract.Result<string>() != null);

            expander = expander ?? DefaultExpander;

            // see if we've got this expansion cached
            if (expander.ExpansionCacheChar == separator && expander.ExpansionCache.TryGetValue(name, out string result))
            {
                return result;
            }

            bool succeeded = TryExpandNameRelativeToAnother(s_nullNode, name, out result, expander, separator);
            Contract.Assume(succeeded);

            if (expander.ExpansionCacheChar == separator)
            {
                // add to the cache
                expander.ExpansionCache.AddItem(name, result);
            }

            return result;
        }
        
        /// <summary>
        /// Returns a character array representing the given id.
        /// </summary>
        /// <remarks>
        /// This method is thread-safe without the need for any locking.
        /// </remarks>
        public char[] ExpandNameToCharArray(HierarchicalNameId name)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(name.IsValid);

            bool succeeded = TryExpandNameRelativeToAnother(s_nullNode, name, out char[] result);
            Contract.Assume(succeeded);

            return result;
        }
        
        /// <summary>
        /// Returns a string representing the final component of the given name.
        /// </summary>
        /// <remarks>
        /// This method is thread-safe without the need for any locking.
        /// </remarks>
        public string ExpandFinalComponent(HierarchicalNameId name)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(name.IsValid);
            Contract.Ensures(Contract.Result<string>() != null);

            Node node = GetNode(name);
            int length = m_stringTable.GetLength(node.Component);

            // The root node is modeled with UnixPathRootSentinel for Unix based paths, we can just return the volume separator
            // char as string if we detect it while running on Unix systems
            if (OperatingSystemHelper.IsUnixOS && length == 0)
            {
                return Path.VolumeSeparatorChar.ToString();
            }

            var buffer = new char[length];
            m_stringTable.CopyString(node.Component, buffer, 0);
            return new string(buffer);
        }

        /// <summary>
        /// Returns the string id of the final component of the given name.
        /// </summary>
        /// <remarks>
        /// This method is thread-safe without the need for any locking.
        /// </remarks>
        public StringId GetFinalComponent(HierarchicalNameId name)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
            Contract.Requires(name.IsValid);
            Contract.Ensures(Contract.Result<StringId>().IsValid);

            Node node = GetNode(name);
            return node.Component;
        }

        /// <summary>
        /// Releases temporary resources and prevents the table from mutating from this point forward.
        /// </summary>
        public void Freeze()
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");

            m_frozenNodeCount = m_nodeMap.Count;

            // prevents mutation
            m_nodeMap = null;
            m_equalityComparer = null;
        }

        /// <summary>
        /// Gets the string table that holds the text for this hierarchical name table.
        /// </summary>
        public StringTable StringTable
        {
            get
            {
                Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
                Contract.Ensures(Contract.Result<StringTable>() != null);
                return m_stringTable;
            }
        }

        /// <summary>
        /// Gets approximately how much memory this abstraction is consuming.
        /// </summary>
        /// <remarks>
        /// This assumes the table has been frozen as the data that gets freed when freezing is not counted here.
        /// </remarks>
        public int SizeInBytes
        {
            get
            {
                Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");
                int nodeBufferSize = m_nodes.Capacity * 16;
                int nodeBufferArrayOverhead = m_nodes.NumberOfBuffers * 12;

                return nodeBufferSize + nodeBufferArrayOverhead;
            }
        }

        /// <summary>
        /// Gets the number of names held in this table.
        /// </summary>
        public int Count => m_nodeMap?.Count ?? m_frozenNodeCount;

        /// <summary>
        /// Gets the debug tag that is ORed into each id returned by this table.
        /// </summary>
        protected int DebugTag { get; }

        /// <summary>
        /// Gets the number of cache misses on the name expansion cache.
        /// </summary>
        public long CacheHits => DefaultExpander.ExpansionCache.Hits;

        /// <summary>
        /// Gets the number of cache hits on the name expansion cache.
        /// </summary>
        public long CacheMisses => DefaultExpander.ExpansionCache.Misses;

        /// <summary>
        /// When possible, returns the HierarchicalTable which owns the given id. Otherwise, returns null.
        /// </summary>
        /// <remarks>
        /// Note that this method uses limited bookkeeping for the single purpose of facilitating easier
        /// debugging of code that uses HierarchicalNameIds. It should not be used for runtime logic.
        /// </remarks>
        [ExcludeFromCodeCoverage]
        internal static HierarchicalNameTable DebugTryGetTableForId(HierarchicalNameId id)
        {
            int debugTag = (id.Value >> DebugTagShift) & DebugTagValueMask;

            // We use the max table ID as a catch-all (there are no IDs to use after it),
            // so we can't actually retrieve a single HierarchicalNameId from it.
            if (debugTag == DebugTagValueMask)
            {
                return null;
            }

            Contract.Assert(debugTag < DebugTagValueMask && debugTag <= s_tableInstancesForDebug.Length, I($"Invalid debugTag: {debugTag}."));

            WeakReference<HierarchicalNameTable> instance = Volatile.Read(ref s_tableInstancesForDebug[debugTag]);
            return instance.TryGetTarget(out HierarchicalNameTable owningTable) ? owningTable : null;
        }

        #region Serialization

        /// <summary>
        /// Serializes the name table.
        /// </summary>
        /// <remarks>
        /// Not thread safe. The caller should ensure there are no concurrent accesses to the structure while serializing.
        /// </remarks>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(IsValid, "This Table has been invalidated. Likely you should be using a newly created one.");

            m_isSerializationInProgress = true;
            // Actual serialization done in a static method with a snapshot of internal state to ensure nothing is
            // written that isn't accessible during deserialization
            try
            {
                Serialize(
                    writer,
                    new SerializedState()
                    {
                        DebugTag = DebugTag,
                        IgnoreCase = m_ignoreCase,
                        NodeCount = Count,
                        NodeMap = m_nodeMap,
                    });
            }
            finally
            {
                m_isSerializationInProgress = false;
            }
        }

        /// <summary>
        /// Serializes
        /// </summary>
        protected static void Serialize(BuildXLWriter writer, SerializedState state)
        {
            Contract.Requires(writer != null);
            Contract.Requires(state != null);

            writer.Write(state.DebugTag);
            writer.Write(state.IgnoreCase);
            writer.Write(state.NodeCount);
            state.NodeMap.Serialize(writer, node => node.Serialize(writer));
        }

        /// <summary>
        /// State serialized and deserialized
        /// </summary>
        protected sealed class SerializedState
        {
            /// <summary>
            /// DebugTag for the table
            /// </summary>
            public int DebugTag;

            /// <summary>
            /// NodeIndex
            /// </summary>
            public int NodeCount;

            /// <summary>
            /// Whether to ignore case
            /// </summary>
            public bool IgnoreCase;

            /// <summary>
            /// NodeBuffers
            /// </summary>
            public ConcurrentBigSet<Node> NodeMap;

            /// <summary>
            /// Checks if this instance is consistent.
            /// </summary>
            public bool Validate(StringTable stringTable)
            {
                return NodeMap.Validate(new NodeComparer(HierarchicalNameTable.CreateEqualityComparer(stringTable, IgnoreCase)));
            }
        }

        /// <summary>
        /// Reads the SerializationState
        /// </summary>
        protected static async Task<SerializedState> ReadSerializationStateAsync(BuildXLReader reader, Task<StringTable> stringTableTask)
        {
            Contract.Requires(reader != null);
            Contract.Requires(stringTableTask != null);

            SerializedState state = new SerializedState();
            state.DebugTag = reader.ReadInt32();
            state.IgnoreCase = reader.ReadBoolean();
            state.NodeCount = reader.ReadInt32();
            state.NodeMap = ConcurrentBigSet<Node>.Deserialize(reader, () => Node.Deserialize(reader));

            var stringTable = await stringTableTask;
            if (stringTable != null)
            {
                // The CLR reserves the right to change the hash function of strings,
                // so we check if that didn't happen.
                if (state.Validate(stringTable))
                {
                    return state;
                }
            }

            return null;
        }

        #endregion
    }
}

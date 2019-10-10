// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Represents an absolute dotted identifier.
    /// </summary>
    [DebuggerTypeProxy(typeof(AbsoluteIdDebuggerView))]
    [DebuggerDisplay("{ToDebuggerDisplay(),nq}")]

    // Note that [DebuggerDisplay] applies to this type, not the proxy. Applying it to the proxy doesn't work.
    public readonly struct FullSymbol : IEquatable<FullSymbol>, ISymbol
    {
        /// <summary>
        /// An invalid identifier.
        /// </summary>
        public static readonly FullSymbol Invalid = new FullSymbol(HierarchicalNameId.Invalid);

        /// <summary>
        /// Identifier of this identifier as understood by the owning identifier table.
        /// </summary>
        /// <remarks>
        /// AbsoluteIds are a simple mapping of a HierarchicalNameId.
        /// </remarks>
        public readonly HierarchicalNameId Value;

        /// <summary>
        /// The alternate separator character used when expanding the full symbol
        /// </summary>
        public readonly char AlternateSeparator;

        /// <summary>
        /// Creates an absolute identifier for some underlying HierchicalNameId value.
        /// </summary>
        /// <remarks>
        /// Since the value must have some meaning to a identifier table, this constructor should primarily be called by SymbolTables.
        /// The only other reasonable usage would be for temporary serialization (e.g. to a child process).
        /// </remarks>
        public FullSymbol(HierarchicalNameId value, char separator = default)
        {
            Value = value;
            AlternateSeparator = separator;
        }

        /// <summary>
        /// Creates an absolute identifier for some underlying integer value.
        /// </summary>
        /// <remarks>
        /// Since the value must have some meaning to a identifier table, this constructor should primarily be called by SymbolTables.
        /// The only other reasonable usage would be for temporary serialization (e.g. to a child process).
        /// </remarks>
        public FullSymbol(int value, char separator = default)
        {
            Value = new HierarchicalNameId(value);
            AlternateSeparator = separator;
        }

        /// <summary>
        /// Try to create an absolute identifier from a string.
        /// </summary>
        /// <returns>Return the parser result indicating success, or what was wrong with the parsing.</returns>
        public static ParseResult TryCreate(SymbolTable table, StringSegment fullSymbol, out FullSymbol result, out int characterWithError)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Ensures((Contract.Result<ParseResult>() == ParseResult.Success) == Contract.ValueAtReturn(out result).IsValid);

            StringId[] components;
            ParseResult parseRes = TryGetComponents(table, fullSymbol, out components, out characterWithError);
            if (parseRes == ParseResult.Success)
            {
                if (components.Length == 0)
                {
                    result = Invalid;
                    characterWithError = 0;
                    return ParseResult.FailureDueToInvalidCharacter;
                }

                result = AddIdentifierComponents(table, Invalid, components);
            }
            else
            {
                result = Invalid;
            }

            return parseRes;
        }

        /// <summary>
        /// Tries to break down an absolute identifier string into its constituent parts.
        /// </summary>
        private static ParseResult TryGetComponents(SymbolTable table, StringSegment fullSymbol, out StringId[] components, out int characterWithError)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Ensures((Contract.Result<ParseResult>() == ParseResult.Success) == (Contract.ValueAtReturn(out components) != null));

            PartialSymbol relIdentifier;
            PartialSymbol.ParseResult parseResult = PartialSymbol.TryCreate(table.StringTable, fullSymbol, out relIdentifier, out characterWithError);
            if (parseResult == PartialSymbol.ParseResult.Success)
            {
                components = relIdentifier.Components;
                characterWithError = -1;
                return ParseResult.Success;
            }

            components = null;

            return parseResult == PartialSymbol.ParseResult.LeadingOrTrailingDot
                ? ParseResult.LeadingOrTrailingDot
                : ParseResult.FailureDueToInvalidCharacter;
        }

        /// <summary>
        /// Create a full symbol from an atomic symbol.
        /// </summary>
        public static FullSymbol Create(SymbolTable table, SymbolAtom atom)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Requires(atom.IsValid);
            Contract.Ensures(Contract.Result<FullSymbol>().IsValid);

            return AddIdentifierComponent(table, Invalid, atom.StringId);
        }

        /// <summary>
        /// Private helper method
        /// </summary>
        /// <returns>FullSymbol of the identifier just added.</returns>
        private static FullSymbol AddIdentifierComponents(SymbolTable table, FullSymbol parentIdentifier, params StringId[] components)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Requires(components != null, "components != null");
            Contract.RequiresForAll(components, id => id.IsValid);

            return new FullSymbol(table.AddComponents(parentIdentifier.Value, components));
        }

        /// <summary>
        /// Private helper method
        /// </summary>
        /// <returns>FullSymbol of the identifier just added.</returns>
        private static FullSymbol AddIdentifierComponent(SymbolTable table, FullSymbol parentIdentifier, StringId component)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Requires(component.IsValid);

            return new FullSymbol(table.AddComponent(parentIdentifier.Value, component));
        }

        /// <summary>
        /// Creates an FullSymbol from a string and abandons if the identifier is invalid.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded identifiers, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public static FullSymbol Create(SymbolTable table, StringSegment fullSymbol)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Ensures(Contract.Result<FullSymbol>().IsValid);

            FullSymbol result;
            ParseResult parseResult = TryCreate(table, fullSymbol, out result, out _);

            if (parseResult != ParseResult.Success)
            {
                Contract.Assume(false, I($"Failed to create a full symbol from the segment '{fullSymbol.ToString()}' - ParseResult {parseResult}"));
            }

            return result;
        }

        /// <summary>
        /// Adds a identifier that might be relative, abandons if the identifier is invalid.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded identifiers, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        /// <param name="table">The identifier table to use.</param>
        /// <param name="relativeId">The identifier to add. If absolute this will be the identifier returned, otherwise the relative identifier is tacked onto the end of the base identifier.</param>
        /// <returns>Final resulting absolute identifier.</returns>
        public FullSymbol Combine(SymbolTable table, StringSegment relativeId)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Ensures(Contract.Result<FullSymbol>() != FullSymbol.Invalid);
            PartialSymbol relIdentifier;
            PartialSymbol.ParseResult parseResult = PartialSymbol.TryCreate(table.StringTable, relativeId, out relIdentifier, out _);
            if (parseResult != PartialSymbol.ParseResult.Success)
            {
                Contract.Assume(false, I($"Failed to create a full symbol from the segment '{relativeId.ToString()}'"));
            }

            return AddIdentifierComponents(table, this, relIdentifier.Components);
        }

        /// <summary>
        /// Looks for a identifier in the table and returns it if found.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        [Pure]
        public static bool TryGet(SymbolTable table, StringSegment fullSymbol, out FullSymbol result)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Ensures(Contract.Result<bool>() == (Contract.ValueAtReturn(out result) != FullSymbol.Invalid));

            StringId[] components;
            int characterWithError;
            ParseResult parseRes = TryGetComponents(table, fullSymbol, out components, out characterWithError);
            if (parseRes == ParseResult.Success)
            {
                HierarchicalNameId nameId;
                bool b = table.TryGetName(components, out nameId);
                result = b ? new FullSymbol(nameId) : Invalid;

                return b;
            }

            result = Invalid;
            return false;
        }

        /// <summary>
        /// Looks for a identifier in the table and returns it if found.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        [Pure]
        public bool TryGet(SymbolTable table, SymbolAtom component, out FullSymbol result)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Requires(component.IsValid);
            Contract.Ensures(Contract.Result<bool>() == (Contract.ValueAtReturn(out result) != FullSymbol.Invalid));

            HierarchicalNameId child;
            var found = table.TryGetName(Value, component.StringId, out child);

            result = new FullSymbol(child);
            return found;
        }

        /// <summary>
        /// Looks for a identifier relative to the current identifier in the table and returns it if found.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        [Pure]
        public bool TryGet(SymbolTable table, PartialSymbol relativeId, out FullSymbol result)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Ensures(Contract.Result<bool>() == (Contract.ValueAtReturn(out result) != FullSymbol.Invalid));

            if (relativeId.IsEmpty)
            {
                result = this;
                return true;
            }

            HierarchicalNameId child;
            var found = table.TryGetName(Value, relativeId.Components, out child);

            result = new FullSymbol(child);
            return found;
        }

        /// <summary>
        /// Given a possible descendant identifier (which can be 'this' itself), returns a
        /// (possibly empty) relative identifier that represents traversal between the two identifiers (without any .. backtracking).
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        [Pure]
        public bool TryGetRelative(SymbolTable table, FullSymbol proposedRelativeId, out PartialSymbol result)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Requires(proposedRelativeId.IsValid);

            string str;
            bool b = table.TryExpandNameRelativeToAnother(Value, proposedRelativeId.Value, out str);

            result = b ? PartialSymbol.Create(table.StringTable, str) : PartialSymbol.Invalid;
            return b;
        }

        /// <summary>
        /// Extends an absolute identifier with new identifier components.
        /// </summary>
        public FullSymbol Combine(SymbolTable table, PartialSymbol identifier)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Requires(identifier.IsValid);
            Contract.Ensures(Contract.Result<FullSymbol>().IsValid);

            return AddIdentifierComponents(table, this, identifier.Components);
        }

        /// <summary>
        /// Extends a absolute identifier with a new identifier components
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public FullSymbol Combine(SymbolTable table, SymbolAtom atom)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Requires(atom.IsValid);
            Contract.Ensures(Contract.Result<FullSymbol>().IsValid);

            return AddIdentifierComponent(table, this, atom.StringId);
        }

        /// <summary>
        /// Extends a absolute identifier with new identifier components.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public FullSymbol Combine(SymbolTable table, SymbolAtom atom1, SymbolAtom atom2)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Requires(atom1.IsValid);
            Contract.Requires(atom2.IsValid);
            Contract.Ensures(Contract.Result<FullSymbol>().IsValid);

            FullSymbol r1 = AddIdentifierComponent(table, this, atom1.StringId);
            return AddIdentifierComponent(table, r1, atom2.StringId);
        }

        /// <summary>
        /// Extends an absolute identifier with new identifier components.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public FullSymbol Combine(SymbolTable table, params SymbolAtom[] atoms)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Requires(atoms != null);
            Contract.RequiresForAll(atoms, a => a.IsValid);
            Contract.Ensures(Contract.Result<FullSymbol>().IsValid);

            FullSymbol absIdentifier = this;
            for (int i = 0; i < atoms.Length; i++)
            {
                absIdentifier = AddIdentifierComponent(table, absIdentifier, atoms[i].StringId);
            }

            return absIdentifier;
        }

        /// <summary>
        /// Extends a absolute identifier with an absolute identifier.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public FullSymbol Combine(SymbolTable table, FullSymbol symbol)
        {
            Contract.Requires(table != null, "table != null");
            return !symbol.IsValid ? this : Combine(table, symbol.GetParent(table)).Combine(table, symbol.GetName(table));
        }

        /// <summary>
        /// Concatenates a identifier atom to the end of a absolute identifier.
        /// </summary>
        public FullSymbol Concat(SymbolTable table, SymbolAtom addition)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Requires(IsValid);
            Contract.Requires(addition.IsValid);
            Contract.Ensures(Contract.Result<FullSymbol>().IsValid);

            FullSymbol parent = GetParent(table);
            SymbolAtom newName = GetName(table).Concat(table.StringTable, addition);
            return parent.Combine(table, newName);
        }

        /// <summary>
        /// Removes the last identifier component of this FullSymbol.
        /// </summary>
        /// <remarks>
        /// If the given identifier is a root, this method returns FullSymbol.Invalid
        /// This method is thread-safe without the need for any locking.
        /// </remarks>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public FullSymbol GetParent(SymbolTable table)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Requires(IsValid);

            return new FullSymbol(table.GetContainer(Value));
        }

        /// <summary>
        /// Returns the last component of the FullSymbol.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public SymbolAtom GetName(SymbolTable table)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Requires(IsValid);
            Contract.Ensures(Contract.Result<SymbolAtom>().IsValid);

            return new SymbolAtom(table.GetFinalComponent(Value));
        }

        /// <summary>
        /// Relocates an FullSymbol from one subtree to another.
        /// </summary>
        /// <param name="table">The identifier table to operate against.</param>
        /// <param name="source">The root of the tree to clone.</param>
        /// <param name="destination">The destination directory for the relocation.</param>
        /// <returns>The relocated identifier.</returns>
        public FullSymbol Relocate(
            SymbolTable table,
            FullSymbol source,
            FullSymbol destination)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Requires(IsValid);
            Contract.Requires(source.IsValid);
            Contract.Requires(destination.IsValid);
            Contract.Requires(IsWithin(table, source));

            // figure out how many components from the source item to its containing directory
            int count = 0;
            for (FullSymbol currentNode = this;
                currentNode != Invalid;
                currentNode = currentNode.GetParent(table))
            {
                if (currentNode == source)
                {
                    var components = new StringId[count];

                    // now create the component list for the subtree
                    for (currentNode = this;
                        currentNode != Invalid;
                        currentNode = currentNode.GetParent(table))
                    {
                        if (currentNode == source)
                        {
                            break;
                        }

                        Contract.Assume(count > 0, "count > 0");
                        components[--count] = currentNode.GetName(table).StringId;
                    }

                    // and record the new subtree
                    return AddIdentifierComponents(table, destination, components);
                }

                count++;
            }

            // if we get here, it's because the current identifier is not under 'source' which shouldn't happen given the precondition
            Contract.Assume(false, "Current identifier is not under the 'source'");
            return Invalid;
        }

        /// <summary>
        /// Relocates the name component of an FullSymbol to a destination directory.
        /// </summary>
        /// <param name="table">The identifier table to operate against.</param>
        /// <param name="destination">The destination directory for the relocation.</param>
        /// <returns>The relocated identifier.</returns>
        public FullSymbol Relocate(
            SymbolTable table,
            FullSymbol destination)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Requires(destination.IsValid);
            Contract.Ensures(Contract.Result<FullSymbol>().IsValid);

            return Relocate(table, GetParent(table), destination);
        }

        /// <summary>
        /// Returns true if this file is exactly equal to the given directory (ignoring case),
        /// or if the file lies within the given directory.
        /// </summary>
        /// <remarks>
        /// For example, /// if tree is 'C', and identifier='C.Windows', then the return value would
        /// be true.  But if tree is 'C.Foo', and identifier is 'C.Bar', then the
        /// return value is false.
        /// This method is thread-safe without the need for any locking.
        /// </remarks>
        [Pure]
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public bool IsWithin(SymbolTable table, FullSymbol potentialContainer)
        {
            Contract.Requires(table != null, "table != null");
            Contract.Requires(IsValid);
            Contract.Requires(potentialContainer.IsValid);

            return table.IsWithin(potentialContainer.Value, Value);
        }

        /// <summary>
        /// Determines whether an absolute identifier is valid or not.
        /// </summary>
        [Pure]
        public bool IsValid => this != Invalid;

        /// <summary>
        /// Determines whether a particular character is valid within an absolute identifier.
        /// </summary>
        public static bool IsValidAbsoluteIdChar(char value)
        {
            return PartialSymbol.IsValidRelativeIdChar(value);
        }

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <returns>
        /// true if the current object is equal to the <paramref name="other" /> parameter; otherwise, false.
        /// </returns>
        /// <param name="other">An object to compare with this object.</param>
        /// <filterpriority>2</filterpriority>
        public bool Equals(FullSymbol other)
        {
            return Value == other.Value;
        }

        /// <summary>
        /// Determines whether the specified object is equal to the current object.
        /// </summary>
        /// <returns>
        /// true if the specified object  is equal to the current object; otherwise, false.
        /// </returns>
        /// <param name="obj">The object to compare with the current object. </param>
        /// <filterpriority>2</filterpriority>
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <summary>
        /// Serves as a hash function for a particular type.
        /// </summary>
        /// <remarks>
        /// It is illegal for a file to have both a rewrite count of 0 AND 1 in the graph.
        /// Therefore we will give both the same hash value as there shouldn't be many collisions, only to report errors.
        /// Furthermore we expect the rewrites > 1 to be limited and eliminated over time. We will use the higher-order bits,
        /// One strategy would be to reverse the bits on the rewrite count and bitwise or it with the absolute identifier so collisions
        /// would only occur when there are tons of files or high rewrite counts.
        /// </remarks>
        /// <returns>
        /// A hash code for the current object.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public override int GetHashCode()
        {
            // see remarks on why it is implemented this way.
            return Value.GetHashCode();
        }

        /// <summary>
        /// Indicates whether two object instances are equal.
        /// </summary>
        /// <returns>
        /// true if the values of <paramref name="left" /> and <paramref name="right" /> are equal; otherwise, false.
        /// </returns>
        /// <param name="left">The first object to compare. </param>
        /// <param name="right">The second object to compare. </param>
        /// <filterpriority>3</filterpriority>
        public static bool operator ==(FullSymbol left, FullSymbol right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Indicates whether two objects instances are not equal.
        /// </summary>
        /// <returns>
        /// true if the values of <paramref name="left" /> and <paramref name="right" /> are not equal; otherwise, false.
        /// </returns>
        /// <param name="left">The first object to compare.</param>
        /// <param name="right">The second object to compare.</param>
        /// <filterpriority>3</filterpriority>
        public static bool operator !=(FullSymbol left, FullSymbol right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Returns a string representation of the absolute identifier.
        /// </summary>
        /// <param name="symbolTable">The identifier table used when creating the FullSymbol.</param>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        [Pure]
        public string ToString(SymbolTable symbolTable)
        {
            if (!IsValid)
            {
                return "{Invalid}";
            }

            string result = symbolTable.ExpandName(Value, separator: AlternateSeparator);
            return result;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return this == Invalid ? "{Invalid}" : I($"{{Identifier (id: {Value.Value:x})}}");
        }

        /// <summary>
        /// Convert to DottedIdentifier
        /// </summary>
        public DottedIdentifier ToDottedIdentifier(SymbolTable table, DottedIdentifier tail = null)
        {
            FullSymbol current = this;
            DottedIdentifier dottedIdentifier = tail;
            while (current.IsValid)
            {
                dottedIdentifier = new DottedIdentifier(current.GetName(table), dottedIdentifier);
                current = current.GetParent(table);
            }

            return dottedIdentifier;
        }

        /// <summary>
        /// Returns a string to be displayed as the debugger representation of this value.
        /// This string contains an expanded identifier when possible. See the comments in SymbolTable.cs
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode",
            Justification = "Nothing is private to the debugger.")]
        [ExcludeFromCodeCoverage]
        private string ToDebuggerDisplay()
        {
            if (this == Invalid)
            {
                return ToString();
            }

            SymbolTable owner = HierarchicalNameTable.DebugTryGetTableForId(Value) as SymbolTable;
            return owner == null
                ? "{Unable to expand FullSymbol; this may occur after the allocation of a large number of SymbolTables}"
                : I($"{{Identifier '{ToString(owner)}' (id: {Value.Value:x})}}");
        }

        /// <summary>
        /// Debugger type proxy for FullSymbol. The properties of this type are shown in place of the single integer field of FullSymbol.
        /// </summary>
        [ExcludeFromCodeCoverage]
        private sealed class AbsoluteIdDebuggerView
        {
            /// <summary>
            /// Constructs a debug view from a normal FullSymbol.
            /// </summary>
            /// <remarks>
            /// This constructor is required by the debugger.
            /// Consequently, Invalid AbsoluteIds are allowed.
            /// </remarks>
            public AbsoluteIdDebuggerView(FullSymbol fullSymbol)
            {
                Id = fullSymbol.Value;

                if (fullSymbol == Invalid)
                {
                    OwningSymbolTable = null;
                    Identifier = null;
                }
                else
                {
                    OwningSymbolTable = HierarchicalNameTable.DebugTryGetTableForId(fullSymbol.Value) as SymbolTable;
                    if (OwningSymbolTable != null)
                    {
                        Identifier = fullSymbol.ToString(OwningSymbolTable);
                    }
                }
            }

            /// <summary>
            /// Identifier table which owns this ID and was used to expand it.
            /// </summary>
            /// <remarks>
            /// This may be null if the table could not be found.
            /// </remarks>
            private SymbolTable OwningSymbolTable { get; }

            /// <summary>
            /// Integer ID as relevant in the owning identifier table.
            /// </summary>
            private HierarchicalNameId Id { get; }

            /// <summary>
            /// Expanded identifier according to the owning identifier table.
            /// </summary>
            /// <remarks>
            /// This may be null if the owning identifier table was not found.
            /// </remarks>
            private string Identifier { get; }
        }

        /// <summary>
        /// Explains the identifier errors
        /// </summary>
        public enum ParseResult
        {
            /// <summary>
            /// Successfully parsed
            /// </summary>
            Success = 0,

            /// <summary>
            /// Invalid character.
            /// </summary>
            FailureDueToInvalidCharacter,

            /// <summary>
            /// RelativeId does not allow for '.' and the beginning or end.
            /// </summary>
            LeadingOrTrailingDot,
        }

        #region ISymbol Members

        /// <summary>
        /// Attempts to get the full symbol of the combined symbols. If this value represents a full symbol
        /// it is returned unmodified.
        /// </summary>
        public bool TryGetFullSymbol(SymbolTable symbolTable, FullSymbol root, out FullSymbol fullSymbol)
        {
            fullSymbol = this;
            return true;
        }

        #endregion
    }
}

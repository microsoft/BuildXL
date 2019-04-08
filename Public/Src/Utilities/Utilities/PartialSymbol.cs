// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Text;
using BuildXL.Utilities.Collections;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Represents a partial dotted identifier.
    /// </summary>
    [DebuggerDisplay("{ToDebuggerDisplay(),nq}")]
    public readonly struct PartialSymbol : IEquatable<PartialSymbol>, ISymbol
    {
        /// <summary>
        /// Invalid identifier for uninitialized fields.
        /// </summary>
        public static readonly PartialSymbol Invalid = default(PartialSymbol);

        private readonly StringId[] m_components;

        private PartialSymbol(params StringId[] components)
        {
            Contract.Requires(components != null);
            Contract.RequiresForAll(components, component => component.IsValid);

            m_components = components;
        }

        /// <summary>
        /// Try to create a PartialSymbol from a string.
        /// </summary>
        /// <returns>Return false if the input identifier is not in a valid format.</returns>
        public static bool TryCreate(StringTable table, string partialSymbol, out PartialSymbol result)
        {
            Contract.Requires(table != null);
            Contract.Requires(partialSymbol != null);
            Contract.Ensures(Contract.Result<bool>() == Contract.ValueAtReturn(out result).IsValid);

            return TryCreate(table, (StringSegment)partialSymbol, out result);
        }

        /// <summary>
        /// Try to create a PartialSymbol from a string.
        /// </summary>
        /// <returns>Return false if the input identifier is not in a valid format.</returns>
        public static bool TryCreate<T>(StringTable table, T partialSymbol, out PartialSymbol result)
            where T : struct, ICharSpan<T>
        {
            Contract.Requires(table != null);
            Contract.Ensures(Contract.Result<bool>() == Contract.ValueAtReturn(out result).IsValid);

            int characterWithError;
            ParseResult parseResult = TryCreate(table, partialSymbol, out result, out characterWithError);
            return parseResult == ParseResult.Success;
        }

        /// <summary>
        /// Try to create a PartialSymbol from a string.
        /// </summary>
        /// <returns>Return the parser result indicating success, or what was wrong with the parsing.</returns>
        public static ParseResult TryCreate<T>(StringTable table, T partialSymbol, out PartialSymbol result, out int characterWithError)
            where T : struct, ICharSpan<T>
        {
            Contract.Requires(table != null);
            Contract.Ensures((Contract.Result<ParseResult>() == ParseResult.Success) == Contract.ValueAtReturn(out result).IsValid);

            using (var wrap = Pools.GetStringIdList())
            {
                List<StringId> components = wrap.Instance;

                int index = 0;
                int start = 0;
                int last = partialSymbol.Length - 1;
                while (index < partialSymbol.Length)
                {
                    var ch = partialSymbol[index];

                    // trivial reject of invalid characters
                    if (!SymbolCharacters.IsValidDottedIdentifierChar(ch))
                    {
                        characterWithError = index;
                        result = Invalid;
                        return ParseResult.FailureDueToInvalidCharacter;
                    }

                    if (ch == SymbolCharacters.DottedIdentifierSeparatorChar)
                    {
                        // found a component separator
                        if (index == start || index == last)
                        {
                            characterWithError = index;
                            result = Invalid;
                            return ParseResult.LeadingOrTrailingDot;
                        }
                        else if (index > start)
                        {
                            // make a identifier atom out of [start..index]
                            SymbolAtom atom;
                            int charError;
                            SymbolAtom.ParseResult papr = SymbolAtom.TryCreate(
                                table,
                                partialSymbol.Subsegment(start, index - start),
                                out atom,
                                out charError);

                            if (papr != SymbolAtom.ParseResult.Success)
                            {
                                characterWithError = index + charError;
                                result = Invalid;
                                return ParseResult.FailureDueToInvalidCharacter;
                            }

                            components.Add(atom.StringId);
                        }

                        // skip over the dot
                        index++;
                        start = index;
                        continue;
                    }

                    index++;
                }

                if (index > start)
                {
                    // make a identifier atom out of [start..index]
                    SymbolAtom atom;
                    int charError;
                    SymbolAtom.ParseResult papr = SymbolAtom.TryCreate(
                        table,
                        partialSymbol.Subsegment(start, index - start),
                        out atom,
                        out charError);

                    if (papr != SymbolAtom.ParseResult.Success)
                    {
                        characterWithError = index + charError;
                        result = Invalid;
                        return ParseResult.FailureDueToInvalidCharacter;
                    }

                    components.Add(atom.StringId);
                }

                result = new PartialSymbol(components.ToArray());

                characterWithError = -1;
                return ParseResult.Success;
            }
        }

        /// <summary>
        /// Creates a PartialSymbol from a string and abandons if the string is invalid.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded relative ids, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public static PartialSymbol Create(StringTable table, string partialSymbol)
        {
            Contract.Requires(table != null);
            Contract.Requires(partialSymbol != null);
            Contract.Ensures(Contract.Result<PartialSymbol>().IsValid);

            return Create(table, (StringSegment)partialSymbol);
        }

        /// <summary>
        /// Creates a PartialSymbol from a string and abandons if the string is invalid.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded identifiers, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public static PartialSymbol Create<T>(StringTable table, T partialSymbol)
            where T : struct, ICharSpan<T>
        {
            Contract.Requires(table != null);
            Contract.Ensures(Contract.Result<PartialSymbol>().IsValid);

            PartialSymbol result;
            bool f = TryCreate(table, partialSymbol, out result);
            Contract.Assert(f);
            return result;
        }

        /// <summary>
        /// Creates a PartialSymbol from a identifier atom.
        /// </summary>
        public static PartialSymbol Create(SymbolAtom atom)
        {
            Contract.Requires(atom.IsValid);
            Contract.Ensures(Contract.Result<PartialSymbol>().IsValid);

            return new PartialSymbol(atom.StringId);
        }

        /// <summary>
        /// Creates a PartialSymbol from an array of identifier atoms.
        /// </summary>
        public static PartialSymbol Create(params SymbolAtom[] atoms)
        {
            Contract.Requires(atoms != null);
            Contract.RequiresForAll(atoms, a => a.IsValid);
            Contract.Ensures(Contract.Result<PartialSymbol>().IsValid);

            var components = new StringId[atoms.Length];
            int count = 0;
            foreach (SymbolAtom a in atoms)
            {
                components[count++] = a.StringId;
            }

            return new PartialSymbol(components);
        }

        /// <summary>
        /// Determines whether a particular character is valid within a relative identifier.
        /// </summary>
        public static bool IsValidRelativeIdChar(char value)
        {
            return SymbolCharacters.IsValidChar(value);
        }

        /// <summary>
        /// Extends a relative identifier with new identifier components.
        /// </summary>
        public PartialSymbol Combine(PartialSymbol identifier)
        {
            Contract.Requires(IsValid);
            Contract.Requires(identifier.IsValid);
            Contract.Ensures(Contract.Result<PartialSymbol>().IsValid);

            var components = new StringId[m_components.Length + identifier.m_components.Length];
            int count = 0;
            foreach (StringId component in m_components)
            {
                components[count++] = component;
            }

            foreach (StringId component in identifier.m_components)
            {
                components[count++] = component;
            }

            return new PartialSymbol(components);
        }

        /// <summary>
        /// Extends a relative identifier with a new identifier components.
        /// </summary>
        public PartialSymbol Combine(SymbolAtom atom)
        {
            Contract.Requires(IsValid);
            Contract.Requires(atom.IsValid);
            Contract.Ensures(Contract.Result<PartialSymbol>().IsValid);

            var components = new StringId[m_components.Length + 1];
            int count = 0;
            foreach (StringId component in m_components)
            {
                components[count++] = component;
            }

            components[count] = atom.StringId;

            return new PartialSymbol(components);
        }

        /// <summary>
        /// Extends a relative identifier with new identifier components.
        /// </summary>
        public PartialSymbol Combine(SymbolAtom atom1, SymbolAtom atom2)
        {
            Contract.Requires(IsValid);
            Contract.Requires(atom1.IsValid);
            Contract.Requires(atom2.IsValid);
            Contract.Ensures(Contract.Result<PartialSymbol>().IsValid);

            var components = new StringId[m_components.Length + 2];
            int count = 0;
            foreach (StringId component in m_components)
            {
                components[count++] = component;
            }

            components[count++] = atom1.StringId;
            components[count] = atom2.StringId;

            return new PartialSymbol(components);
        }

        /// <summary>
        /// Extends a relative identifier with new identifier components.
        /// </summary>
        public PartialSymbol Combine(params SymbolAtom[] atoms)
        {
            Contract.Requires(IsValid);
            Contract.Requires(atoms != null);
            Contract.RequiresForAll(atoms, a => a.IsValid);
            Contract.Ensures(Contract.Result<PartialSymbol>().IsValid);

            var components = new StringId[m_components.Length + atoms.Length];
            int count = 0;
            foreach (StringId component in m_components)
            {
                components[count++] = component;
            }

            foreach (SymbolAtom a in atoms)
            {
                components[count++] = a.StringId;
            }

            return new PartialSymbol(components);
        }

        /// <summary>
        /// Concatenates a identifier atom to the end of a relative identifier.
        /// </summary>
        /// <remarks>
        /// The relative identifier may not be empty when calling this method.
        /// </remarks>
        public PartialSymbol Concat(StringTable table, SymbolAtom addition)
        {
            Contract.Requires(IsValid);
            Contract.Requires(table != null);
            Contract.Requires(addition.IsValid);
            Contract.Requires(!IsEmpty);
            Contract.Ensures(Contract.Result<PartialSymbol>().IsValid);

            StringId changed = new SymbolAtom(m_components[m_components.Length - 1]).Concat(table, addition).StringId;

            var components = new StringId[m_components.Length];
            int count = 0;
            foreach (StringId component in m_components)
            {
                components[count++] = component;
            }

            components[count - 1] = changed;
            return new PartialSymbol(components);
        }

        /// <summary>
        /// Removes the last identifier component of this relative identifier.
        /// </summary>
        /// <remarks>
        /// The relative identifier may not be empty when calling this method.
        /// </remarks>
        public PartialSymbol GetParent()
        {
            Contract.Requires(IsValid);
            Contract.Requires(!IsEmpty);
            Contract.Ensures(Contract.Result<PartialSymbol>().IsValid);

            var components = new StringId[m_components.Length - 1];
            for (int i = 0; i < m_components.Length - 1; i++)
            {
                components[i] = m_components[i];
            }

            return new PartialSymbol(components);
        }

        /// <summary>
        /// Returns the last component of the relative identifier.
        /// </summary>
        /// <remarks>
        /// The relative identifier may not be empty when calling this method.
        /// </remarks>
        public SymbolAtom GetName()
        {
            Contract.Requires(IsValid);
            Contract.Requires(!IsEmpty);
            Contract.Ensures(Contract.Result<SymbolAtom>().IsValid);

            return new SymbolAtom(m_components[m_components.Length - 1]);
        }

        /// <summary>
        /// Indicates if this relative identifier and the one given represent the same underlying value.
        /// </summary>
        /// <remarks>
        /// Note that it is only meaningful to compare relative identifiers created against the same <see cref="StringTable" />.
        /// </remarks>
        public bool Equals(PartialSymbol other)
        {
            if (m_components == null || other.m_components == null)
            {
                return m_components == null && other.m_components == null;
            }

            if (m_components.Length != other.m_components.Length)
            {
                return false;
            }

            for (int i = 0; i < m_components.Length; i++)
            {
                if (m_components[i] != other.m_components[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Indicates if a given object is a PartialSymbol equal to this one. See <see cref="Equals(PartialSymbol)" />.
        /// </summary>
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            if (m_components == null || m_components.Length == 0)
            {
                return 0;
            }

            // good enough...
            return m_components[0].GetHashCode();
        }

        /// <summary>
        /// Equality operator for two relative identifiers.
        /// </summary>
        public static bool operator ==(PartialSymbol left, PartialSymbol right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator for two relative identifiers.
        /// </summary>
        public static bool operator !=(PartialSymbol left, PartialSymbol right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Returns a string representation of the relative identifier.
        /// </summary>
        /// <param name="table">The string table used when creating the PartialSymbol.</param>
        public string ToString(StringTable table)
        {
            Contract.Requires(IsValid);
            Contract.Requires(table != null);
            Contract.Ensures(Contract.Result<string>() != null);

            int length = 0;
            foreach (StringId component in m_components)
            {
                length += table.GetLength(component) + 1;
            }

            if (length > 0)
            {
                length--;
            }

            using (var wrap = Pools.GetStringBuilder())
            {
                StringBuilder sb = wrap.Instance;

                foreach (StringId component in m_components)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(SymbolCharacters.DottedIdentifierSeparatorChar);
                    }

                    table.CopyString(component, sb);
                }

                return sb.ToString();
            }
        }

        /// <summary>
        /// Convert to DottedIdentifier
        /// </summary>
        public DottedIdentifier ToDottedIdentifier()
        {
            if (!IsValid || IsEmpty)
            {
                return null;
            }

            int index = Components.Length - 1;
            DottedIdentifier dottedIdentifier = null;
            while (index != 0)
            {
                dottedIdentifier = new DottedIdentifier(new SymbolAtom(Components[index]), dottedIdentifier);
                index--;
            }

            return dottedIdentifier;
        }

        /// <summary>
        /// Convert from DottedIdentifier
        /// </summary>
        public static PartialSymbol FromDottedIdentifier(SymbolTable symbolTable, DottedIdentifier identifier)
        {
            using (var pooledStringIds = Pools.GetStringIdList())
            {
                var stringIds = pooledStringIds.Instance;
                while (identifier != null)
                {
                    stringIds.Add(identifier.Head.StringId);
                    identifier = identifier.GetTail(symbolTable);
                }

                var components = stringIds.AsArray();
                return new PartialSymbol(components);
            }
        }

#pragma warning disable 809

        /// <summary>
        /// Not available for PartialSymbol, throws an exception
        /// </summary>
        [Obsolete("Not suitable for PartialSymbol")]
        public override string ToString()
        {
            throw new NotImplementedException();
        }

        [SuppressMessage("Microsoft.Performance", "CA1811")]
        private string ToDebuggerDisplay()
        {
            return string.Join(".", m_components.Select(c => c.ToDebuggerDisplay()));
        }

#pragma warning restore 809

        /// <summary>
        /// Determines whether this instance has been properly initialized or is merely default(PartialSymbol).
        /// </summary>
        public bool IsValid => m_components != null;

        /// <summary>
        /// Determines whether this instance is the empty identifier.
        /// </summary>
        public bool IsEmpty
        {
            get
            {
                Contract.Requires(IsValid);
                return m_components.Length == 0;
            }
        }

        /// <summary>
        /// Returns the internal array of atoms representing this relative identifier.
        /// </summary>
        internal StringId[] Components => m_components;

        /// <summary>
        /// Explains the identifier error
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
            /// PartialSymbol does not allow for '.' and the beginning or end.
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
            return root.TryGet(symbolTable, this, out fullSymbol);
        }

        /// <summary>
        /// Converts the symbol to its string representation
        /// </summary>
        public string ToString(SymbolTable symbolTable)
        {
            return ToString(symbolTable.StringTable);
        }

        #endregion
    }
}

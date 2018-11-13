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
    /// A single component of a symbol.
    /// </summary>
    /// <remarks>
    /// This type represents a single element of a symbol
    /// </remarks>
    [DebuggerDisplay("{ToDebuggerDisplay(),nq}")]
    public readonly struct SymbolAtom : IEquatable<SymbolAtom>, ISymbol
    {
        /// <summary>
        /// Invalid atom for uninitialized fields.
        /// </summary>
        public static readonly SymbolAtom Invalid = default(SymbolAtom);

        internal SymbolAtom(StringId value)
        {
            Contract.Requires(value.IsValid);
            StringId = value;
        }

        /// <summary>
        /// Unsafe factory method that constructs <see cref="PathAtom"/> instance from the underlying string id.
        /// </summary>
        public static SymbolAtom UnsafeCreateFrom(StringId value)
        {
            return new SymbolAtom(value);
        }

        /// <summary>
        /// Validate whether a string is a valid identifier atom.
        /// </summary>
        /// <remarks>
        /// The rules for a valid identifier atom are that the input string may not
        /// be empty and must only contain characters reported as valid by IsValidIdentifierAtomChar.
        /// </remarks>
        [Pure]
        public static bool Validate<T>(T prospectiveAtom)
            where T : struct, ICharSpan<T>
        {
            ParseResult parseResult = Validate(prospectiveAtom, out _);
            return parseResult == ParseResult.Success;
        }

        /// <summary>
        /// Validate whether a string is a valid identifier atom.
        /// </summary>
        /// <remarks>
        /// The rules for a valid identifier atom are that the input string may not
        /// be empty and must only contain characters reported as valid by IsValidIdentifierAtomChar.
        /// </remarks>
        [Pure]
        public static ParseResult Validate<T>(T prospectiveAtom, out int characterWithError)
            where T : struct, ICharSpan<T>
        {
            // superfluous, but check insists
            Contract.Requires(prospectiveAtom.Length >= 0);

            if (prospectiveAtom.Length == 0)
            {
                // can't be empty
                characterWithError = 0;
                return ParseResult.FailureDueToEmptyValue;
            }

            if (!IsValidIdentifierAtomStartChar(prospectiveAtom[0]))
            {
                characterWithError = 0;
                return ParseResult.FailureDueToInvalidCharacter;
            }

            if (prospectiveAtom.CheckIfOnlyContainsValidIdentifierAtomChars(out characterWithError))
            {
                return ParseResult.Success;
            }

            return ParseResult.FailureDueToInvalidCharacter;
        }

        /// <summary>
        /// Try to create an IdentifierAtom from a string.
        /// </summary>
        /// <remarks>
        /// The rules for a valid identifier atom are that the input string may not
        /// be empty and must only contain characters reported as valid by IsValidIdentifierAtomChar.
        /// </remarks>
        public static bool TryCreate(StringTable table, string atom, out SymbolAtom result)
        {
            Contract.Requires(table != null);
            Contract.Requires(atom != null);
            Contract.Ensures(Contract.Result<bool>() == Contract.ValueAtReturn(out result).IsValid);

            return TryCreate(table, (StringSegment)atom, out result);
        }

        /// <summary>
        /// Try to create an IdentifierAtom from a string.
        /// </summary>
        /// <remarks>
        /// The rules for a valid identifier atom are that the input string may not
        /// be empty and must only contain characters reported as valid by IsValidIdentifierAtomChar.
        /// </remarks>
        public static bool TryCreate<T>(StringTable table, T atom, out SymbolAtom result)
            where T : struct, ICharSpan<T>
        {
            Contract.Requires(table != null);
            Contract.Ensures(Contract.Result<bool>() == Contract.ValueAtReturn(out result).IsValid);

            ParseResult parseResult = TryCreate(table, atom, out result, out _);
            return parseResult == ParseResult.Success;
        }

        /// <summary>
        /// Try to create an IdentifierAtom from a string.
        /// </summary>
        /// <remarks>
        /// The rules for a valid identifier atom are that the input string may not
        /// be empty and must only contain characters reported as valid by IsValidIdentifierAtomChar.
        /// </remarks>
        public static ParseResult TryCreate<T>(StringTable table, T atom, out SymbolAtom result, out int characterWithError)
            where T : struct, ICharSpan<T>
        {
            Contract.Requires(table != null);
            Contract.Ensures((Contract.Result<ParseResult>() == ParseResult.Success) == Contract.ValueAtReturn(out result).IsValid);

            ParseResult validationResult = Validate(atom, out characterWithError);
            result = validationResult == ParseResult.Success ? new SymbolAtom(table.AddString(atom)) : default(SymbolAtom);
            return validationResult;
        }

        /// <summary>
        /// Creates an IdentifierAtom from a string and abandons if the atom is invalid.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded atoms, don't use with any user input.
        /// </remarks>
        public static SymbolAtom Create(StringTable table, string atom)
        {
            Contract.Requires(table != null);
            Contract.Requires(atom != null);
            Contract.Requires(atom.Length > 0);
            Contract.Ensures(Contract.Result<SymbolAtom>().IsValid);

            return Create(table, (StringSegment)atom);
        }

        /// <summary>
        /// Creates an IdentifierAtom from a string and abandons if the atom is invalid.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded atoms, don't use with any user input.
        /// </remarks>
        public static SymbolAtom Create<T>(StringTable table, T atom)
            where T : struct, ICharSpan<T>
        {
            Contract.Requires(table != null);
            Contract.Requires(atom.Length > 0);
            Contract.Ensures(Contract.Result<SymbolAtom>().IsValid);

            bool identifierAtomCreated = TryCreate(table, atom, out SymbolAtom result);

            if (!identifierAtomCreated)
            {
                // Moving this check inside the if block to avoid 'atom' boxing allocation.
                Contract.Assert(false, I($"Failed to create SymbolAtom from '{atom}'."));
            }

            return result;
        }

        /// <summary>
        /// Creates an IdentifierAtom from a string regardless whether it is a well-formed identifier atom.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded atoms, don't use with any user input.
        /// </remarks>
        public static SymbolAtom CreateUnchecked(StringTable table, string atom)
        {
            Contract.Requires(table != null);
            Contract.Requires(atom != null);
            Contract.Requires(atom.Length > 0);
            Contract.Ensures(Contract.Result<SymbolAtom>().IsValid);

            return CreateUnchecked(table, (StringSegment)atom);
        }

        /// <summary>
        /// Creates an IdentifierAtom from a string regardless whether it is a well-formed identifier atom.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded atoms, don't use with any user input.
        /// </remarks>
        public static SymbolAtom CreateUnchecked<T>(StringTable table, T atom)
            where T : struct, ICharSpan<T>
        {
            Contract.Requires(table != null);
            Contract.Requires(atom.Length > 0);
            Contract.Ensures(Contract.Result<SymbolAtom>().IsValid);

            StringId id = table.AddString(atom);
            return new SymbolAtom(id);
        }

        /// <summary>
        /// Concatenates two path atoms together.
        /// </summary>
        public SymbolAtom Concat(StringTable table, SymbolAtom addition)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);
            Contract.Requires(addition.IsValid);
            Contract.Ensures(Contract.Result<SymbolAtom>().IsValid);

            StringId newId = table.Concat(StringId, addition.StringId);
            return new SymbolAtom(newId);
        }

        /// <summary>
        /// Indicates if this identifier atom and the one given represent the same underlying value.
        /// </summary>
        /// <remarks>
        /// Note that it is only meaningful to compare IdentifierAtoms created against the same StringTable.
        /// </remarks>
        public bool Equals(SymbolAtom other)
        {
            return StringId == other.StringId;
        }

        /// <summary>
        /// Indicates if a given object is an IdentifierAtom equal to this one. See <see cref="Equals(BuildXL.Utilities.SymbolAtom)" />.
        /// </summary>
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return StringId.GetHashCode();
        }

        /// <summary>
        /// Equality operator for two IdentifierAtoms.
        /// </summary>
        public static bool operator ==(SymbolAtom left, SymbolAtom right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator for two IdentifierAtoms.
        /// </summary>
        public static bool operator !=(SymbolAtom left, SymbolAtom right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Determines whether a particular character is valid as the first character of an atom.
        /// </summary>
        public static bool IsValidIdentifierAtomStartChar(char value)
        {
            return SymbolCharacters.IsValidStartChar(value);
        }

        /// <summary>
        /// Determines whether a particular character is valid within an atom.
        /// </summary>
        public static bool IsValidIdentifierAtomChar(char value)
        {
            return SymbolCharacters.IsValidChar(value);
        }

        /// <summary>
        /// Returns a string representation of the identifier atom.
        /// </summary>
        /// <param name="table">The string table used when creating the atom.</param>
        [Pure]
        public string ToString(StringTable table)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);
            Contract.Ensures(!string.IsNullOrEmpty(Contract.Result<string>()));

            return table.GetString(StringId);
        }

        /// <nodoc />
        [ExcludeFromCodeCoverage]
        public string ToDebuggerDisplay() => StringId.ToDebuggerDisplay();
#pragma warning disable 809

        /// <summary>
        /// Not available for IdentifierAtom, throws an exception
        /// </summary>
        [Obsolete("Not suitable for IdentifierAtom")]
        public override string ToString()
        {
            throw new NotImplementedException();
        }

#pragma warning restore 809

        /// <summary>
        /// Determines whether this instance has been properly initialized or is merely default(IdentifierAtom).
        /// </summary>
        public bool IsValid => StringId.IsValid;

        /// <summary>
        /// Returns the string identifier for this atom.
        /// </summary>
        public StringId StringId { get; }

        /// <summary>
        /// Explains parsing errors.
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
            /// Empty IdentifierAtom is not allowed to be empty.
            /// </summary>
            FailureDueToEmptyValue,
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

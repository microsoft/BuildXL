// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;

namespace BuildXL.Utilities
{
    /// <summary>
    /// A single component of a file path.
    /// </summary>
    /// <remarks>
    /// This type represents a single file name or directory name.
    /// </remarks>
    [DebuggerDisplay("{ToDebuggerDisplay(),nq}")]
    public readonly struct PathAtom : IEquatable<PathAtom>, IPathSegment
    {
        /// <summary>
        /// Invalid atom for uninitialized fields.
        /// </summary>
        public static readonly PathAtom Invalid = default(PathAtom);

        private static readonly BitArray s_invalidPathAtomChars = new BitArray(65536); // one bit per char value

        [SuppressMessage("Microsoft.Usage", "CA2207:InitializeValueTypeStaticFieldsInline")]
        static PathAtom()
        {
            // set a bit for each invalid character value
            foreach (char ch in Path.GetInvalidFileNameChars())
            {
                s_invalidPathAtomChars.Set(ch, true);
            }

            // also explicitly disallow control characters and other weird things just to help maintain sanity
            for (int i = 0; i < 65536; i++)
            {
                var ch = unchecked((char)i);
                if (char.IsControl(ch))
                {
                    s_invalidPathAtomChars.Set(ch, true);
                }
            }
        }

        internal PathAtom(StringId value)
        {
            Contract.Requires(value.IsValid);
            StringId = value;
        }

        /// <summary>
        /// Unsafe factory method that constructs <see cref="PathAtom"/> instance from the underlying string id.
        /// </summary>
        public static PathAtom UnsafeCreateFrom(StringId value)
        {
            return new PathAtom(value);
        }

        /// <summary>
        /// Validate whether a string is a valid path atom.
        /// </summary>
        /// <remarks>
        /// The rules for a valid path atom are that the input string may not
        /// be empty and must only contain characters reported as valid by IsValidPathAtomChar.
        /// </remarks>
        [Pure]
        public static bool Validate<T>(T prospectiveAtom)
            where T : struct, ICharSpan<T>
        {
            ParseResult parseResult = Validate(prospectiveAtom, out _);
            return parseResult == ParseResult.Success;
        }

        /// <summary>
        /// Validate whether a string is a valid path atom.
        /// </summary>
        /// <remarks>
        /// The rules for a valid path atom are that the input string may not
        /// be empty and must only contain characters reported as valid by IsValidPathAtomChar.
        /// </remarks>
        [Pure]
        public static ParseResult Validate<T>(T prospectiveAtom, out int characterWithError)
            where T : struct, ICharSpan<T>
        {
            if (prospectiveAtom.Length == 0)
            {
                // can't be empty
                characterWithError = 0;
                return ParseResult.FailureDueToEmptyValue;
            }

            if (prospectiveAtom.CheckIfOnlyContainsValidPathAtomChars(out characterWithError))
            {
                // NOTE: In theory, we should prevent path atoms that use the well-known no-no strings
                //       from Windows such as AUX, COM1, LPN1. We don't do that though, it's not worth the
                //       cycles.
                return ParseResult.Success;
            }

            return ParseResult.FailureDueToInvalidCharacter;
        }

        /// <summary>
        /// Try to create a PathAtom from a string.
        /// </summary>
        /// <remarks>
        /// The rules for a valid path atom are that the input string may not
        /// be empty and must only contain characters reported as valid by IsValidPathAtomChar.
        /// </remarks>
        public static bool TryCreate(StringTable table, string atom, out PathAtom result)
        {
            Contract.Requires(table != null);
            Contract.Requires(atom != null);
            Contract.Ensures(Contract.Result<bool>() == Contract.ValueAtReturn(out result).IsValid);

            return TryCreate(table, (StringSegment)atom, out result);
        }

        /// <summary>
        /// Try to create a PathAtom from a string.
        /// </summary>
        /// <remarks>
        /// The rules for a valid path atom are that the input string may not
        /// be empty and must only contain characters reported as valid by IsValidPathAtomChar.
        /// </remarks>
        public static bool TryCreate<T>(StringTable table, T atom, out PathAtom result)
            where T : struct, ICharSpan<T>
        {
            Contract.Requires(table != null);
            Contract.Ensures(Contract.Result<bool>() == Contract.ValueAtReturn(out result).IsValid);

            ParseResult parseResult = TryCreate(table, atom, out result, out _);
            return parseResult == ParseResult.Success;
        }

        /// <summary>
        /// Try to create a PathAtom from a string.
        /// </summary>
        /// <remarks>
        /// The rules for a valid path atom are that the input string may not
        /// be empty and must only contain characters reported as valid by IsValidPathAtomChar.
        /// </remarks>
        public static ParseResult TryCreate<T>(StringTable table, T atom, out PathAtom result, out int characterWithError)
            where T : struct, ICharSpan<T>
        {
            Contract.Requires(table != null);
            Contract.Ensures((Contract.Result<ParseResult>() == ParseResult.Success) == Contract.ValueAtReturn(out result).IsValid);

            ParseResult validationResult = Validate(atom, out characterWithError);
            result = validationResult == ParseResult.Success ? new PathAtom(table.AddString(atom)) : default(PathAtom);
            return validationResult;
        }

        /// <summary>
        /// Creates a PathAtom from a string and abandons if the atom is invalid.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded literals, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public static PathAtom Create(StringTable table, string atom)
        {
            Contract.Requires(table != null);
            Contract.Requires(atom != null);
            Contract.Requires(atom.Length > 0);
            Contract.Ensures(Contract.Result<PathAtom>().IsValid);

            return Create(table, (StringSegment)atom);
        }

        /// <summary>
        /// Creates a PathAtom from a string and abandons if the atom is invalid.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded literals, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public static PathAtom Create<T>(StringTable table, T atom)
            where T : struct, ICharSpan<T>
        {
            Contract.Requires(table != null);
            Contract.Requires(atom.Length > 0);
            Contract.Ensures(Contract.Result<PathAtom>().IsValid);

            bool pathAtomCreated = TryCreate(table, atom, out PathAtom result);

            if (!pathAtomCreated)
            {
                Contract.Assert(false, "Unable to create path atom '" + atom.ToString() + "'");
            }

            return result;
        }

        /// <summary>
        /// Concatenates two path atoms together.
        /// </summary>
        public PathAtom Concat(StringTable table, PathAtom addition)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);
            Contract.Requires(addition.IsValid);
            Contract.Ensures(Contract.Result<PathAtom>().IsValid);

            StringId newId = table.Concat(StringId, addition.StringId);
            return new PathAtom(newId);
        }

        /// <summary>
        /// Changes the extension of a path atom.
        /// </summary>
        /// <param name="table">The string table holding the strings for the path atoms.</param>
        /// <param name="extension">The new file extension. If this is PathAtom.Invalid, this method removes any existing extension.</param>
        /// <returns>A new path atom with the applied extension.</returns>
        public PathAtom ChangeExtension(StringTable table, PathAtom extension)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);
            Contract.Ensures(Contract.Result<PathAtom>().IsValid);

            if (extension.IsValid)
            {
                StringId newId = table.ChangeExtension(StringId, extension.StringId);
                return new PathAtom(newId);
            }

            return RemoveExtension(table);
        }

        /// <summary>
        /// Removes the extension of a path atom.
        /// </summary>
        /// <returns>A new path atom without the final extension.</returns>
        public PathAtom RemoveExtension(StringTable table)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);
            Contract.Ensures(Contract.Result<PathAtom>().IsValid);

            StringId newId = table.RemoveExtension(StringId);
            return new PathAtom(newId);
        }

        /// <summary>
        /// Gets the extension of a path atom.
        /// </summary>
        /// <returns>A new path atom containing the extension.</returns>
        public PathAtom GetExtension(StringTable table)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);

            StringId newId = table.GetExtension(StringId);
            return newId.IsValid ? new PathAtom(newId) : Invalid;
        }

        /// <summary>
        /// Indicates if this path atom and the one given represent the same underlying value.
        /// </summary>
        /// <remarks>
        /// Note that it is only meaningful to compare PathAtoms created against the same StringTable.
        /// </remarks>
        public bool Equals(PathAtom other)
        {
            return StringId == other.StringId;
        }

        /// <summary>
        /// Indicates if a given object is a PathAtom equal to this one. See <see cref="Equals(BuildXL.Utilities.PathAtom)" />.
        /// </summary>
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <summary>
        /// Performs a case insensitive comparison against another PathAtom
        /// </summary>
        /// <remarks>
        /// Note that it is only meaningful to compare PathAtoms created against the same StringTable.
        /// </remarks>
        public bool CaseInsensitiveEquals(StringTable stringTable, PathAtom other)
        {
            Contract.Requires(stringTable != null);

            return stringTable.CaseInsensitiveEqualityComparer.Equals(StringId, other.StringId);
        }

        /// <summary>
        /// Performs a case insensitive ordinal comparison against another PathAtom
        /// </summary>
        /// <remarks>
        /// Note that it is only meaningful to compare PathAtoms created against the same StringTable.
        /// </remarks>
        public int CaseInsensitiveCompareTo(StringTable stringTable, PathAtom other)
        {
            Contract.Requires(stringTable != null);

            return stringTable.CompareCaseInsensitive(StringId, other.StringId);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return StringId.GetHashCode();
        }

        /// <summary>
        /// Equality operator for two PathAtoms.
        /// </summary>
        public static bool operator ==(PathAtom left, PathAtom right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator for two PathAtoms.
        /// </summary>
        public static bool operator !=(PathAtom left, PathAtom right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Determines whether a particular character is valid within an atom.
        /// </summary>
        public static bool IsValidPathAtomChar(char value)
        {
            return !s_invalidPathAtomChars[value];
        }

        /// <summary>
        /// Returns a string representation of the path atom.
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

        string IPathSegment.ToString(StringTable table, PathFormat pathFormat)
        {
            return ToString(table);
        }

        /// <nodoc/>
        [ExcludeFromCodeCoverage]
        public string ToDebuggerDisplay() => StringId.ToDebuggerDisplay();

#pragma warning disable 809

        /// <summary>
        /// Not available for PathAtom, throws an exception
        /// </summary>
        [Obsolete("Not suitable for PathAtom")]
        public override string ToString()
        {
            throw new NotImplementedException();
        }

#pragma warning restore 809

        /// <summary>
        /// Determines whether this instance has been properly initialized or is merely default(PathAtom).
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
            /// Empty PathAtom is not allowed to be empty.
            /// </summary>
            FailureDueToEmptyValue,
        }
    }
}

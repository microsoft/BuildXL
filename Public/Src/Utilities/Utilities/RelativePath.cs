// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Text;
using BuildXL.Utilities.Collections;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Represents a relative file system path.
    /// </summary>
    /// <remarks>
    /// This type holds a simple relative path without any . or .. present.
    /// </remarks>
    public readonly struct RelativePath : IEquatable<RelativePath>, IPathSegment
    {
        /// <summary>
        /// Invalid path for uninitialized fields.
        /// </summary>
        public static readonly RelativePath Invalid = default(RelativePath);

        /// <summary>
        /// Empty relative path
        /// </summary>
        public static readonly RelativePath Empty = new RelativePath(CollectionUtilities.EmptyArray<StringId>());

        private static readonly BitArray s_invalidPathChars = new BitArray(65536); // one bit per char value

        [SuppressMessage("Microsoft.Usage", "CA2207:InitializeValueTypeStaticFieldsInline")]
        static RelativePath()
        {
            // set a bit for each invalid character value
            foreach (char ch in Path.GetInvalidPathChars())
            {
                s_invalidPathChars.Set(ch, true);
            }

            // also explicitly disallow control characters and other weird things
            for (int i = 0; i < 65536; i++)
            {
                var ch = unchecked((char)i);
                if (char.IsControl(ch))
                {
                    s_invalidPathChars.Set(ch, true);
                }
            }

            // disallow absolute path chars
            s_invalidPathChars.Set('?', !OperatingSystemHelper.IsUnixOS);
            s_invalidPathChars.Set('*', !OperatingSystemHelper.IsUnixOS);
            s_invalidPathChars.Set(':', !OperatingSystemHelper.IsUnixOS);
        }

        internal RelativePath(params StringId[] components)
        {
            Contract.Requires(components != null);
            Contract.RequiresForAll(components, component => component.IsValid);

            Components = components;
        }

        /// <summary>
        /// Try to create a RelativePath from a string.
        /// </summary>
        /// <returns>Return false if the input path is not in a valid format.</returns>
        public static bool TryCreate(StringTable table, string relativePath, out RelativePath result)
        {
            Contract.Requires(table != null);
            Contract.Requires(relativePath != null);
            Contract.Ensures(Contract.Result<bool>() == Contract.ValueAtReturn(out result).IsValid);

            return TryCreate(table, (StringSegment)relativePath, out result);
        }

        /// <summary>
        /// Try to create a RelativePath from a string.
        /// </summary>
        /// <returns>Return false if the input path is not in a valid format.</returns>
        public static bool TryCreate<T>(StringTable table, T relativePath, out RelativePath result)
            where T : struct, ICharSpan<T>
        {
            Contract.Requires(table != null);
            Contract.Ensures(Contract.Result<bool>() == Contract.ValueAtReturn(out result).IsValid);

            ParseResult parseResult = TryCreate(table, relativePath, out result, out _);
            return parseResult == ParseResult.Success;
        }

        /// <summary>
        /// Try to create a RelativePath from a string.
        /// </summary>
        /// <returns>Return the parser result indicating success, or what was wrong with the parsing.</returns>
        public static ParseResult TryCreate<T>(StringTable table, T relativePath, out RelativePath result, out int characterWithError) where T : struct, ICharSpan<T>
        {
            return TryCreateInternal(table, relativePath, out result, out characterWithError);
        }

        /// <summary>
        /// Internal api to try to create a RelativePath from a string.
        /// </summary>
        /// <remarks>This function serves as an internal overload for tryCreate when called from AbsolutePath so we do not get the DotDotOutOfScope error when traversing beyond the root.</remarks>
        /// <param name="table">StringTable instance.</param>
        /// <param name="relativePath">Relative path to pass in.</param>
        /// <param name="result">Output relative path after parsing.</param>
        /// <param name="characterWithError">Output the character that had the error.</param>
        /// <param name="allowDotDotOutOfScope">Whether to allow the function to parse .. beyond the root.</param>
        /// <returns>Return the parser result indicating success, or what was wrong with the parsing.</returns>
        internal static ParseResult TryCreateInternal<T>(StringTable table, T relativePath, out RelativePath result, out int characterWithError, bool allowDotDotOutOfScope = false)
            where T : struct, ICharSpan<T>
        {
            Contract.Requires(table != null);
            Contract.Ensures((Contract.Result<ParseResult>() == ParseResult.Success) == Contract.ValueAtReturn(out result).IsValid);

            using (var wrap = Pools.GetStringIdList())
            {
                List<StringId> components = wrap.Instance;

                int index = 0;
                int start = 0;
                while (index < relativePath.Length)
                {
                    var ch = relativePath[index];

                    // trivial reject of invalid characters
                    if (!IsValidRelativePathChar(ch))
                    {
                        characterWithError = index;
                        result = Invalid;
                        return ParseResult.FailureDueToInvalidCharacter;
                    }

                    if (ch == '\\' || ch == '/')
                    {
                        // found a component separator
                        if (index > start)
                        {
                            // make a path atom out of [start..index]
                            PathAtom.ParseResult papr = PathAtom.TryCreate(
                                table,
                                relativePath.Subsegment(start, index - start),
                                out PathAtom atom,
                                out int charError);

                            if (papr != PathAtom.ParseResult.Success)
                            {
                                characterWithError = index + charError;
                                result = Invalid;
                                return ParseResult.FailureDueToInvalidCharacter;
                            }

                            components.Add(atom.StringId);
                        }

                        // skip over the slash
                        index++;
                        start = index;
                        continue;
                    }

                    if (ch == '.' && index == start)
                    {
                        // component starts with a .
                        if ((index == relativePath.Length - 1)
                            || (relativePath[index + 1] == '\\')
                            || (relativePath[index + 1] == '/'))
                        {
                            // component is a sole . so skip it
                            index += 2;
                            start = index;
                            continue;
                        }

                        if (relativePath[index + 1] == '.')
                        {
                            // component starts with ..
                            if ((index == relativePath.Length - 2)
                                || (relativePath[index + 2] == '\\')
                                || (relativePath[index + 2] == '/'))
                            {
                                // component is a sole .. so try to go up
                                if (components.Count == 0 && !allowDotDotOutOfScope)
                                {
                                    characterWithError = index;
                                    result = Invalid;
                                    return ParseResult.FailureDueToDotDotOutOfScope;
                                }

                                if (components.Count != 0)
                                {
                                    components.RemoveAt(components.Count - 1);
                                }

                                index += 3;
                                start = index;
                                continue;
                            }
                        }
                    }

                    index++;
                }

                if (index > start)
                {
                    // make a path atom out of [start..index]
                    PathAtom.ParseResult papr = PathAtom.TryCreate(
                        table,
                        relativePath.Subsegment(start, index - start),
                        out PathAtom atom,
                        out int charError);

                    if (papr != PathAtom.ParseResult.Success)
                    {
                        characterWithError = index + charError;
                        result = Invalid;
                        return ParseResult.FailureDueToInvalidCharacter;
                    }

                    components.Add(atom.StringId);
                }

                result = new RelativePath(components.ToArray());

                characterWithError = -1;
                return ParseResult.Success;
            }
        }

        /// <summary>
        /// Creates a RelativePath from a string and abandons if the string is invalid.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded paths, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public static RelativePath Create(StringTable table, string relativePath)
        {
            Contract.Requires(table != null);
            Contract.Requires(relativePath != null);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            return Create(table, (StringSegment)relativePath);
        }

        /// <summary>
        /// Creates a RelativePath from a string and abandons if the string is invalid.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded paths, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public static RelativePath Create<T>(StringTable table, T relativePath)
            where T : struct, ICharSpan<T>
        {
            Contract.Requires(table != null);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            bool f = TryCreate(table, relativePath, out RelativePath result);
            if (!f)
            {
                throw Contract.AssertFailure($"Failed creating relative path from '{relativePath}'");
            }

            return result;
        }

        /// <summary>
        /// Creates a RelativePath from a path atom.
        /// </summary>
        public static RelativePath Create(PathAtom atom)
        {
            Contract.Requires(atom.IsValid);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            return new RelativePath(atom.StringId);
        }

        /// <summary>
        /// Creates a RelativePath from an array of path atoms.
        /// </summary>
        public static RelativePath Create(params PathAtom[] atoms)
        {
            Contract.Requires(atoms != null);
            Contract.RequiresForAll(atoms, a => a.IsValid);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            var components = new StringId[atoms.Length];
            int count = 0;
            foreach (PathAtom a in atoms)
            {
                components[count++] = a.StringId;
            }

            return new RelativePath(components);
        }

        /// <summary>
        /// Determines whether a particular character is valid within a relative path.
        /// </summary>
        public static bool IsValidRelativePathChar(char value)
        {
            return !s_invalidPathChars[value];
        }

        /// <summary>
        /// Extends a relative path with new path components.
        /// </summary>
        public RelativePath Combine(RelativePath path)
        {
            Contract.Requires(IsValid);
            Contract.Requires(path.IsValid);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            var components = new StringId[Components.Length + path.Components.Length];
            int count = 0;
            foreach (StringId component in Components)
            {
                components[count++] = component;
            }

            foreach (StringId component in path.Components)
            {
                components[count++] = component;
            }

            return new RelativePath(components);
        }

        /// <summary>
        /// Extends a relative path with a new path components.
        /// </summary>
        public RelativePath Combine(PathAtom atom)
        {
            Contract.Requires(IsValid);
            Contract.Requires(atom.IsValid);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            var components = new StringId[Components.Length + 1];
            int count = 0;
            foreach (StringId component in Components)
            {
                components[count++] = component;
            }

            components[count] = atom.StringId;

            return new RelativePath(components);
        }

        /// <summary>
        /// Extends a relative path with new path components.
        /// </summary>
        public RelativePath Combine(PathAtom atom1, PathAtom atom2)
        {
            Contract.Requires(IsValid);
            Contract.Requires(atom1.IsValid);
            Contract.Requires(atom2.IsValid);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            var components = new StringId[Components.Length + 2];
            int count = 0;
            foreach (StringId component in Components)
            {
                components[count++] = component;
            }

            components[count++] = atom1.StringId;
            components[count] = atom2.StringId;

            return new RelativePath(components);
        }

        /// <summary>
        /// Extends a relative path with new path components.
        /// </summary>
        public RelativePath Combine(params PathAtom[] atoms)
        {
            Contract.Requires(IsValid);
            Contract.Requires(atoms != null);
            Contract.RequiresForAll(atoms, a => a.IsValid);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            var components = new StringId[Components.Length + atoms.Length];
            int count = 0;
            foreach (StringId component in Components)
            {
                components[count++] = component;
            }

            foreach (PathAtom a in atoms)
            {
                components[count++] = a.StringId;
            }

            return new RelativePath(components);
        }

        /// <summary>
        /// Concatenates a path atom to the end of a relative path.
        /// </summary>
        /// <remarks>
        /// The relative path may not be empty when calling this method.
        /// </remarks>
        public RelativePath Concat(StringTable table, PathAtom addition)
        {
            Contract.Requires(IsValid);
            Contract.Requires(table != null);
            Contract.Requires(addition.IsValid);
            Contract.Requires(!IsEmpty);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            StringId changed = new PathAtom(Components[Components.Length - 1]).Concat(table, addition).StringId;

            var components = new StringId[Components.Length];
            int count = 0;
            foreach (StringId component in Components)
            {
                components[count++] = component;
            }

            components[count - 1] = changed;
            return new RelativePath(components);
        }

        /// <summary>
        /// Changes the extension of a path.
        /// </summary>
        /// <returns>A new relative path with the applied extension.</returns>
        public RelativePath ChangeExtension(StringTable table, PathAtom extension)
        {
            Contract.Requires(IsValid);
            Contract.Requires(!IsEmpty);
            Contract.Requires(table != null);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            if (extension.IsValid)
            {
                StringId changed = new PathAtom(Components[Components.Length - 1]).ChangeExtension(table, extension).StringId;
                if (changed == Components[Components.Length - 1])
                {
                    // nothing changed
                    return this;
                }

                var components = new StringId[Components.Length];
                int count = 0;
                foreach (StringId component in Components)
                {
                    components[count++] = component;
                }

                components[count - 1] = changed;
                return new RelativePath(components);
            }

            return RemoveExtension(table);
        }

        /// <summary>
        /// Removes the extension of a relative path.
        /// </summary>
        /// <returns>A new relative path without the final extension.</returns>
        public RelativePath RemoveExtension(StringTable table)
        {
            Contract.Requires(IsValid);
            Contract.Requires(table != null);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            if (Components.Length == 0)
            {
                // nothing to do
                return this;
            }

            StringId changed = new PathAtom(Components[Components.Length - 1]).RemoveExtension(table).StringId;
            if (changed == Components[Components.Length - 1])
            {
                // nothing changed...
                return this;
            }

            var components = new StringId[Components.Length];
            int count = 0;
            foreach (StringId component in Components)
            {
                components[count++] = component;
            }

            components[count - 1] = changed;
            return new RelativePath(components);
        }

        /// <summary>
        /// Gets the relative root of a relative path.
        /// </summary>
        /// <returns>A new relative path containing the root portion of the original.</returns>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public RelativePath GetRelativeRoot()
        {
            Contract.Requires(IsValid);
            Contract.Requires(!IsEmpty);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            return new RelativePath(Components[0]);
        }

        /// <summary>
        /// Removes the last path component of this relative path.
        /// </summary>
        /// <remarks>
        /// The relative path may not be empty when calling this method.
        /// </remarks>
        public RelativePath GetParent()
        {
            Contract.Requires(IsValid);
            Contract.Requires(!IsEmpty);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            var components = new StringId[Components.Length - 1];
            for (int i = 0; i < Components.Length - 1; i++)
            {
                components[i] = Components[i];
            }

            return new RelativePath(components);
        }

        /// <summary>
        /// Returns the last component of the relative path.
        /// </summary>
        /// <remarks>
        /// The relative path may not be empty when calling this method.
        /// </remarks>
        public PathAtom GetName()
        {
            Contract.Requires(IsValid);
            Contract.Requires(!IsEmpty);
            Contract.Ensures(Contract.Result<PathAtom>().IsValid);

            return new PathAtom(Components[Components.Length - 1]);
        }

        /// <summary>
        /// Returns the last component of the relative path.
        /// </summary>
        /// <remarks>
        /// The relative path may not be empty when calling this method.
        /// </remarks>
        public PathAtom GetExtension(StringTable table)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);
            Contract.Requires(!IsEmpty);

            return GetName().GetExtension(table);
        }

        /// <summary>
        /// Indicates if this relative path and the one given represent the same underlying value.
        /// </summary>
        /// <remarks>
        /// Note that it is only meaningful to compare relative paths created against the same <see cref="StringTable" />.
        /// </remarks>
        public bool Equals(RelativePath other)
        {
            if (Components == null || other.Components == null)
            {
                return Components == null && other.Components == null;
            }

            if (Components.Length != other.Components.Length)
            {
                return false;
            }

            for (int i = 0; i < Components.Length; i++)
            {
                if (Components[i] != other.Components[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Performs a case insensitive comparison against another RelativePath
        /// </summary>
        /// <remarks>
        /// Note that it is only meaningful to compare PathAtoms created against the same StringTable.
        /// </remarks>
        public bool CaseInsensitiveEquals(StringTable stringTable, RelativePath other)
        {
            Contract.Requires(stringTable != null);

            if (Components == null || other.Components == null)
            {
                return Components == null && other.Components == null;
            }

            if (Components.Length != other.Components.Length)
            {
                return false;
            }

            for (int i = 0; i < Components.Length; i++)
            {
                if (!stringTable.CaseInsensitiveEqualityComparer.Equals(Components[i], other.Components[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Indicates if a given object is a RelativePath equal to this one. See <see cref="Equals(RelativePath)" />.
        /// </summary>
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            if (Components == null || Components.Length == 0)
            {
                return 0;
            }

            // good enough...
            return Components[0].GetHashCode();
        }

        /// <summary>
        /// Equality operator for two relative paths.
        /// </summary>
        public static bool operator ==(RelativePath left, RelativePath right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Inequality operator for two relative paths.
        /// </summary>
        public static bool operator !=(RelativePath left, RelativePath right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Returns a string representation of the relative path.
        /// </summary>
        /// <param name="table">The string table used when creating the RelativePath.</param>
        /// <param name="pathFormat">Optional override for path format.</param>
        public string ToString(StringTable table, PathFormat pathFormat = PathFormat.HostOs)
        {
            Contract.Requires(IsValid);
            Contract.Requires(table != null);
            Contract.Ensures(Contract.Result<string>() != null);

            var pathSeparator = PathFormatter.GetPathSeparator(pathFormat);

            using (var wrap = Pools.GetStringBuilder())
            {
                StringBuilder sb = wrap.Instance;

                foreach (StringId component in Components)
                {
                    if (sb.Length > 0)
                    {
                        sb.Append(pathSeparator);
                    }

                    table.CopyString(component, sb);
                }

                return sb.ToString();
            }
        }

        string IPathSegment.ToString(StringTable table, PathFormat pathFormat)
        {
            return ToString(table, pathFormat);
        }

#pragma warning disable 809

        /// <summary>
        /// Not available for RelativePath, throws an exception
        /// </summary>
        [Obsolete("Not suitable for RelativePath")]
        public override string ToString()
        {
            throw new NotImplementedException();
        }

#pragma warning restore 809

        /// <summary>
        /// Determines whether this instance has been properly initialized or is merely default(RelativePath).
        /// </summary>
        public bool IsValid => Components != null;

        /// <summary>
        /// Determines whether this instance is the empty path.
        /// </summary>
        public bool IsEmpty
        {
            get
            {
                Contract.Requires(IsValid);
                return Components.Length == 0;
            }
        }

        /// <summary>
        /// Returns the internal array of atoms representing this relative path.
        /// </summary>
        internal StringId[] Components { get; }

        /// <summary>
        /// Returns an array of PathAtom representing the relative path.
        /// </summary>
        public PathAtom[] GetAtoms()
        {
            var result = new PathAtom[Components.Length];
            for (int i = 0; i < Components.Length; i++)
            {
                result[i] = new PathAtom(Components[i]);
            }

            return result;
        }

        /// <summary>
        /// Explains the path error
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
            /// RelativePath does not allow for '..' to go out of the scope.
            /// </summary>
            FailureDueToDotDotOutOfScope,
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using static BuildXL.Utilities.FormattableStringEx;
using static BuildXL.Utilities.HierarchicalNameTable;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Represents an absolute file system path.
    /// </summary>
    [DebuggerTypeProxy(typeof(AbsolutePathDebuggerView))]
    [DebuggerDisplay("{ToDebuggerDisplay(),nq}")]

    // Note that [DebuggerDisplay] applies to this type, not the proxy. Applying it to the proxy doesn't work.
    public readonly struct AbsolutePath : IEquatable<AbsolutePath>, IImplicitPath
    {
        /// <summary>
        /// An invalid path.
        /// </summary>
        public static readonly AbsolutePath Invalid = new AbsolutePath(HierarchicalNameId.Invalid);

#pragma warning disable CA1051 // Do not declare visible instance fields
        /// <summary>
        /// Identifier of this path as understood by the owning path table.
        /// </summary>
        /// <remarks>
        /// AbsolutePaths are a simple mapping of a HierarchicalNameId.
        /// </remarks>
        public readonly HierarchicalNameId Value;
#pragma warning restore CA1051 // Do not declare visible instance fields

        /// <summary>
        /// Raw identifier of this entry as understood by the owning name table.
        /// </summary>
        public int RawValue => Value.Value;

        /// <summary>
        /// Creates an absolute path for some underlying HierarchicalNameId value.
        /// </summary>
        /// <remarks>
        /// Since the value must have some meaning to a path table, this constructor should primarily be called by PathTables.
        /// The only other reasonable usage would be for temporary serialization (e.g. to a child process).
        /// </remarks>
        public AbsolutePath(HierarchicalNameId value)
        {
            Value = value;
        }

        /// <summary>
        /// Creates an absolute path for some underlying integer value.
        /// </summary>
        /// <remarks>
        /// Since the value must have some meaning to a path table, this constructor should primarily be called by PathTables.
        /// The only other reasonable usage would be for temporary serialization (e.g. to a child process).
        /// </remarks>
        public AbsolutePath(int value)
        {
            Value = new HierarchicalNameId(value);
        }

        /// <summary>
        /// Try to create an absolute path from a string.
        /// </summary>
        /// <returns>Return false if the input path is not in a valid format.</returns>
        public static bool TryCreate(PathTable table, string absolutePath, out AbsolutePath result)
        {
            Contract.Requires(table != null);
            Contract.Requires(absolutePath != null);
            Contract.Ensures(Contract.Result<bool>() == Contract.ValueAtReturn(out result).IsValid);

            return TryCreate(table, (StringSegment)absolutePath, out result);
        }

        /// <summary>
        /// Try to create an absolute path from a string.
        /// </summary>
        /// <returns>Return false if the input path is not in a valid format.</returns>
        public static bool TryCreate(PathTable table, StringSegment absolutePath, out AbsolutePath result)
        {
            Contract.Requires(table != null);
            Contract.Ensures(Contract.Result<bool>() == Contract.ValueAtReturn(out result).IsValid);

            ParseResult parseResult = TryCreate(table, absolutePath, out result, out _);
            return parseResult == ParseResult.Success;
        }

        /// <summary>
        /// Tries to create an absolute path from a string.
        /// </summary>
        /// <returns>Returns the parser result indicating success, or what was wrong with the parsing.</returns>
        public static ParseResult TryCreate(PathTable table, string absolutePath, out AbsolutePath result, out int characterWithError)
        {
            Contract.Requires(table != null);
            Contract.Requires(absolutePath != null);
            Contract.Ensures((Contract.Result<ParseResult>() == ParseResult.Success) == Contract.ValueAtReturn(out result).IsValid);

            return TryCreate(table, (StringSegment) absolutePath, out result, out characterWithError);
        }

        /// <summary>
        /// Tries to create an absolute path from a string.
        /// </summary>
        /// <returns>Returns the parser result indicating success, or what was wrong with the parsing.</returns>
        public static ParseResult TryCreate(PathTable table, StringSegment absolutePath, out AbsolutePath result, out int characterWithError)
        {
            Contract.Requires(table != null);
            Contract.Ensures((Contract.Result<ParseResult>() == ParseResult.Success) == Contract.ValueAtReturn(out result).IsValid);

            // First we check to see if the path has previously been created and is cached
            if (table.InsertionCache.TryGetValue(absolutePath, out HierarchicalNameId id))
            {
                result = new AbsolutePath(id);
                characterWithError = -1;
                return ParseResult.Success;
            }

            // It isn't in the cache so we must try to add it to the table
            ParseResult parseRes = TryGetComponents(table, absolutePath, out StringId[] components, out characterWithError);
            if (parseRes == ParseResult.Success)
            {
                result = AddPathComponents(table, Invalid, components);
            }
            else
            {
                result = Invalid;
            }

            // Add it to the cache
            if (result.IsValid)
            {
                table.InsertionCache.AddItem(absolutePath, result.Value);
            }

            return parseRes;
        }

        /// <summary>
        /// Tries to break down an absolute path string into its constituent parts.
        /// </summary>
        private static ParseResult TryGetComponents(PathTable table, StringSegment absolutePath, out StringId[] components, out int characterWithError)
        {
            Contract.Requires(table != null);
            Contract.Ensures((Contract.Result<ParseResult>() == ParseResult.Success) == (Contract.ValueAtReturn(out components) != null));

            // Win32Nt: \\?\ or \??\ prefix
            // - Skips all canonicalization that one would expect from GetFullPathNameW (.. etc)
            // - Allows length > MAX_PATH (relevant for string-ifying but not parsing)
            // - Allows access to non-drive letter devices e.g. \??\pipe\foo or \??\nul
            // Device: \\.\ prefix
            // - Applies all canonicalization that one would expect from GetFullPathNameW (.. etc)
            // - Allows access to non-drive letter devices e.g. \\.\pipe\foo or \\.\nul
            // We support both types for parsing, but only when the path represented is a normal drive-letter path. We do not support device paths. like \\.\pipe\foo.
            int skipped = 0;
            AbsolutePathType pathType = ClassifyPath(absolutePath, out int prefixLength);
            bool shouldParseAsLocalDriveLetter = pathType == AbsolutePathType.LocalDriveLetter;

            if (pathType == AbsolutePathType.Win32NtPrefixed || pathType == AbsolutePathType.DevicePrefixed)
            {
                // A path like \\?\C:\foo or \\.\C:\foo can be treated just like C:\foo. We reparse as a local drive letter path (with the prefix skipped).
                // TODO: But in the case of \\?\C:\foo\..\bar or \\?\C:\foo\\\bar we need to consider preserving the semantics that GetFullPathNameW canonicalization does not apply.
                //       Otherwise, we could (a) lose this information in round-trips and (b) consider two paths equivalent when they do not give equivalent behavior (when handed to the kernel).
                //       or (c) consider two paths not equivalent when they point to the same place (\\?\C:\foo\..\bar and \\?\C:\bar might be equivalent - are .. and . reified in the directory?)
                //       (c) is perhaps the same issue as symlinks - when we ask the disk, it is always possible that two unequal paths point to the same final location.
                AbsolutePathType pathTypeIgnoringPrefix = ClassifyPath(CharSpan.Skip(absolutePath, prefixLength), out int secondPrefixLength);
                if (pathTypeIgnoringPrefix == AbsolutePathType.LocalDriveLetter)
                {
                    Contract.Assume(secondPrefixLength == 0, "Local drive letter paths do not have a prefix to remove.");
                    shouldParseAsLocalDriveLetter = true;
                    absolutePath = CharSpan.Skip(absolutePath, prefixLength);
                    skipped += prefixLength;
                }
                else
                {
                    // TODO: Maybe like UNC paths we need just insert components like "\\?\C:". But the algebra would be tricky - what if we add a component to \\?\C:
                    //       Presumably \\?\C: + dir == C:\dir yet then Parent(C:\dir) would be C:\. In short, we would need a way to encode \\?\C: vs. \\?\C:\ in the hierarchy.
                    characterWithError = 0;
                    components = null;
                    return ParseResult.DevicePathsNotSupported;
                }
            }

            if (shouldParseAsLocalDriveLetter)
            {
                // Here we handle classic absolute paths like C:\foo.txt, or a prefixed one like \\?\C:\foo.txt after \\?\ has been skipped (skipped > 0)
                // N.B. We intentionally do not allow C:dir. That actually means C: + (current working directory on C:) + dir, which is in fact an exotic *relative* path.
                RelativePath.ParseResult parseResult = RelativePath.TryCreateInternal(
                    table.StringTable,
                    CharSpan.Skip(absolutePath, OperatingSystemHelper.IsUnixOS ? prefixLength : 3),
                    out RelativePath relPath,
                    out characterWithError,
                    allowDotDotOutOfScope: true);
                if (parseResult == RelativePath.ParseResult.Success)
                {
                    components = new StringId[1 + relPath.Components.Length];

                    // On Windows based systems we use the drive letter and separator e.g. 'c:' as the first component or root, on Unix based systems we
                    // insert an empty string in the PathTable as there is no drive notion and handle the empty string
                    components[0] = OperatingSystemHelper.IsUnixOS ? table.StringTable.AddString(string.Empty) : table.StringTable.AddString(absolutePath.Subsegment(0, 2));
                    for (int i = 0; i < relPath.Components.Length; i++)
                    {
                        components[i + 1] = relPath.Components[i];
                    }

                    characterWithError = -1;
                    return ParseResult.Success;
                }

                components = null;
                characterWithError += 3 + skipped;

                return (parseResult == RelativePath.ParseResult.FailureDueToDotDotOutOfScope)
                    ? ParseResult.FailureDueToDotDotOutOfScope
                    : ParseResult.FailureDueToInvalidCharacter;
            }

            if (pathType == AbsolutePathType.UNC)
            {
                // here we handle UNC paths like \\srv\share\foo.txt
                RelativePath.ParseResult parseResult = RelativePath.TryCreateInternal(table.StringTable, CharSpan.Skip(absolutePath, 2), out RelativePath relPath, out characterWithError, allowDotDotOutOfScope: true);
                if (parseResult == RelativePath.ParseResult.Success)
                {
                    if (relPath.IsEmpty)
                    {
                        // edge case, happens when someone does \\srv\.. or some equivalent normalization
                        characterWithError = 0;
                        components = null;
                        return ParseResult.FailureDueToDotDotOutOfScope;
                    }

                    components = new StringId[relPath.Components.Length];
                    for (int i = 0; i < relPath.Components.Length; i++)
                    {
                        components[i] = relPath.Components[i];
                    }

                    components[0] = StringId.Create(table.StringTable, "\\\\" + table.StringTable.GetString(components[0]));

                    characterWithError = -1;
                    return ParseResult.Success;
                }

                components = null;
                characterWithError += 2 + skipped;

                return parseResult == RelativePath.ParseResult.FailureDueToDotDotOutOfScope
                    ? ParseResult.FailureDueToDotDotOutOfScope
                    : ParseResult.FailureDueToInvalidCharacter;
            }

            Contract.Assert(pathType == AbsolutePathType.Invalid);
            characterWithError = 0;
            components = null;
            return ParseResult.UnknownPathStyle;
        }

        /// <summary>
        /// Private helper method
        /// </summary>
        /// <returns>AbsolutePath of the path just added.</returns>
        private static AbsolutePath AddPathComponents(PathTable table, AbsolutePath parentPath, params StringId[] components)
        {
            Contract.Requires(table != null);
            Contract.Requires(components != null);
            Contract.RequiresForAll(components, id => id.IsValid);

            return new AbsolutePath(table.AddComponents(parentPath.Value, components));
        }

        /// <summary>
        /// Private helper method
        /// </summary>
        /// <returns>AbsolutePath of the path just added.</returns>
        private static AbsolutePath AddPathComponent(PathTable table, AbsolutePath parentPath, StringId component)
        {
            Contract.Requires(table != null);
            Contract.Requires(component.IsValid);

            return new AbsolutePath(table.AddComponent(parentPath.Value, component));
        }

        /// <summary>
        /// Creates an AbsolutePath from a string and abandons if the path is invalid.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded paths, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public static AbsolutePath Create(PathTable table, string absolutePath)
        {
            Contract.Requires(table != null);
            Contract.Requires(absolutePath != null);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return Create(table, (StringSegment)absolutePath);
        }

        /// <summary>
        /// Creates an AbsolutePath from a string and abandons if the path is invalid.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded paths, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public static AbsolutePath Create(PathTable table, StringSegment absolutePath)
        {
            Contract.Requires(table != null);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            if (!TryCreate(table, absolutePath, out AbsolutePath file))
            {
                Contract.Assert(false, I($"Invalid path '{absolutePath.ToString()}'"));
            }

            return file;
        }

        private enum AbsolutePathType
        {
            /// <summary>
            /// Not a valid type of absolute path - e.g. dir\subdir
            /// </summary>
            Invalid,

            /// <summary>
            /// E.g. C:\dir
            /// </summary>
            LocalDriveLetter,

            /// <summary>
            /// E.g. \\server\
            /// </summary>
            UNC,

            /// <summary>
            /// E.g. \\.\nul
            /// </summary>
            DevicePrefixed,

            /// <summary>
            /// E.g. \??\C:\dir
            /// </summary>
            Win32NtPrefixed,
        }

        [Pure]
        private static bool IsAbsolutePath<T>(T path)
            where T : struct, ICharSpan<T>
        {
            return ClassifyPath(path, out _) != AbsolutePathType.Invalid;
        }

        [Pure]
        private static AbsolutePathType ClassifyPath<T>(T path, out int prefixLength)
            where T : struct, ICharSpan<T>
        {
            // Absolute Unix paths always start with a a forward slash '/'
            if (OperatingSystemHelper.IsUnixOS && path.Length >= 1 && (path[0] == Path.VolumeSeparatorChar))
            {
                prefixLength = 0;
                return AbsolutePathType.LocalDriveLetter;
            }

            // Maybe a normal Win32 path like C:\foo
            if (path.Length >= 3
                && char.IsLetter(path[0])
                && path[1] == Path.VolumeSeparatorChar
                && (path[2] == Path.DirectorySeparatorChar || path[2] == Path.AltDirectorySeparatorChar))
            {
                prefixLength = 0;
                return AbsolutePathType.LocalDriveLetter;
            }

            if (path.Length >= 2 && (path[0] == '\\' || path[0] == '/'))
            {
                char path0 = path[0];

                // UNC or prefixed (\\?\, \??\, \\.\) path.
                // This also works when the separator is the forward slash.
                if (path.Length >= 4 && path[3] == path0)
                {
                    if (path[1] == path0)
                    {
                        if (path[2] == '?')
                        {
                            prefixLength = 4;
                            return AbsolutePathType.Win32NtPrefixed;
                        }
                        else if (path[2] == '.')
                        {
                            prefixLength = 4;
                            return AbsolutePathType.DevicePrefixed;
                        }
                    }
                    else if (path[1] == '?' && path[2] == '?')
                    {
                        prefixLength = 4;
                        return AbsolutePathType.Win32NtPrefixed;
                    }
                }

                if (path[1] == path0)
                {
                    prefixLength = 0;
                    return AbsolutePathType.UNC;
                }
            }

            prefixLength = 0;
            return AbsolutePathType.Invalid;
        }

        /// <summary>
        /// Adds a path that might be relative, abandons if the path is invalid.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded paths, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        /// <param name="table">The path table to use.</param>
        /// <param name="relativeOrAbsolutePath">The path to add. If absolute this will be the path returned, otherwise the relative path is tacked onto the end of the base path.</param>
        /// <returns>Final resulting absolute path.</returns>
        public AbsolutePath CreateRelative(PathTable table, string relativeOrAbsolutePath)
        {
            Contract.Requires(table != null);
            Contract.Requires(relativeOrAbsolutePath != null);
            Contract.Ensures(Contract.Result<AbsolutePath>() != AbsolutePath.Invalid);

            return CreateRelative(table, (StringSegment)relativeOrAbsolutePath);
        }

        /// <summary>
        /// Adds a path that might be relative, abandons if the path is invalid.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded paths, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        /// <param name="table">The path table to use.</param>
        /// <param name="relativeOrAbsolutePath">The path to add. If absolute this will be the path returned, otherwise the relative path is tacked onto the end of the base path.</param>
        /// <returns>Final resulting absolute path.</returns>
        public AbsolutePath CreateRelative(PathTable table, StringSegment relativeOrAbsolutePath)
        {
            Contract.Requires(table != null);
            Contract.Ensures(Contract.Result<AbsolutePath>() != AbsolutePath.Invalid);

            if (IsAbsolutePath(relativeOrAbsolutePath))
            {
                ParseResult parseRes = TryGetComponents(table, relativeOrAbsolutePath, out StringId[] components, out _);
                Contract.Assert(parseRes == ParseResult.Success);
                return AddPathComponents(table, Invalid, components);
            }
            else
            {
                RelativePath.ParseResult parseRes = RelativePath.TryCreateInternal(table.StringTable, relativeOrAbsolutePath, out RelativePath relPath, out _);
                Contract.Assert(parseRes == RelativePath.ParseResult.Success);

                return AddPathComponents(table, this, relPath.Components);
            }
        }

        /// <summary>
        /// Looks for a path in the table and returns it if found.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public static bool TryGet(PathTable table, StringSegment absolutePath, out AbsolutePath result)
        {
            Contract.Requires(table != null);
            Contract.Ensures(Contract.Result<bool>() == (Contract.ValueAtReturn(out result) != AbsolutePath.Invalid));

            // First we check to see if the path has previously been created and is cached
            if (table.InsertionCache.TryGetValue(absolutePath, out HierarchicalNameId id))
            {
                result = new AbsolutePath(id);
                return true;
            }

            ParseResult parseRes = TryGetComponents(table, absolutePath, out StringId[] components, out _);
            if (parseRes == ParseResult.Success)
            {
                bool b = table.TryGetName(components, out HierarchicalNameId nameId);
                if (b)
                {
                    result = new AbsolutePath(nameId);
                    table.InsertionCache.AddItem(absolutePath, result.Value);
                }
                else
                {
                    result = Invalid;
                }

                return b;
            }

            result = Invalid;
            return false;
        }

        /// <summary>
        /// Given a possible descendent path, which can be 'this' itself, returns a
        /// possibly empty relative path that represents traversal between the two paths,
        /// without any '..' backtracking.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        [Pure]
        public bool TryGetRelative(PathTable table, AbsolutePath proposedRelativePath, out RelativePath result)
        {
            Contract.Requires(table != null);
            Contract.Requires(proposedRelativePath.IsValid);

            bool b = table.TryExpandNameRelativeToAnother(Value, proposedRelativePath.Value, out string str);
            result = b ? RelativePath.Create(table.StringTable, (StringSegment) str) : RelativePath.Invalid;

            return b;
        }

        /// <summary>
        /// Given a descendant path (which can be 'this' itself), returns a
        /// (possibly empty) relative path that represents traversal between the two paths (without any .. backtracking).
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        [Pure]
        public string ExpandRelative(PathTable table, AbsolutePath descendantPath)
        {
            Contract.Requires(table != null);
            Contract.Requires(descendantPath.IsValid);

            bool succeeded = table.TryExpandNameRelativeToAnother(Value, descendantPath.Value, out string str);
            if (!succeeded)
            {
                Contract.Assert(false, I($"Given descendantPath '{descendantPath.ToString(table)}' value is not a descendant path of {ToString(table)}"));
            }
            return str;
        }

        /// <summary>
        /// Extends an absolute path with new path components.
        /// </summary>
        [Pure]
        public AbsolutePath Combine(PathTable table, RelativePath path)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);
            Contract.Requires(path.IsValid);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return AddPathComponents(table, this, path.Components);
        }

        /// <summary>
        /// Extends an absolute path with a new path components.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public AbsolutePath Combine(PathTable table, PathAtom atom)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);
            Contract.Requires(atom.IsValid);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return AddPathComponent(table, this, atom.StringId);
        }

        /// <summary>
        /// Extends an absolute path with a new path components.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public AbsolutePath Combine(PathTable table, string atom)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);
            Contract.Requires(!string.IsNullOrEmpty(atom));
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return Combine(table, PathAtom.Create(table.StringTable, atom));
        }

        /// <summary>
        /// Extends an absolute path with new path components.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public AbsolutePath Combine(PathTable table, PathAtom atom1, PathAtom atom2)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);
            Contract.Requires(atom1.IsValid);
            Contract.Requires(atom2.IsValid);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            AbsolutePath r1 = AddPathComponent(table, this, atom1.StringId);
            return AddPathComponent(table, r1, atom2.StringId);
        }

        /// <summary>
        /// Extends an absolute path with new path components.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public AbsolutePath Combine(PathTable table, params PathAtom[] atoms)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);
            Contract.Requires(atoms != null);
            Contract.RequiresForAll(atoms, a => a.IsValid);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            AbsolutePath absPath = this;
            for (int i = 0; i < atoms.Length; i++)
            {
                absPath = AddPathComponent(table, absPath, atoms[i].StringId);
            }

            return absPath;
        }

        /// <summary>
        /// Concatenates a path atom to the end of an absolute path.
        /// </summary>
        public AbsolutePath Concat(PathTable table, PathAtom addition)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);
            Contract.Requires(addition.IsValid);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            AbsolutePath parent = GetParent(table);
            PathAtom newName = GetName(table).Concat(table.StringTable, addition);
            return parent.Combine(table, newName);
        }

        /// <summary>
        /// Changes the extension of a path.
        /// </summary>
        /// <returns>A new absolute path with the applied extension.</returns>
        public AbsolutePath ChangeExtension(PathTable table, PathAtom extension)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            PathAtom newName = GetName(table).ChangeExtension(table.StringTable, extension);
            return GetParent(table).Combine(table, newName);
        }

        /// <summary>
        /// Removes the extension from an absolute path.
        /// </summary>
        /// <returns>A new absolute path without the final extension.</returns>
        public AbsolutePath RemoveExtension(PathTable table)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            PathAtom newName = GetName(table).RemoveExtension(table.StringTable);
            return GetParent(table).Combine(table, newName);
        }

        /// <summary>
        /// Gets the extension of an absolute path.
        /// </summary>
        /// <returns>A new path atom containing the extension, or PathAtom.Invalid if there was no extension.</returns>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public PathAtom GetExtension(PathTable table)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);

            return GetName(table).GetExtension(table.StringTable);
        }

        /// <summary>
        /// Gets the root portion of an absolute path.
        /// </summary>
        /// <returns>A new absolute path containing the root portion of the original.</returns>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public AbsolutePath GetRoot(PathTable table)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);

            // loop until we hit a root
            HierarchicalNameId node = Value;
            for (; ;)
            {
                var parent = table.GetContainer(node);
                if (!parent.IsValid)
                {
                    return new AbsolutePath(node);
                }

                node = parent;
            }
        }

        /// <summary>
        /// Removes the last path component of this AbsolutePath.
        /// </summary>
        /// <remarks>
        /// If the given path is a root, this method returns AbsolutePath.Invalid
        /// This method is thread-safe without the need for any locking.
        /// </remarks>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public AbsolutePath GetParent(PathTable table)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);

            return new AbsolutePath(table.GetContainer(Value));
        }

        /// <summary>
        /// Returns the last component of the AbsolutePath.
        /// </summary>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public PathAtom GetName(PathTable table)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);
            Contract.Ensures(Contract.Result<PathAtom>().IsValid);

            return new PathAtom(table.GetFinalComponent(Value));
        }

        /// <summary>
        /// Relocates an AbsolutePath from one subtree to another, changing the extension in the process.
        /// </summary>
        /// <param name="table">The path table to operate against.</param>
        /// <param name="source">The root of the tree to clone.</param>
        /// <param name="destination">The destination directory for the relocation.</param>
        /// <param name="newExtension">The new extension to apply with a leading period. If this is PathAtom.Invalid, then any existing extension is removed.</param>
        /// <returns>The relocated path.</returns>
        /// <remarks>
        /// This method is typically used to create paths in an output directory based on paths within the
        /// source directory. For example, consider this source file:
        ///
        ///     c:\MySourceDirectory\MyProject\a\foo.cpp
        ///
        /// Using this method to relocate the entry could result in the following output path being produced:
        ///
        ///     c:\MyObjectDirectory\MyProject\a\foo.obj
        ///
        /// In this example,
        /// The current absolute path would be "c:\MySourceDirectory\MyProject\a\foo.cpp",
        /// <paramref name="source" /> would be "c:\MySourceDirectory\MyProject",
        /// <paramref name="destination" /> would be "c:\MyObjectDirectory\MyProject",
        /// <paramref name="newExtension" /> would be ".obj".
        /// </remarks>
        public AbsolutePath Relocate(
            PathTable table,
            AbsolutePath source,
            AbsolutePath destination,
            PathAtom newExtension)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);
            Contract.Requires(source.IsValid);
            Contract.Requires(destination.IsValid);
            Contract.Requires(IsWithin(table, source));

            // figure out how many components from the source item to its containing directory
            int count = 0;
            for (AbsolutePath currentNode = this;
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

                        Contract.Assume(count > 0);
                        components[--count] = currentNode.GetName(table).StringId;
                    }

                    // change or remove the file extension of the last component
                    StringId componentId = components[components.Length - 1];
                    string component = table.StringTable.GetString(componentId);
                    int length = component.Length;
                    while (length > 0)
                    {
                        char ch = component[--length];
                        if (ch == '.')
                        {
                            break;
                        }
                    }

                    if (newExtension.IsValid)
                    {
                        // change extension
                        if (component[length] == '.')
                        {
                            // strip away the old extension and add the new one
                            components[components.Length - 1] = StringId.Create(table.StringTable, component.Remove(length) + newExtension.ToString(table.StringTable));
                        }
                        else
                        {
                            // there wasn't any extension in the old name, so just tack on the new extension
                            components[components.Length - 1] = table.StringTable.Concat(componentId, newExtension.StringId);
                        }
                    }
                    else
                    {
                        if (component[length] == '.')
                        {
                            // strip away the old extension
                            components[components.Length - 1] = table.StringTable.AddString(component.Subsegment(0, length));
                        }
                    }

                    // and record the new subtree
                    return AddPathComponents(table, destination, components);
                }

                count++;
            }

            // if we get here, it's because the current path is not under 'source' which shouldn't happen given the precondition
            Contract.Assume(false);
            return Invalid;
        }

        /// <summary>
        /// Relocates an AbsolutePath from one subtree to another.
        /// </summary>
        /// <param name="table">The path table to operate against.</param>
        /// <param name="source">The root of the tree to clone.</param>
        /// <param name="destination">The destination directory for the relocation.</param>
        /// <returns>The relocated path.</returns>
        /// <remarks>
        /// This method is typically used to create paths in the object directory based on paths within the
        /// source directory. For example, consider this source file:
        ///
        ///     c:\MySourceDirectory\MyProject\a\foo.jpg
        ///
        /// Using this method to relocate the entry could result in the following output path being produced:
        ///
        ///     c:\MyObjectDirectory\MyProject\a\foo.jpg
        ///
        /// In this example,
        /// The current absolute path would be "c:\MySourceDirectory\MyProject\a\foo.jpg",
        /// <paramref name="source" /> would be "c:\MySourceDirectory\MyProject",
        /// <paramref name="destination" /> would be "c:\MyObjectDirectory\MyProject".
        /// </remarks>
        public AbsolutePath Relocate(
            PathTable table,
            AbsolutePath source,
            AbsolutePath destination)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);
            Contract.Requires(source.IsValid);
            Contract.Requires(destination.IsValid);
            Contract.Requires(IsWithin(table, source));

            // figure out how many components from the source item to its containing directory
            int count = 0;
            for (AbsolutePath currentNode = this;
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

                        Contract.Assume(count > 0);
                        components[--count] = currentNode.GetName(table).StringId;
                    }

                    // and record the new subtree
                    return AddPathComponents(table, destination, components);
                }

                count++;
            }

            // If we get here, it's because the current path is not under 'source' which shouldn't happen given the precondition.
            Contract.Assume(false);
            return Invalid;
        }

        /// <summary>
        /// Relocates the name component of an AbsolutePath to a destination directory, changing the extension in the process.
        /// </summary>
        /// <param name="table">The path table to operate against.</param>
        /// <param name="destination">The destination directory for the relocation.</param>
        /// <param name="newExtension">The new file extension to apply with a leading period. If this is PathAtom.Invalid, then any existing extension is removed.</param>
        /// <returns>The relocated path.</returns>
        /// <remarks>
        /// This method is typically used to create paths in an output directory based on the final name of an existing path.
        /// For example, consider this source file:
        ///
        ///     c:\MySourceDirectory\MyProject\a\foo.cpp
        ///
        /// Using this method to relocate the entry could result in the following output path being produced:
        ///
        ///     c:\MyObjectDirectory\MyProject\foo.obj
        ///
        /// In this example,
        /// the current absolute path would be "c:\MySourceDirectory\MyProject\a\foo.cpp",
        /// <paramref name="destination" /> would be "c:\MyObjectDirectory\MyProject".
        /// <paramref name="newExtension" /> would be ".obj".
        /// </remarks>
        public AbsolutePath Relocate(
            PathTable table,
            AbsolutePath destination,
            PathAtom newExtension)
        {
            Contract.Requires(table != null);
            Contract.Requires(destination.IsValid);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return Relocate(table, GetParent(table), destination, newExtension);
        }

        /// <summary>
        /// Relocates the name component of an AbsolutePath to a destination directory.
        /// </summary>
        /// <param name="table">The path table to operate against.</param>
        /// <param name="destination">The destination directory for the relocation.</param>
        /// <returns>The relocated path.</returns>
        /// <remarks>
        /// This method is typically used to create paths in an output directory based on the final name of an existing path.
        /// For example, consider this source file:
        ///
        ///     c:\MySourceDirectory\MyProject\a\foo.jpg
        ///
        /// Using this method to relocate the entry could result in the following output path being produced:
        ///
        ///     c:\MyObjectDirectory\MyProject\foo.jpg
        ///
        /// In this example,
        /// The current absolute path would be "c:\MySourceDirectory\MyProject\a\foo.cpp",
        /// <paramref name="destination" /> would be "c:\MyObjectDirectory\MyProject".
        /// </remarks>
        public AbsolutePath Relocate(
            PathTable table,
            AbsolutePath destination)
        {
            Contract.Requires(table != null);
            Contract.Requires(destination.IsValid);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return Relocate(table, GetParent(table), destination);
        }

        /// <summary>
        /// Returns true if this file is exactly equal to the given directory (ignoring case),
        /// or if the file lies within the given directory.
        /// </summary>
        /// <remarks>
        /// For example, /// if tree is 'C:\', and path='C:\Windows', then the return value would
        /// be true.  But if tree is 'C:\Foo', and path is 'C:\Bar', then the
        /// return value is false.
        /// This method is thread-safe without the need for any locking.
        /// </remarks>
        [Pure]
        [SuppressMessage("Microsoft.Design", "CA1011")]
        public bool IsWithin(PathTable table, AbsolutePath potentialContainer)
        {
            Contract.Requires(table != null);
            Contract.Requires(IsValid);
            Contract.Requires(potentialContainer.IsValid);

            return table.IsWithin(potentialContainer.Value, Value);
        }

        /// <summary>
        /// Gets whether an absolute path is valid.
        /// </summary>
        /// <remarks>
        /// AbsolutePath is a structure whose default initial state is invalid.
        /// </remarks>
        [Pure]
        public bool IsValid => this != Invalid;

        /// <summary>
        /// Determines whether a particular character is valid within an absolute path.
        /// </summary>
        public static bool IsValidAbsolutePathChar(char value)
        {
            return RelativePath.IsValidRelativePathChar(value) || value == ':';
        }

        /// <summary>
        /// Compares the absolute paths based on the expanded forms without actually expanding the paths
        /// </summary>
        public int ExpandedCompareTo(PathTable table, AbsolutePath other)
        {
            return table.ExpandedPathComparer.Compare(this, other);
        }

        /// <summary>
        /// Indicates whether the current object is equal to another object of the same type.
        /// </summary>
        /// <returns>
        /// true if the current object is equal to the <paramref name="other" /> parameter; otherwise, false.
        /// </returns>
        /// <param name="other">An object to compare with this object.</param>
        /// <filterpriority>2</filterpriority>
        public bool Equals(AbsolutePath other)
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
        /// One strategy would be to reverse the bits on the rewrite count and bitwise or it with the absolute path so collisions
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
        public static bool operator ==(AbsolutePath left, AbsolutePath right)
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
        public static bool operator !=(AbsolutePath left, AbsolutePath right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Implicit conversion of FileArtifact to AbsolutePath.
        /// </summary>
        public static implicit operator AbsolutePath(FileArtifact file)
        {
            return file.Path;
        }

        /// <summary>
        /// Implicit conversion of DirectoryArtifact to AbsolutePath.
        /// </summary>
        public static implicit operator AbsolutePath(DirectoryArtifact directory)
        {
            return directory.Path;
        }

        /// <summary>
        /// Returns expanded form of the absolute path
        /// </summary>
        public ExpandedAbsolutePath Expand(PathTable pathTable, NameExpander nameExpander = null)
        {
            if (!IsValid)
            {
                return ExpandedAbsolutePath.Invalid;
            }

            return new ExpandedAbsolutePath(this, pathTable, nameExpander);
        }

        /// <summary>
        /// Returns a string representation of the absolute path.
        /// </summary>
        /// <param name="table">The path table used when creating the AbsolutePath.</param>
        /// <param name="pathFormat">Optional path format indicator.</param>
        /// <param name="nameExpander">the name expander to use for expanding path segments</param>
        [SuppressMessage("Microsoft.Design", "CA1011")]
        [Pure]
        public string ToString(PathTable table, PathFormat pathFormat = PathFormat.HostOs, NameExpander nameExpander = null)
        {
            Contract.Requires(table != null);
            Contract.Ensures(!string.IsNullOrEmpty(Contract.Result<string>()));

            if (!IsValid)
            {
                return "{Invalid}";
            }

            char separator = PathFormatter.GetPathSeparator(pathFormat);
            string result = table.ExpandName(Value, expander: nameExpander, separator: separator);

            // deal with the exceptional case of needing a \ after a drive name and skip this on Unix based systems if the PathFormat is HostOS
            if (pathFormat == PathFormat.HostOs && OperatingSystemHelper.IsUnixOS)
            {
                return result;
            }

            if (result.Length == 2)
            {
                result += separator;
            }

            return result;
        }

        /// <inheritdoc />
        public override string ToString()
        {
            if (this == Invalid)
            {
                return "{Invalid}";
            }

            return string.Format(CultureInfo.InvariantCulture, "{{Path (id: {0:x})}}", Value.Value);
        }

        /// <summary>
        /// Returns a string to be displayed as the debugger representation of this value.
        /// This string contains an expanded path when possible. See the comments in PathTable.cs
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1811:AvoidUncalledPrivateCode",
            Justification = "Nothing is private to the debugger.")]
        [ExcludeFromCodeCoverage]
        [Pure]
        internal string ToDebuggerDisplay()
        {
            if (this == Invalid)
            {
                return ToString();
            }

            PathTable owner = HierarchicalNameTable.DebugTryGetTableForId(Value) as PathTable ?? PathTable.DebugPathTable;
            return (owner == null)
                ? "{Unable to expand AbsolutePath; this may occur after the allocation of a large number of PathTables}"
                : string.Format(CultureInfo.InvariantCulture, "{{Path '{0}' (id: {1:x})}}", ToString(owner), Value);
        }

        /// <summary>
        /// Debugger type proxy for AbsolutePath. The properties of this type are shown in place of the single integer field of AbsolutePath.
        /// </summary>
        [ExcludeFromCodeCoverage]
        private sealed class AbsolutePathDebuggerView
        {
            /// <summary>
            /// Constructs a debug view from a normal AbsolutePath.
            /// </summary>
            /// <remarks>
            /// This constructor is required by the debugger.
            /// Consequently, Invalid AbsolutePaths are allowed.
            /// </remarks>
            public AbsolutePathDebuggerView(AbsolutePath absolutePath)
            {
                Id = absolutePath.Value;

                if (absolutePath == Invalid)
                {
                    OwningPathTable = null;
                    Path = null;
                }
                else
                {
                    OwningPathTable = HierarchicalNameTable.DebugTryGetTableForId(absolutePath.Value) as PathTable;
                    if (OwningPathTable != null)
                    {
                        Path = absolutePath.ToString(OwningPathTable);
                    }
                }
            }

            /// <summary>
            /// Path table which owns this ID and was used to expand it.
            /// </summary>
            /// <remarks>
            /// This may be null if the table could not be found.
            /// </remarks>
            private PathTable OwningPathTable { get; }

            /// <summary>
            /// Integer ID as relevant in the owning path table.
            /// </summary>
            private HierarchicalNameId Id { get; }

            /// <summary>
            /// Expanded path according to the owning path table.
            /// </summary>
            /// <remarks>
            /// This may be null if the owning path table was not found.
            /// </remarks>
            private string Path { get; }
        }

        /// <summary>
        /// Explains the path errors
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

            /// <summary>
            /// Though a path like \\?\C:\dir or \\.\C:\dir is supported,
            /// device paths like \\.\nul or \\?\C: are not.
            /// </summary>
            DevicePathsNotSupported,

            /// <summary>
            /// The path style was not recognized; the string is not an absolute path.
            /// </summary>
            UnknownPathStyle,
        }

        #region IImplicitPath Members

        /// <inheritdoc/>
        AbsolutePath IImplicitPath.Path => this;

        #endregion
    }
}

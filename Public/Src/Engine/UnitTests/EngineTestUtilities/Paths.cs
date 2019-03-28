// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace Test.BuildXL.TestUtilities
{
    /// <summary>
    /// Utility services to efficiently manipulate paths.
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1815:OverrideEqualsAndOperatorEqualsOnValueTypes")]
    public readonly struct Paths
    {
        private readonly PathTable m_pathTable;
        private readonly StringTable m_stringTable;

        /// <summary>
        /// Constructor
        /// </summary>
        public Paths(PathTable pathTable)
        {
            Contract.Requires(pathTable != null);

            m_pathTable = pathTable;
            m_stringTable = pathTable.StringTable;
        }

        /// <summary>
        /// Try to create a RelativePath from a root path and a given descendant path.
        /// </summary>
        /// <param name="root">the root path</param>
        /// <param name="path">the descendant path</param>
        /// <param name="result">the computed relative path</param>
        /// <returns>Return false if path is not relative to root.</returns>
        public bool TryCreateRootRelativePath(AbsolutePath root, AbsolutePath path, out RelativePath result)
        {
            return root.TryGetRelative(m_pathTable, path, out result);
        }

        /// <summary>
        /// Try to create an AbsolutePath from a string.
        /// </summary>
        /// <returns>Return false if the input path is not in a valid format.</returns>
        public bool TryCreateAbsolutePath(string absolutePath, out AbsolutePath result)
        {
            Contract.Requires(absolutePath != null);

            return AbsolutePath.TryCreate(m_pathTable, absolutePath, out result);
        }

        /// <summary>
        /// Try to create a RelativePath from a string.
        /// </summary>
        /// <returns>Return false if the input path is not in a valid format.</returns>
        public bool TryCreateRelativePath(string relativePath, out RelativePath result)
        {
            Contract.Requires(relativePath != null);

            return RelativePath.TryCreate(m_stringTable, relativePath, out result);
        }

        /// <summary>
        /// Creates an AbsolutePath from a string and abandons if the string is invalid.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded paths, don't use with any user input.
        /// </remarks>
        public AbsolutePath CreateAbsolutePath(string absolutePath)
        {
            Contract.Requires(absolutePath != null);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return AbsolutePath.Create(m_pathTable, absolutePath);
        }

        /// <summary>
        /// Creates an AbsolutePath by appending a relative path to an existing one.
        /// </summary>
        public AbsolutePath CreateAbsolutePath(AbsolutePath original, RelativePath path)
        {
            Contract.Requires(original.IsValid);
            Contract.Requires(path.IsValid);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return original.Combine(m_pathTable, path);
        }

        /// <summary>
        /// Creates an AbsolutePath by appending a path atom to an existing one.
        /// </summary>
        public AbsolutePath CreateAbsolutePath(AbsolutePath original, PathAtom atom)
        {
            Contract.Requires(original.IsValid);
            Contract.Requires(atom.IsValid);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return original.Combine(m_pathTable, atom);
        }

        /// <summary>
        /// Creates an AbsolutePath path by appending a path atom to an existing one.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded literals, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public AbsolutePath CreateAbsolutePath(AbsolutePath original, string atom)
        {
            Contract.Requires(original.IsValid);
            Contract.Requires(!string.IsNullOrEmpty(atom));
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return CreateAbsolutePath(original, CreatePathAtom(atom));
        }

        /// <summary>
        /// Creates an AbsolutePath by appending path atoms to an existing one.
        /// </summary>
        public AbsolutePath CreateAbsolutePath(AbsolutePath original, PathAtom atom1, PathAtom atom2)
        {
            Contract.Requires(original.IsValid);
            Contract.Requires(atom1.IsValid);
            Contract.Requires(atom2.IsValid);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return original.Combine(m_pathTable, atom1, atom2);
        }

        /// <summary>
        /// Creates an AbsolutePath by appending path atoms to an existing one.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded literals, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public AbsolutePath CreateAbsolutePath(AbsolutePath original, string atom1, string atom2)
        {
            Contract.Requires(original.IsValid);
            Contract.Requires(!string.IsNullOrEmpty(atom1));
            Contract.Requires(!string.IsNullOrEmpty(atom2));
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return CreateAbsolutePath(original, CreatePathAtom(atom1), CreatePathAtom(atom2));
        }

        /// <summary>
        /// Creates an AbsolutePath by appending path atoms to an existing one.
        /// </summary>
        public AbsolutePath CreateAbsolutePath(AbsolutePath original, params PathAtom[] atoms)
        {
            Contract.Requires(original.IsValid);
            Contract.Requires(atoms != null);
            Contract.RequiresForAll(atoms, a => a.IsValid);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return original.Combine(m_pathTable, atoms);
        }

        /// <summary>
        /// Creates an AbsolutePath by appending path atoms to an existing one.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded literals, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public AbsolutePath CreateAbsolutePath(AbsolutePath original, params string[] atoms)
        {
            Contract.Requires(original.IsValid);
            Contract.Requires(atoms != null);
            Contract.RequiresForAll(atoms, a => !string.IsNullOrEmpty(a));
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return CreateAbsolutePath(original, ToPathAtomArray(atoms));
        }

        /// <summary>
        /// Creates a RelativePath from a string and abandons if the string is invalid.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded paths, don't use with any user input.
        /// </remarks>
        public RelativePath CreateRelativePath(string relativePath)
        {
            Contract.Requires(relativePath != null);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            return RelativePath.Create(m_stringTable, relativePath);
        }

        /// <summary>
        /// Creates a RelativePath by appending a relative path to an existing one.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Symmetry with AbsolutePath methods.")]
        public RelativePath CreateRelativePath(RelativePath original, RelativePath path)
        {
            Contract.Requires(original.IsValid);
            Contract.Requires(path.IsValid);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            return original.Combine(path);
        }

        /// <summary>
        /// Creates a RelativePath by appending a path atom to an existing one.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Symmetry with AbsolutePath methods.")]
        public RelativePath CreateRelativePath(RelativePath original, PathAtom atom)
        {
            Contract.Requires(original.IsValid);
            Contract.Requires(atom.IsValid);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            return original.Combine(atom);
        }

        /// <summary>
        /// Creates a RelativePath by appending a path atom to an existing one.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded literals, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public RelativePath CreateRelativePath(RelativePath original, string atom)
        {
            Contract.Requires(original.IsValid);
            Contract.Requires(!string.IsNullOrEmpty(atom));
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            return CreateRelativePath(original, CreatePathAtom(atom));
        }

        /// <summary>
        /// Creates a RelativePath by appending path atoms to an existing one.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Symmetry with AbsolutePath methods.")]
        public RelativePath CreateRelativePath(RelativePath original, PathAtom atom1, PathAtom atom2)
        {
            Contract.Requires(original.IsValid);
            Contract.Requires(atom1.IsValid);
            Contract.Requires(atom2.IsValid);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            return original.Combine(atom1, atom2);
        }

        /// <summary>
        /// Creates a RelativePath by appending path atoms to an existing one.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded literals, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public RelativePath CreateRelativePath(RelativePath original, string atom1, string atom2)
        {
            Contract.Requires(original.IsValid);
            Contract.Requires(!string.IsNullOrEmpty(atom1));
            Contract.Requires(!string.IsNullOrEmpty(atom2));
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            return CreateRelativePath(original, CreatePathAtom(atom1), CreatePathAtom(atom2));
        }

        /// <summary>
        /// Creates a RelativePath by appending path atoms to an existing one.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Symmetry with AbsolutePath methods.")]
        public RelativePath CreateRelativePath(RelativePath original, params PathAtom[] atoms)
        {
            Contract.Requires(original.IsValid);
            Contract.Requires(atoms != null);
            Contract.RequiresForAll(atoms, a => a.IsValid);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            return original.Combine(atoms);
        }

        /// <summary>
        /// Creates a RelativePath by appending path atoms to an existing one.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded paths, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public RelativePath CreateRelativePath(RelativePath original, params string[] atoms)
        {
            Contract.Requires(original.IsValid);
            Contract.Requires(atoms != null);
            Contract.RequiresForAll(atoms, a => !string.IsNullOrEmpty(a));
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            return CreateRelativePath(original, ToPathAtomArray(atoms));
        }

        /// <summary>
        /// Creates a RelativePath combining together several path atoms.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Symmetry with AbsolutePath methods.")]
        public RelativePath CreateRelativePath(params PathAtom[] atoms)
        {
            Contract.Requires(atoms != null);
            Contract.RequiresForAll(atoms, a => a.IsValid);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            return RelativePath.Create(atoms);
        }

        /// <summary>
        /// Creates a RelativePath combining together several path atoms.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded paths, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public RelativePath CreateRelativePath(params string[] atoms)
        {
            Contract.Requires(atoms != null);
            Contract.RequiresForAll(atoms, a => !string.IsNullOrEmpty(a));
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            return RelativePath.Create(ToPathAtomArray(atoms));
        }

        /// <summary>
        /// Creates a FileArtifact from an absolute path to use as an input sourceFile in the scheduler
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
        public FileArtifact CreateSourceFile(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);

            return FileArtifact.CreateSourceFile(path);
        }

        /// <summary>
        /// Try to create a PathAtom from a string.
        /// </summary>
        /// <remarks>
        /// The rules for a valid path atom are that the input string may not
        /// be empty and must only contain characters reported as valid by <see cref="PathAtom.IsValidPathAtomChar" />.
        /// </remarks>
        public bool TryCreatePathAtom(string atom, out PathAtom result)
        {
            Contract.Requires(!string.IsNullOrEmpty(atom));

            return PathAtom.TryCreate(m_stringTable, atom, out result);
        }

        /// <summary>
        /// Create a PathAtom from a string and abandons if the string is invalid.
        /// </summary>
        /// <remarks>
        /// The rules for a valid path atom are that the input string may not
        /// be empty and must only contain characters reported as valid by <see cref="PathAtom.IsValidPathAtomChar" />.
        /// This is useful for hard-coded literals, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public PathAtom CreatePathAtom(string atom)
        {
            Contract.Requires(!string.IsNullOrEmpty(atom));
            Contract.Ensures(Contract.Result<PathAtom>().IsValid);

            return PathAtom.Create(m_stringTable, atom);
        }

        /// <summary>
        /// Gets the extension of a path atom including the leading dot. Ex: ".exe"
        /// </summary>
        /// <returns>A new path atom containing the extension, or PathAtom. Invalid if it didn't have an extension.</returns>
        public PathAtom GetExtension(PathAtom atom)
        {
            Contract.Requires(atom.IsValid);

            return atom.GetExtension(m_stringTable);
        }

        /// <summary>
        /// Gets the extension of an absolute path including the leading dot. Ex: ".exe"
        /// </summary>
        /// <returns>A new path atom containing the extension, or PathAtom. Invalid if it didn't have an extension.</returns>
        public PathAtom GetExtension(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);

            return path.GetExtension(m_pathTable);
        }

        /// <summary>
        /// Gets the extension of a relative path including the leading dot. Ex: ".exe"
        /// </summary>
        /// <returns>A new path atom containing the extension, or PathAtom. Invalid if it didn't have an extension.</returns>
        public PathAtom GetExtension(RelativePath path)
        {
            Contract.Requires(path.IsValid);

            return path.GetExtension(m_stringTable);
        }

        /// <summary>
        /// Gets the parent path of the given absolute path.
        /// </summary>
        public AbsolutePath GetParent(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return path.GetParent(m_pathTable);
        }

        /// <summary>
        /// Gets the root portion of the given absolute path.
        /// </summary>
        public AbsolutePath GetRoot(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return path.GetRoot(m_pathTable);
        }

        /// <summary>
        /// Extracts the final component of an absolute path.
        /// </summary>
        public PathAtom GetName(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            Contract.Ensures(Contract.Result<PathAtom>().IsValid);

            return path.GetName(m_pathTable);
        }

        /// <summary>
        /// Extracts the final component of a relative path.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic", Justification = "Symmetry with AbsolutePath methods.")]
        public PathAtom GetName(RelativePath path)
        {
            Contract.Requires(path.IsValid);
            Contract.Ensures(Contract.Result<PathAtom>().IsValid);

            return path.GetName();
        }

        /// <summary>
        /// Extracts the final component of an absolute path and strips off the final file extension.
        /// </summary>
        public PathAtom GetNameWithoutExtension(AbsolutePath path)
        {
            return RemoveExtension(GetName(path));
        }

        /// <summary>
        /// Extracts the final component of a relative path and strips off the final file extension.
        /// </summary>
        public PathAtom GetNameWithoutExtension(RelativePath path)
        {
            return RemoveExtension(GetName(path));
        }

        /// <summary>
        /// Adds or changes the file extension of an absolute path, returning a new absolute path.
        /// </summary>
        /// <param name="path">The original absolute path that may or may not have an extension.</param>
        /// <param name="extension">The new extension (this string must include a leading .). If this is PathAtom.Invalid then this method is equivalent to calling RemoveExtension instead.</param>
        public AbsolutePath ChangeExtension(AbsolutePath path, PathAtom extension)
        {
            Contract.Requires(path.IsValid);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return path.ChangeExtension(m_pathTable, extension);
        }

        /// <summary>
        /// Adds or changes the file extension of an absolute path, returning a new absolute path.
        /// </summary>
        /// <param name="path">The original absolute path that may or may not have an extension.</param>
        /// <param name="extension">The new extension (this string must include a leading .)</param>
        /// <remarks>
        /// This is useful for hard-coded extensions, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public AbsolutePath ChangeExtension(AbsolutePath path, string extension)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(!string.IsNullOrEmpty(extension));
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return ChangeExtension(path, CreatePathAtom(extension));
        }

        /// <summary>
        /// Adds or changes the file extension of a relative path, returning a new relative path.
        /// </summary>
        /// <param name="path">The original relative path that may or may not have an extension.</param>
        /// <param name="extension">The new extension (this string must include a leading .). If this is PathAtom.Invalid then this method is equivalent to calling RemoveExtension instead.</param>
        public RelativePath ChangeExtension(RelativePath path, PathAtom extension)
        {
            Contract.Requires(path.IsValid);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            return path.ChangeExtension(m_stringTable, extension);
        }

        /// <summary>
        /// Adds or changes the file extension of a relative path, returning a new relative path.
        /// </summary>
        /// <param name="path">The original relative path that may or may not have an extension.</param>
        /// <param name="extension">The new extension (this string must include a leading .)</param>
        /// <remarks>
        /// This is useful for hard-coded extensions, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public RelativePath ChangeExtension(RelativePath path, string extension)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(!string.IsNullOrEmpty(extension));
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            return ChangeExtension(path, CreatePathAtom(extension));
        }

        /// <summary>
        /// Adds or changes the file extension of a path atom, returning a new path atom.
        /// </summary>
        /// <param name="atom">The original atom that may or may not have an extension.</param>
        /// <param name="extension">The new extension (this string must include a leading .). If this is PathAtom.Invalid then this method is equivalent to calling RemoveExtension instead.</param>
        public PathAtom ChangeExtension(PathAtom atom, PathAtom extension)
        {
            Contract.Requires(atom.IsValid);
            Contract.Ensures(Contract.Result<PathAtom>().IsValid);

            return atom.ChangeExtension(m_stringTable, extension);
        }

        /// <summary>
        /// Adds or changes the file extension of a path atom, returning a new path atom.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded extensions, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        /// <param name="atom">The original atom that may or may not have an extension.</param>
        /// <param name="extension">The new extension (this string must include a leading .)</param>
        public PathAtom ChangeExtension(PathAtom atom, string extension)
        {
            Contract.Requires(atom.IsValid);
            Contract.Requires(!string.IsNullOrEmpty(extension));
            Contract.Ensures(Contract.Result<PathAtom>().IsValid);

            return ChangeExtension(atom, CreatePathAtom(extension));
        }

        /// <summary>
        /// Removes the final extension of an absolute path, returning a new absolute path.
        /// </summary>
        /// <remarks>
        /// This function is a nop if the absolute path has no extension.
        /// </remarks>
        public AbsolutePath RemoveExtension(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return path.RemoveExtension(m_pathTable);
        }

        /// <summary>
        /// Removes the final extension of a relative path, returning a new relative path.
        /// </summary>
        /// <remarks>
        /// This function is a nop if the relative path has no extension.
        /// </remarks>
        public RelativePath RemoveExtension(RelativePath path)
        {
            Contract.Requires(path.IsValid);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            return path.RemoveExtension(m_stringTable);
        }

        /// <summary>
        /// Removes the final extension of a path atom, returning a new atom.
        /// </summary>
        /// <remarks>
        /// This function is a nop if the atom has no extension.
        /// </remarks>
        public PathAtom RemoveExtension(PathAtom atom)
        {
            Contract.Requires(atom.IsValid);
            Contract.Ensures(Contract.Result<PathAtom>().IsValid);

            return atom.RemoveExtension(m_stringTable);
        }

        /// <summary>
        /// Concatenates a path atom to the end of an absolute path, returning a new absolute path.
        /// </summary>
        public AbsolutePath Concat(AbsolutePath path, PathAtom addition)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(addition.IsValid);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return path.Concat(m_pathTable, addition);
        }

        /// <summary>
        /// Concatenates a path atom to the end of an absolute path, returning a new absolute path.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded literals, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public AbsolutePath Concat(AbsolutePath path, string addition)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(!string.IsNullOrEmpty(addition));
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return Concat(path, CreatePathAtom(addition));
        }

        /// <summary>
        /// Concatenates a path atom to the end of a relative path, returning a new relative path.
        /// </summary>
        public RelativePath Concat(RelativePath path, PathAtom addition)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(addition.IsValid);
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            return path.Concat(m_stringTable, addition);
        }

        /// <summary>
        /// Concatenates a path atom to the end of a relative path, returning a new relative path.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded literals, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public RelativePath Concat(RelativePath path, string addition)
        {
            Contract.Requires(path.IsValid);
            Contract.Requires(!string.IsNullOrEmpty(addition));
            Contract.Ensures(Contract.Result<RelativePath>().IsValid);

            return Concat(path, CreatePathAtom(addition));
        }

        /// <summary>
        /// Concatenates two path atoms together, returning a new path atom.
        /// </summary>
        public PathAtom Concat(PathAtom atom, PathAtom addition)
        {
            Contract.Requires(atom.IsValid);
            Contract.Requires(addition.IsValid);
            Contract.Ensures(Contract.Result<PathAtom>().IsValid);

            return atom.Concat(m_stringTable, addition);
        }

        /// <summary>
        /// Concatenates two path atoms together, returning a new path atom.
        /// </summary>
        /// <remarks>
        /// This is useful for hard-coded literals, don't use with any user input since it will kill the process on bad format.
        /// </remarks>
        public PathAtom Concat(PathAtom atom, string addition)
        {
            Contract.Requires(atom.IsValid);
            Contract.Requires(!string.IsNullOrEmpty(addition));
            Contract.Ensures(Contract.Result<PathAtom>().IsValid);

            return Concat(atom, CreatePathAtom(addition));
        }

        /// <summary>
        /// Relocates an AbsolutePath from one subtree to another, changing the extension in the process.
        /// </summary>
        /// <param name="entry">The path to relocate.</param>
        /// <param name="source">The root of the tree to clone.</param>
        /// <param name="destination">The destination directory for the relocation.</param>
        /// <param name="newExtension">The new extension to apply with a leading period. If this is PathAtom.Invalid, then any existing extension is removed.</param>
        /// <returns>The relocated path.</returns>
        /// <remarks>
        /// This method is typically used to create paths in the object directory based on paths within the
        /// source directory. For example, consider this source file:
        ///
        ///     c:\MySourceDirectory\MyProject\a\foo.cpp
        ///
        /// Using this method to relocate the entry could result in the following output path being produced:
        ///
        ///     c:\MyObjectDirectory\MyProject\a\foo.obj
        ///
        /// In this example,
        /// <paramref name="entry"/> would be "c:\MySourceDirectory\MyProject\a\foo.cpp",
        /// <paramref name="source" /> would be "c:\MySourceDirectory\MyProject",
        /// <paramref name="destination" /> would be "c:\MyObjectDirectory\MyProject",
        /// <paramref name="newExtension" /> would be ".obj".
        /// </remarks>
        public AbsolutePath Relocate(
            AbsolutePath entry,
            AbsolutePath source,
            AbsolutePath destination,
            PathAtom newExtension)
        {
            Contract.Requires(entry.IsValid);
            Contract.Requires(source.IsValid);
            Contract.Requires(destination.IsValid);
            Contract.Requires(IsPathWithinPath(entry, source));
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return entry.Relocate(m_pathTable, source, destination, newExtension);
        }

        /// <summary>
        /// Relocates an AbsolutePath from one subtree to another.
        /// </summary>
        /// <param name="entry">The path to relocate.</param>
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
        /// <paramref name="entry"/> would be "c:\MySourceDirectory\MyProject\a\foo.jpg",
        /// <paramref name="source" /> would be "c:\MySourceDirectory\MyProject",
        /// <paramref name="destination" /> would be "c:\MyObjectDirectory\MyProject".
        /// </remarks>
        public AbsolutePath Relocate(
            AbsolutePath entry,
            AbsolutePath source,
            AbsolutePath destination)
        {
            Contract.Requires(entry.IsValid);
            Contract.Requires(source.IsValid);
            Contract.Requires(destination.IsValid);
            Contract.Requires(IsPathWithinPath(entry, source));
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return entry.Relocate(m_pathTable, source, destination);
        }

        /// <summary>
        /// Relocates the name component of an AbsolutePath to a destination directory, changing the extension in the process.
        /// </summary>
        /// <param name="entry">The file to relocate.</param>
        /// <param name="destination">The destination directory for the relocation.</param>
        /// <param name="newExtension">The new file extension to apply with a leading period. If this is PathAtom.Invalid, then any existing extension is removed.</param>
        /// <returns>The relocated path.</returns>
        /// <remarks>
        /// This method is typically used to create paths in the object directory based on the final name of an existing path.
        /// For example, consider this source file:
        ///
        ///     c:\MySourceDirectory\MyProject\a\foo.cpp
        ///
        /// Using this method to relocate the entry could result in the following output path being produced:
        ///
        ///     c:\MyObjectDirectory\MyProject\foo.obj
        ///
        /// In this example,
        /// <paramref name="entry"/> would be "c:\MySourceDirectory\MyProject\a\foo.cpp",
        /// <paramref name="destination" /> would be "c:\MyObjectDirectory\MyProject".
        /// <paramref name="newExtension" /> would be ".obj".
        /// </remarks>
        public AbsolutePath Relocate(
            AbsolutePath entry,
            AbsolutePath destination,
            PathAtom newExtension)
        {
            Contract.Requires(entry.IsValid);
            Contract.Requires(destination.IsValid);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return entry.Relocate(m_pathTable, destination, newExtension);
        }

        /// <summary>
        /// Relocates the name component of an AbsolutePath to a destination directory.
        /// </summary>
        /// <param name="entry">The file to relocate.</param>
        /// <param name="destination">The destination directory for the relocation.</param>
        /// <returns>The relocated path.</returns>
        /// <remarks>
        /// This method is typically used to create paths in the object directory based on the final name of an existing path.
        /// For example, consider this source file:
        ///
        ///     c:\MySourceDirectory\MyProject\a\foo.jpg
        ///
        /// Using this method to relocate the entry could result in the following output path being produced:
        ///
        ///     c:\MyObjectDirectory\MyProject\foo.jpg
        ///
        /// In this example,
        /// <paramref name="entry"/> would be "c:\MySourceDirectory\MyProject\a\foo.cpp",
        /// <paramref name="destination" /> would be "c:\MyObjectDirectory\MyProject".
        /// </remarks>
        public AbsolutePath Relocate(
            AbsolutePath entry,
            AbsolutePath destination)
        {
            Contract.Requires(entry.IsValid);
            Contract.Requires(destination.IsValid);
            Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);

            return entry.Relocate(m_pathTable, destination);
        }

        /// <summary>
        /// Returns true if the given absolute path lies within (under) another path.
        /// </summary>
        [Pure]
        public bool IsPathWithinPath(AbsolutePath path, AbsolutePath potentialContainer)
        {
            return path.IsWithin(m_pathTable, potentialContainer);
        }

        /// <summary>
        /// Gets the path to a file relative to the directory it is contained in. Caller should ensure file is in directory
        /// </summary>
        public RelativePath GetRelativePath(AbsolutePath container, AbsolutePath path)
        {
            Contract.Requires(IsPathWithinPath(path, container));

            var relPathFragments = new Stack<PathAtom>();
            relPathFragments.Push(path.GetName(m_pathTable));
            AbsolutePath current = path.GetParent(m_pathTable);
            while (current != container)
            {
                relPathFragments.Push(current.GetName(m_pathTable));
                current = current.GetParent(m_pathTable);
            }

            return RelativePath.Create(relPathFragments.ToArray());
        }

        /// <summary>
        /// Retrieve a string representation of an absolute path.
        /// </summary>
        /// <remarks>
        /// This method is intended for debugging purposes only. Exposing this method to users will lead to absolute paths
        /// leaking in textual form within the users code and potentially within a pip's command-line. When
        /// paths appear in text form, BuildXL isn't aware the path are present and thus cannot properly canonicalize the paths for
        /// use in the distributed cache.
        /// </remarks>
        public string Expand(AbsolutePath path)
        {
            Contract.Requires(path.IsValid);
            Contract.Ensures(Contract.Result<string>() != null);

            return path.ToString(m_pathTable);
        }

        /// <summary>
        /// Retrieve a string representation of a relative path.
        /// </summary>
        public string Expand(RelativePath path)
        {
            Contract.Requires(path.IsValid);
            Contract.Ensures(Contract.Result<string>() != null);

            return path.ToString(m_stringTable);
        }

        /// <summary>
        /// Retrieve a string representation of a path atom.
        /// </summary>
        public string Expand(PathAtom atom)
        {
            Contract.Requires(atom.IsValid);
            Contract.Ensures(!string.IsNullOrEmpty(Contract.Result<string>()));

            return atom.ToString(m_stringTable);
        }

        /// <summary>
        /// Performs a case insensitive comparison on two PathAtoms
        /// </summary>
        public bool CaseInsensitiveEquals(PathAtom atom1, PathAtom atom2)
        {
            return atom1.CaseInsensitiveEquals(m_stringTable, atom2);
        }

        /// <summary>
        /// Performs a case insensitive comparison on two RelativePaths
        /// </summary>
        public bool CaseInsensitiveEquals(RelativePath relativePath1, RelativePath relativePath2)
        {
            return relativePath1.CaseInsensitiveEquals(m_stringTable, relativePath2);
        }

        /// <summary>
        /// Case insensitive comparer for the underlying StringTable
        /// </summary>
        public IEqualityComparer<StringId> CaseInsensitiveComparer
        {
            get
            {
                return m_stringTable.CaseInsensitiveEqualityComparer;
            }
        }

        private PathAtom[] ToPathAtomArray(string[] strings)
        {
            var atoms = new PathAtom[strings.Length];
            for (int i = 0; i < strings.Length; i++)
            {
                atoms[i] = CreatePathAtom(strings[i]);
            }

            return atoms;
        }
    }
}

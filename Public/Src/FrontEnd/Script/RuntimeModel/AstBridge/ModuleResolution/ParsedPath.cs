// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// ParsedPath = Invalid | AbsolutePath | PackageRelative | (FileRelative+ParentCount)
    /// </summary>
    /// <remarks>
    /// This type represents one of three possible paths in DScript:
    /// 1. Absolute path, like 'c:/foo/boo.dsc'
    /// 2. Package/Config relative path, like '/foo/boo.dsc'
    /// 3. File relative path like './foo.dsc' or '../foo.dsc' (in latter case, ParentCount == 1)
    /// 4. Path fragment, like  '#foo.dsc' - not implemented yet!
    /// </remarks>
    [DebuggerDisplay("{ToString(), nq}")]
    public class ParsedPath : IEquatable<ParsedPath>
    {
        private readonly AbsolutePath m_absolutePath;
        private readonly RelativePath m_packageRelativePath;
        private readonly RelativePath m_fileRelativePath;
        private readonly int m_parentCount;

        private readonly PathTable m_pathTable;

        private ParsedPath()
        { }

        private ParsedPath(AbsolutePath absolutePath, PathTable pathTable)
        {
            m_absolutePath = absolutePath;
            m_pathTable = pathTable;
        }

        private ParsedPath(RelativePath packageRelativePath, RelativePath fileRelativePath, int parentCount, PathTable pathTable)
        {
            m_packageRelativePath = packageRelativePath;
            m_fileRelativePath = fileRelativePath;
            m_parentCount = parentCount;
            m_pathTable = pathTable;
        }

        /// <summary>
        /// Returns true when the instance is valid.
        /// </summary>
        public bool IsValid => m_absolutePath.IsValid || m_packageRelativePath.IsValid || m_fileRelativePath.IsValid;

        /// <summary>
        /// Returns true when the instance holds a value of absolute path.
        /// </summary>
        public bool IsAbsolutePath => m_absolutePath.IsValid;

        /// <summary>
        /// Returns true when the instance holds a value of package-relative path.
        /// </summary>
        public bool IsPackageRelative => m_packageRelativePath.IsValid;

        /// <summary>
        /// Returns true when the instance holds a value of file-relative path.
        /// </summary>
        public bool IsFileRelative => m_fileRelativePath.IsValid;

        /// <nodoc />
        public AbsolutePath Absolute
        {
            get
            {
                Contract.Requires(IsAbsolutePath);
                Contract.Ensures(Contract.Result<AbsolutePath>().IsValid);
                return m_absolutePath;
            }
        }

        /// <nodoc />
        public RelativePath PackageRelative
        {
            get
            {
                Contract.Requires(IsPackageRelative);
                Contract.Ensures(Contract.Result<RelativePath>().IsValid);
                return m_packageRelativePath;
            }
        }

        /// <summary>
        /// Returns relative part of the file-relative path.
        /// I.e. for '../../foo.dsc' this property will return 'foo.dsc'.
        /// </summary>
        public RelativePath FileRelative
        {
            get
            {
                Contract.Requires(IsFileRelative);
                Contract.Ensures(Contract.Result<RelativePath>().IsValid);
                return m_fileRelativePath;
            }
        }

        /// <summary>
        /// Returns a parent count for file relative path.
        /// I.e. for '../../foo.dsc' this property will return 2 and for './foo.dsc' - 0.
        /// </summary>
        public int ParentCount
        {
            get
            {
                Contract.Ensures(Contract.Result<int>() >= 0);
                return m_parentCount;
            }
        }

        /// <summary>
        /// Helper function for pattern-matching current instance to deal with different cases.
        /// </summary>
        public T Match<T>(
            Func<AbsolutePath, T> absolutePathHandler,
            Func<RelativePath, T> packageRelativePathHandler,
            Func<RelativePath, T> fileRelativePathHandler,
            Func<T> invalidPathHandler)
        {
            Contract.Requires(absolutePathHandler != null);
            Contract.Requires(packageRelativePathHandler != null);
            Contract.Requires(fileRelativePathHandler != null);
            Contract.Requires(invalidPathHandler != null);

            if (!IsValid)
            {
                return invalidPathHandler();
            }

            if (IsAbsolutePath)
            {
                return absolutePathHandler(Absolute);
            }

            if (IsPackageRelative)
            {
                return packageRelativePathHandler(PackageRelative);
            }

            if (IsFileRelative)
            {
                return fileRelativePathHandler(FileRelative);
            }

            Contract.Assert(false, "Unknown path kind");
            throw new InvalidOperationException("Unknown path kind");
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return Match(
                absolute => I($"Kind: Absolute, Value: {absolute.ToString(m_pathTable)}"),
                package => I($"Kind: Package Relative, Value: {package.ToString(m_pathTable.StringTable)}"),
                file => I($"Kind: File Relative, Value: {GetParentOffset(ParentCount)}{file.ToString(m_pathTable.StringTable)}"),
                () => "invalid");
        }

        private static string GetParentOffset(int parentCount)
        {
            return string.Join(string.Empty, Enumerable.Repeat("../", parentCount));
        }

        /// <summary>
        /// Creates an instance of invalid instance of the <see cref="ParsedPath"/>.
        /// </summary>
        public static ParsedPath Invalid()
        {
            return new ParsedPath();
        }

        /// <summary>
        /// Creates absolute path like 'c:/foo/bar.dsc'.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1719:ParameterNamesShouldNotMatchMemberNames")]
        public static ParsedPath AbsolutePath(AbsolutePath absolutePath, PathTable pathTable)
        {
            Contract.Requires(absolutePath.IsValid);
            return new ParsedPath(absolutePath, pathTable);
        }

        /// <summary>
        /// Creates package relative path like '/foo/bar.dsc'.
        /// </summary>
        public static ParsedPath PackageRelativePath(RelativePath relativePath, PathTable pathTable)
        {
            Contract.Requires(relativePath.IsValid);

            return new ParsedPath(packageRelativePath: relativePath, fileRelativePath: BuildXL.Utilities.RelativePath.Invalid, parentCount: 0, pathTable: pathTable);
        }

        /// <summary>
        /// Creates file path like 'foo/bar.dsc'.
        /// </summary>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Naming", "CA1719:ParameterNamesShouldNotMatchMemberNames")]
        public static ParsedPath FileRelativePath(RelativePath fileRelativePath, int parentCount, PathTable pathTable)
        {
            Contract.Requires(fileRelativePath.IsValid);
            Contract.Requires(parentCount >= 0);

            return new ParsedPath(packageRelativePath: RelativePath.Invalid, fileRelativePath: fileRelativePath, parentCount: parentCount, pathTable: pathTable);
        }

        /// <inheridoc />
        public bool Equals(ParsedPath other)
        {
            if (ReferenceEquals(null, other))
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return m_absolutePath.Equals(other.m_absolutePath) && m_packageRelativePath.Equals(other.m_packageRelativePath) &&
                   m_fileRelativePath.Equals(other.m_fileRelativePath) && m_parentCount == other.m_parentCount;
        }

        /// <inheridoc />
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return Equals((ParsedPath)obj);
        }

        /// <inheridoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(
                m_absolutePath.GetHashCode(),
                m_packageRelativePath.GetHashCode(),
                m_fileRelativePath.GetHashCode(),
                m_parentCount);
        }
    }
}

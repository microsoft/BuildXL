// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using BuildXL.Cache.ContentStore.Interfaces.Utils;

namespace BuildXL.Cache.ContentStore.Interfaces.FileSystem
{
    /// <summary>
    /// An path that is guaranteed to be absolute.
    /// </summary>
    /// <remarks>
    /// Long path support story.
    /// For Windows platform the constructor of this type automatically adds a long path prefix if the current version of .NET Framework supports long paths.
    /// But the constructor doesn't fail if the current platform does not support long paths and a given path is longer then the max short path.
    /// This is done mostly for backward compatibility purposes and the responsibility to fail an IO operation is still on the client of this type.
    /// For instance, PassThroughFileSystem calls <see cref="ThrowIfPathTooLong"/> method for all the public operations to show a human readable message when the path is too long.
    /// Please note, that the <see cref="PathBase.Path"/> property doesn't fail with <see cref="PathTooLongException"/> for the same reason: backward compatibility.
    /// </remarks>
    public class AbsolutePath : PathBase, IEquatable<AbsolutePath>
    {
        /// <summary>
        /// <see cref="FileSystemConstants.LongPathsSupported"/>.
        /// </summary>
        public static bool LongPathsSupported => FileSystemConstants.LongPathsSupported;

        /// <summary>
        /// Minimum path length.
        /// The value is platform agnostic.
        /// </summary>
        public static int MinimumPathLength = OperatingSystemHelper.IsWindowsOS ? 2 : 1;

        /// <summary>
        ///     Initializes a new instance of the <see cref="AbsolutePath" /> class.
        /// </summary>
        public AbsolutePath(string path)
        {
            if (path == null)
            {
                throw new ArgumentNullException(nameof(path), "cannot be null");
            }

            if (path.Length < MinimumPathLength)
            {
                throw new ArgumentException("does not meet minimum length", nameof(path));
            }

            (Path, IsLocal, IsRoot) = Initialize(path);
        }

        /// <inheritdoc />
        protected override PathBase Create<T>(string path)
        {
            return new AbsolutePath(path);
        }

        /// <summary>
        /// Create path to a randomly named file inside a given directory.
        /// </summary>
        public static AbsolutePath CreateRandomFileName(AbsolutePath directory)
        {
            // Don't use Path.GetRandomFileName(), it's not random enough when running multi-threaded.
            return directory / ("random-" + Guid.NewGuid().ToString("N").Substring(0, 12));
        }

        /// <summary>
        ///     Gets a value indicating whether the path is a drive root.
        /// </summary>
        public bool IsRoot { get; }

        /// <summary>
        ///     Gets a value indicating whether the path is a local one.
        /// </summary>
        public bool IsLocal { get; }

        /// <summary>
        ///     Gets a value indicating whether the path is UNC.
        /// </summary>
        public bool IsUnc => !IsLocal;

        /// <summary>
        /// Returns a drive letter for the current path.
        /// </summary>
        public char DriveLetter
        {
            get
            {
                if (IsLocal)
                {
                    return GetPathWithoutLongPathPrefix()[0];
                }

                throw new InvalidOperationException("Cannot get a drive letter because the path is not a local path.");
            }
        }

        /// <summary>
        /// Returns true if the path starts with a long path prefix.
        /// </summary>
        public bool HasLongPathPrefix => OperatingSystemHelper.IsWindowsOS ? Path[0] == '\\' : false;

        /// <inheritdoc />
        public override int Length => HasLongPathPrefix ? Path.Length - FileSystemConstants.LongPathPrefix.Length : Path.Length;

        /// <summary>
        /// Returns true if the current path is longer then a maximum short path.
        /// </summary>
        public bool IsLongPath => Length >= FileSystemConstants.MaxShortPath;

        /// <summary>
        ///     Gets get parent path.
        /// </summary>
        /// <remarks>
        /// The result can be null.
        /// </remarks>
        public AbsolutePath Parent
        {
            get
            {
                string parent;
                try
                {
                    parent = System.IO.Path.GetDirectoryName(Path);
                }
                catch (PathTooLongException e)
                {
                    throw PathTooLongException(Path, e);
                }
                catch (ArgumentException e) when (IsIllegalCharactersInPathError(e))
                {
                    throw CreateIllegalCharactersInPathError(Path);
                }

#if !PLATFORM_WIN
                // CoreCLR's GetDirectoryName doesn't throw PathTooLongException for long paths
                if (Path.Length > FileSystemConstants.MaxPath)
                {
                    throw new PathTooLongException(PathTooLongExceptionMessage(Path));
                }
#endif

                if (string.IsNullOrEmpty(parent))
                {
                    return null;
                }

                return new AbsolutePath(parent);
            }
        }

        /// <summary>
        /// Removes leading long path prefix from the <paramref name="path"/> if necessary.
        /// </summary>
        public static string RemoveLongPathPrefixIfNeeded(string path)
        {
            Contract.Requires(path != null);

            if (!OperatingSystemHelper.IsWindowsOS)
            {
                return path;
            }

            if (path.StartsWith(FileSystemConstants.LongPathPrefix))
            {
                return path.Remove(0, FileSystemConstants.LongPathPrefix.Length);
            }

            return path;
        }

        private static (string path, bool isLocal, bool isRoot) Initialize(string path)
        {
            // The argument may be constructed from a ToString call of another AbsolutePath instance.
            // To keep the logic simple, we just remove a long path prefix.
            path = RemoveLongPathPrefixIfNeeded(path);

            var isLocal = IsLocalAbsoluteCalculated(path);
            var isUnc = IsUncCalculated(path);
            int startIndex = 2;

            if (!OperatingSystemHelper.IsWindowsOS)
            {
                startIndex = 1;
            }

            var segments = ParseSegments(path.Substring(startIndex), false);
            bool isRoot = false;

            if (isLocal)
            {
                isRoot = segments.Count == 0;
            }
            else if (isUnc)
            {
                if (segments.Count <= 1)
                {
                    throw new ArgumentException("UNC path is missing directory", nameof(path));
                }

                isRoot = segments.Count == 2;
            }
            else
            {
                throw new ArgumentException(
                    string.Format(CultureInfo.CurrentCulture, "path [{0}] is neither an absolute local or UNC path", path));
            }

            string resultingPath;
            if (OperatingSystemHelper.IsWindowsOS)
            {
                // Getting 'd:' or '/' part of the path depending on whether the path is local or not.
                path = path.Substring(0, isLocal ? 2 : 1) + System.IO.Path.DirectorySeparatorChar + string.Join(System.IO.Path.DirectorySeparatorChar + "", segments);
                resultingPath = ToLongPathIfNeeded(path);
            }
            else
            {
                resultingPath = System.IO.Path.DirectorySeparatorChar + string.Join(System.IO.Path.DirectorySeparatorChar + "", segments);
            }

            return (resultingPath, isLocal, isRoot);
        }

        /// <summary>
        /// Throws <see cref="PathTooLongException"/> if the current path is longer (or equals) then max short path and the target platform does not support long paths.
        /// </summary>
        public void ThrowIfPathTooLong([CallerMemberName] string caller = null)
        {
            if (OperatingSystemHelper.IsWindowsOS && IsLongPath && !AbsolutePath.LongPathsSupported)
            {
                string message = $"Failed to {caller}. {PathTooLongExceptionMessage(Path)}";
                throw new PathTooLongException(message);
            }
        }

        internal IReadOnlyList<string> GetSegments()
        {
            return ParseSegments(Path, false);
        }

        internal static PathTooLongException PathTooLongException(string path, Exception inner) => new PathTooLongException(PathTooLongExceptionMessage(path), inner);

        internal static string PathTooLongExceptionMessage(string path) => $"The specified path '{path}' is too long ({path.Length} characters). The fully qualified file name must be less than {FileSystemConstants.MaxPath} characters, and the directory name must be less than {FileSystemConstants.MaxPath - 12} characters.";

        /// <summary>
        /// Returns a path with a long path prefix if the given path exceeds a short max path length.
        /// </summary>
        public static string ToLongPathIfNeeded(string path)
        {
            Contract.Requires(path != null);

            if (path.Length < FileSystemConstants.MaxDirectoryPath)
            {
                return path;
            }

            if (OperatingSystemHelper.IsWindowsOS && FileSystemConstants.LongPathsSupported)
            {
                if (!path.StartsWith(FileSystemConstants.LongPathPrefix))
                {
                    path = FileSystemConstants.LongPathPrefix + path;
                }
            }

            return path;
        }

        /// <summary>
        /// Returns a path without a long file prefix.
        /// </summary>
        public string GetPathWithoutLongPathPrefix() => RemoveLongPathPrefixIfNeeded(Path);

        /// <inheritdoc />
        public bool Equals(AbsolutePath other)
        {
            return base.Equals(other);
        }

        /// <inheritdoc />
        protected override PathBase GetParentPathBase()
        {
            return Parent;
        }

        /// <summary>
        ///     Concatenate paths.
        /// </summary>
        public static AbsolutePath operator /(AbsolutePath left, RelativePath right)
        {
            Contract.Requires(left != null);
            Contract.Requires(right != null);

            return left / right.Path;
        }

        /// <summary>
        ///     Concatenate paths.
        /// </summary>
        public static AbsolutePath operator /(AbsolutePath left, string right)
        {
            Contract.Requires(left != null);
            Contract.Requires(right != null);
            try
            {
                return new AbsolutePath(System.IO.Path.Combine(left.Path, right));
            }
            catch (ArgumentException e) when (IsIllegalCharactersInPathError(e))
            {
                throw CreateIllegalCharactersInPathError(right);
            }
        }
    }
}

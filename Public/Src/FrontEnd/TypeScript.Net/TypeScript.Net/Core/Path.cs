// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Linq;
using TypeScript.Net.Extensions;
using TypeScript.Net.Scanning;
using TypeScript.Net.Types;

namespace TypeScript.Net.Core
{
    /// <summary>
    /// Abstract notion of path.
    /// </summary>
    /// <remarks>
    /// Current design is pretty naive and should be improved.
    /// </remarks>
    public readonly struct Path : IEquatable<Path>
    {
        private const string DirectorySeparator = "/";

        /// <summary>
        ///  List of supported extensions in order of file resolution precedence.
        /// </summary>
        private static readonly string[] s_supportedTypeScriptExtensions = { ".ts", ".tsx", ".d.ts" };

        private static readonly string[] s_supportedJavascriptExtensions = { ".cs", ".jsx" };

        private static readonly string[] s_allSupportedExtensions = s_supportedTypeScriptExtensions.Concatenate(s_supportedJavascriptExtensions);

        private static readonly string[] s_extensionsToRemove = { ".d.ts", ".ts", ".js", ".tsx", ".jsx" };

        private readonly string m_relativePath;

        internal Path(string relativePath)
        {
            m_relativePath = relativePath;
        }

        /// <summary>
        /// Creates instance of a relative path.
        /// </summary>
        public static Path Relative(string relativePath)
        {
            Contract.Requires(relativePath != null);
            return new Path(relativePath);
        }

        /// <summary>
        /// Creates instance of an absolute path.
        /// </summary>
        public static Path Absolute(string absolutePath)
        {
            Contract.Requires(!string.IsNullOrEmpty(absolutePath));
            return new Path(absolutePath);
        }

        /// <summary>
        /// Returns an absolute path.
        /// </summary>
        public string AbsolutePath => m_relativePath;

        /// <summary>
        /// Returns true if the path is rooted.
        /// </summary>
        public bool IsRoot => System.IO.Path.GetPathRoot(m_relativePath) == m_relativePath;

        /// <inheritdoc />
        public bool Equals(Path other)
        {
            return string.Equals(m_relativePath, other.m_relativePath);
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            if (obj.GetType() != GetType())
            {
                return false;
            }

            return Equals((Path)obj);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return m_relativePath.GetHashCode();
        }

        /// <inheritdoc />
        public override string ToString()
        {
            return m_relativePath;
        }

        /// <nodoc />
        public static bool operator ==(Path left, Path right)
        {
            return Equals(left, right);
        }

        /// <nodoc />
        public static bool operator !=(Path left, Path right)
        {
            return !Equals(left, right);
        }

        /// <nodoc />
        public static string RemoveFileExtension(string path)
        {
            foreach (var ext in s_extensionsToRemove)
            {
                if (FileExtensionIs(path, ext))
                {
                    return path.Substr(0, path.Length - ext.Length);
                }
            }

            return path;
        }

        /// <nodoc />
        public static string NormalizeSlashes(string path)
        {
            return path.Replace("\\", "/");
        }

        /// <nodoc />
        public static int GetRootLength(string path)
        {
            if (path.CharCodeAt(0) == CharacterCodes.Slash)
            {
                if (path.CharCodeAt(1) != CharacterCodes.Slash)
                {
                    return 1;
                }

                var p1 = path.IndexOf("/", 2, StringComparison.CurrentCulture);
                if (p1 < 0)
                {
                    return 2;
                }

                var p2 = path.IndexOf("/", p1 + 1, StringComparison.CurrentCulture);
                if (p2 < 0)
                {
                    return p1 + 1;
                }

                return p2 + 1;
            }

            if (path.CharCodeAt(1) == CharacterCodes.Colon)
            {
                if (path.CharCodeAt(2) == CharacterCodes.Slash)
                {
                    return 3;
                }

                return 2;
            }

            // Per RFC 1738 'file' URI schema has the shape file://<host>/<path>
            // if <host> is omitted then it is assumed that host value is 'localhost',
            // however slash after the omitted <host> is not removed.
            // file:///folder1/file1 - this is a correct URI
            // file://folder2/file2 - this is an incorrect URI
            if (path.LastIndexOf("file:///", 0, StringComparison.CurrentCulture) == 0)
            {
                return "file:///".Length;
            }

            var idx = path.IndexOf("://", StringComparison.CurrentCulture);
            if (idx != -1)
            {
                return idx + "://".Length;
            }

            return 0;
        }

        /// <nodoc />
        public static string NormalizePath(string path)
        {
            path = NormalizeSlashes(path);
            var rootLength = GetRootLength(path);
            var normalized = GetNormalizedParts(path, rootLength);
            return path.Substring(0, rootLength) + normalized.Join(DirectorySeparator);
        }

        /// <nodoc />
        public static string GetDirectoryPath(string path)
        {
            return path.Substring(
                0,
                Math.Max(GetRootLength(path), path.LastIndexOf(DirectorySeparator, StringComparison.CurrentCulture)));
        }

        /// <nodoc />
        public static bool IsUrl(string path)
        {
            return path != null && !IsRootedDiskPath(path) && path.IndexOf("://", StringComparison.CurrentCulture) != -1;
        }

        /// <nodoc />
        public static bool IsRootedDiskPath(string path)
        {
            return GetRootLength(path) != 0;
        }

        /// <nodoc />
        public static string[] GetNormalizedPathComponents(string path, string currentDirectory)
        {
            path = NormalizeSlashes(path);
            var rootLength = GetRootLength(path);
            if (rootLength == 0)
            {
                // If the path is not rooted it is relative to current directory
                path = CombinePaths(NormalizeSlashes(currentDirectory), path);
                rootLength = GetRootLength(path);
            }

            return NormalizedPathComponents(path, rootLength);
        }

        /// <nodoc />
        public static string GetNormalizedAbsolutePath(string fileName, string currentDirectory)
        {
            return GetNormalizedPathFromPathComponents(GetNormalizedPathComponents(fileName, currentDirectory));
        }

        /// <nodoc />
        public static string GetNormalizedPathFromPathComponents(string[] pathComponents)
        {
            if (pathComponents != null && pathComponents.Length != 0)
            {
                return pathComponents[0] + pathComponents.Slice(1).Join(DirectorySeparator);
            }

            return null;
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Design", "CA1055:UriReturnValuesShouldNotBeStrings")]
        public static string GetRelativePathToDirectoryOrUrl(string directoryPathOrUrl, string relativeOrAbsolutePath,
            string currentDirectory, Func<string, string> getCanonicalFileName, bool isAbsolutePathAnUrl)
        {
            var pathComponents = GetNormalizedPathOrUrlComponents(relativeOrAbsolutePath, currentDirectory);
            var directoryComponents = GetNormalizedPathOrUrlComponents(directoryPathOrUrl, currentDirectory).ToList();
            if (directoryComponents.Count > 1 && string.IsNullOrEmpty(directoryComponents.LastOrDefault()))
            {
                // If the directory path given was of type test/cases/ then we really need components of directory to be only till its name
                // that is  ["test", "cases", ""] needs to be actually ["test", "cases"]
                directoryComponents.Pop();
            }

            // Find the component that differs
            var joinStartIndex = 0;
            for (joinStartIndex = 0;
                joinStartIndex < pathComponents.Length && joinStartIndex < directoryComponents.Count;
                joinStartIndex++)
            {
                if (getCanonicalFileName(directoryComponents[joinStartIndex]) !=
                    getCanonicalFileName(pathComponents[joinStartIndex]))
                {
                    break;
                }
            }

            // Get the relative path
            if (joinStartIndex != 0)
            {
                var relativePath = string.Empty;
                var relativePathComponents = pathComponents.Slice(joinStartIndex, pathComponents.Length);
                for (; joinStartIndex < directoryComponents.Count; joinStartIndex++)
                {
                    if (!string.IsNullOrEmpty(directoryComponents[joinStartIndex]))
                    {
                        relativePath = relativePath + ".." + DirectorySeparator;
                    }
                }

                return relativePath + relativePathComponents.Join(DirectorySeparator);
            }

            // Cant find the relative path, get the absolute path
            var absolutePath = GetNormalizedPathFromPathComponents(pathComponents);
            if (isAbsolutePathAnUrl && IsRootedDiskPath(absolutePath))
            {
                absolutePath = "file:///" + absolutePath;
            }

            return absolutePath;
        }

        /// <nodoc />
        public static string GetBaseFileName(string path)
        {
            if (path == null)
            {
                return null;
            }

            var i = path.LastIndexOf(DirectorySeparator, StringComparison.Ordinal);
            return i < 0 ? path : path.Substring(i + 1);
        }

        /// <nodoc />
        public static string CombinePaths(string path1, string path2)
        {
            if (string.IsNullOrEmpty(path1))
            {
                return path2;
            }

            if (string.IsNullOrEmpty(path2))
            {
                return path1;
            }

            if (GetRootLength(path2) != 0)
            {
                return path2;
            }

            if (path1.CharAt(path1.Length - 1) == DirectorySeparator)
            {
                return path1 + path2;
            }

            return path1 + DirectorySeparator + path2;
        }

        /// <nodoc />
        public static bool FileExtensionIs(string path, string extension)
        {
            var pathLen = path.Length;
            var extLen = extension.Length;
            return pathLen > extLen && path.Substr(pathLen - extLen, extLen) == extension;
        }

        /// <nodoc />
        public static string[] GetSupportedExtensions(Optional<ICompilerOptions> options)
        {
            return options.HasValue && options.Value.AllowJs.ValueOrDefault
                ? s_allSupportedExtensions
                : s_supportedTypeScriptExtensions;
        }

        /// <nodoc />
        public static bool IsSupportedSourceFileName(string fileName, Optional<ICompilerOptions> compilerOptions)
        {
            if (fileName == null)
            {
                return false;
            }

            foreach (var extension in GetSupportedExtensions(compilerOptions))
            {
                if (FileExtensionIs(fileName, extension))
                {
                    return true;
                }
            }

            return false;
        }

        /// <nodoc />
        public static bool HasJavaScriptFileExtension(string filename)
        {
            if (string.IsNullOrEmpty(filename))
            {
                return false;
            }

            var extension = System.IO.Path.GetExtension(filename);
            return s_supportedJavascriptExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Creates absolute path with a <paramref name="basePath"/> if current path is not absolute.
        /// </summary>
        public Path MakeAbsoluteIfNeeded(Path basePath)
        {
            if (System.IO.Path.IsPathRooted(m_relativePath))
            {
                return this;
            }

            return basePath.Combine(m_relativePath);
        }

        /// <nodoc />
        public Path Combine(string fileName)
        {
            return Absolute(System.IO.Path.Combine(AbsolutePath, fileName));
        }

        /// <nodoc />
        public Path GetDirectoryName()
        {
            return Absolute(System.IO.Path.GetDirectoryName(AbsolutePath));
        }

        /// <nodoc />
        public Path Parent()
        {
            // TODO: check!
            return Absolute(System.IO.Path.GetDirectoryName(AbsolutePath));
        }

        private static string[] GetNormalizedParts(string normalizedSlashedPath, int rootLength)
        {
            var parts = normalizedSlashedPath.Substring(rootLength)
                .Split(new string[] { DirectorySeparator }, StringSplitOptions.RemoveEmptyEntries);
            var normalized = new List<string>();
            foreach (var part in parts)
            {
                if (part != ".")
                {
                    if (part == ".." && normalized.Count > 0 && normalized.ToArray().LastOrDefault() != "..")
                    {
                        normalized.Pop();
                    }
                    else
                    {
                        // A part may be an empty string (which is 'falsy') if the path had consecutive slashes,
                        // e.g. "path//file.ts".  Drop these before re-joining the parts.
                        if (part != null)
                        {
                            normalized.Add(part);
                        }
                    }
                }
            }

            return normalized.ToArray();
        }

        private static string[] NormalizedPathComponents(string path, int rootLength)
        {
            var normalizedParts = GetNormalizedParts(path, rootLength);
            return new List<string> { path.Substring(0, rootLength) }.Union(normalizedParts).ToArray();
        }

        private static string[] GetNormalizedPathComponentsOfUrl(string url)
        {
            // Get root length of http://www.website.com/folder1/foler2/
            // In this example the root is:  http://www.website.com/
            // normalized path components should be ["http://www.website.com/", "folder1", "folder2"]
            var urlLength = url.Length;

            // Initial root length is http:// part
            var rootLength = url.IndexOf("://", StringComparison.CurrentCulture) + "://".Length;
            while (rootLength < urlLength)
            {
                // Consume all immediate slashes in the protocol
                // eg.initial rootlength is just file:// but it needs to consume another "/" in file:///
                if (url.CharCodeAt(rootLength) == CharacterCodes.Slash)
                {
                    rootLength++;
                }
                else
                {
                    // non slash character means we continue proceeding to next component of root search
                    break;
                }
            }

            // there are no parts after http:// just return current string as the pathComponent
            if (rootLength == urlLength)
            {
                return new string[] { url };
            }

            // Find the index of "/" after website.com so the root can be http://www.website.com/ (from existing http://)
            var indexOfNextSlash = url.IndexOf(DirectorySeparator, rootLength, StringComparison.CurrentCulture);
            if (indexOfNextSlash != -1)
            {
                // Found the "/" after the website.com so the root is length of http://www.website.com/
                // and get components afetr the root normally like any other folder components
                rootLength = indexOfNextSlash + 1;
                return NormalizedPathComponents(url, rootLength);
            }

            // Can't find the host assume the rest of the string as component
            // but make sure we append "/"  to it as root is not joined using "/"
            // eg. if url passed in was http://website.com we want to use root as [http://website.com/]
            // so that other path manipulations will be correct and it can be merged with relative paths correctly
            return new[] { url + DirectorySeparator };
        }

        private static string[] GetNormalizedPathOrUrlComponents(string pathOrUrl, string currentDirectory)
        {
            if (IsUrl(pathOrUrl))
            {
                return GetNormalizedPathComponentsOfUrl(pathOrUrl);
            }

            return GetNormalizedPathComponents(pathOrUrl, currentDirectory);
        }
    }
}

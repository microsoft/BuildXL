// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;

namespace BuildXL.Ide.Generator.Old
{
    /// <summary>
    /// Utility methods for paths and Uris (including relativizing and derelativizing)
    /// </summary>
    internal static class PathUtilities
    {
        /// <summary>
        /// Indicates that a relative path is same as the base path
        /// </summary>
        public const string CurrentDirectoryRelativePath = @"./";

        /// <summary>
        /// Relativize the file URI with respect to the <paramref name="baseUri"/> if it is under <paramref name="commonRoot"/>
        /// Non-file URI are returned unchanged.
        /// </summary>
        /// <param name="uriToRelativize">the URI to relativize</param>
        /// <param name="baseUri">the base URI</param>
        /// <param name="commonRoot">the common root path of <paramref name="baseUri"/> and <paramref name="uriToRelativize"/> under
        /// which relative paths are valid.</param>
        /// <returns>the relative URI if the URI is under the root, otherwise returns the input URI as a string. If
        /// <paramref name="uriToRelativize"/> is a file in <paramref name="baseUri"/>, the <see cref="CurrentDirectoryRelativePath"/>
        /// is returned. </returns>
        /// <remarks>
        /// <paramref name="baseUri"/> should be the path to a file or a folder path (path ending in directory separator character)
        ///
        /// <paramref name="commonRoot"/> should be used in cases where the <paramref name="baseUri"/> can be made relative to all
        /// paths under a common directory but not relative to paths outside of <paramref name="commonRoot"/>.
        ///
        /// Case:
        /// <paramref name="commonRoot"/> = "C:\Solution\"
        /// <paramref name="baseUri"/> = "C:\Solution\Proj\Foo.module"
        /// <paramref name="uriToRelativize"/> = "C:\Other\ModuleStore"
        ///
        /// In this case: the relative path to <paramref name="uriToRelativize"/> (..\..\Other\ModuleStore) would not be
        /// valid if the C:\Solution\ folder was moved, so it should be treated as an absolute path.
        /// </remarks>
        public static string ToRelativizedUri(Uri uriToRelativize, Uri baseUri, Uri commonRoot = null)
        {
            return RelativizeUriHelper(uriToRelativize, new Uri(baseUri.GetLeftPart(UriPartial.Path)), commonRoot);
        }

        private static string RelativizeUriHelper(Uri uriToRelativize, Uri baseUri, Uri commonRoot)
        {
            if (!uriToRelativize.IsAbsoluteUri || !uriToRelativize.IsFile // only absolute file URIs can be relativized
                || !baseUri.IsAbsoluteUri || !baseUri.IsFile /* base URI must be absolute file URI
                                                                common Root must be the base of both URIs */
                || (commonRoot != null && (!commonRoot.IsBaseOrEquals(baseUri) || !commonRoot.IsBaseOrEquals(uriToRelativize))))
            {
                return uriToRelativize.OriginalString;
            }

            // If the URIs are equal and baseUri is a file,
            // then we return the file name
            if (uriToRelativize.Equals(baseUri) && !baseUri.IsDirectory())
            {
                return Path.GetFileName(baseUri.ToString());
            }

            // If the two uri's represent the same directory or baseUri is a file inside of uriToRelativize then return the current directory specifier
            if (ToDirectoryUri(uriToRelativize).Equals(ToDirectoryUri(baseUri)) ||
                uriToRelativize.IsParentUriOf(baseUri))
            {
                return CurrentDirectoryRelativePath;
            }

            var relativeUriString = baseUri.MakeRelativeUri(uriToRelativize).ToString();
            if (baseUri.IsFile)
            {
                return TryMakeFilePath(relativeUriString);
            }

            return relativeUriString;
        }

        private static bool IsParentUriOf(this Uri candidateParentUri, Uri uri)
        {
            var parent = uri.AbsoluteUri.Remove(uri.AbsoluteUri.Length - uri.Segments.Last().Length);
            Uri parentUri;
            if (Uri.TryCreate(parent, UriKind.Absolute, out parentUri))
            {
                return parentUri.Equals(ToDirectoryUri(candidateParentUri));
            }

            return false;
        }

        private static bool IsBaseOrEquals(this Uri candidateBaseUri, Uri uri)
        {
            return candidateBaseUri.Equals(uri) || ToDirectoryUri(candidateBaseUri).IsBaseOf(uri);
        }

        private static bool IsDirectory(this Uri uri)
        {
            var baseUriString = uri.GetLeftPart(UriPartial.Path);
            return baseUriString.Length != baseUriString.TrimEnd(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Length;
        }

        /// <summary>
        /// Formats the URI so that it is treated as a directory
        /// </summary>
        [SuppressMessage("Microsoft.Globalization", "CA1307")]
        private static Uri ToDirectoryUri(Uri baseUri)
        {
            if (baseUri == null)
            {
                return null;
            }

            var baseUriString = baseUri.GetLeftPart(UriPartial.Path);
            if (!baseUriString.EndsWith(Path.AltDirectorySeparatorChar.ToString()))
            {
                // base URI should be treated as folder so append directory separator
                baseUri = new Uri(baseUriString + Path.AltDirectorySeparatorChar);
            }

            return baseUri;
        }

        /// <summary>
        /// Relativize the uri with respect to the root if it is under the baseUri
        /// </summary>
        /// <param name="uriToRelativize">the uri to relativize</param>
        /// <param name="baseUri">the base URI</param>
        /// <param name="commonRoot">the common root path of <paramref name="baseUri"/> and <paramref name="uriToRelativize"/> under
        /// which relative paths are valid.</param>
        /// <returns>the relative uri if the uri is under the root, otherwise returns the input URI as a string</returns>
        public static string Relativize(string uriToRelativize, string baseUri, string commonRoot = null)
        {
            if (uriToRelativize == null)
            {
                return null;
            }

            var commonRootUri = commonRoot != null ? new Uri(commonRoot) : null;
            return ToRelativizedUri(new Uri(uriToRelativize, UriKind.RelativeOrAbsolute), new Uri(baseUri), commonRootUri);
        }

        /// <summary>
        /// Returns the file URI string as a file path.
        /// The value returned by this property is unescaped. All forward slashes (/) are replaced by backward slashes (\).
        /// </summary>
        /// <param name="fileUriString">the file URI</param>
        /// <returns>The uri as a file path</returns>
        internal static string TryMakeFilePath(string fileUriString)
        {
            var result = Uri.UnescapeDataString(fileUriString.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar));
            return result;
        }

        /// <summary>
        /// Ensures that the path has a trailing slash at the end
        /// </summary>
        /// <param name="path">the path</param>
        /// <returns>the path ending with a trailing slash</returns>
        public static string EnsureTrailingSlash(string path)
        {
            return EnsureTrailingSlash(path, Path.DirectorySeparatorChar);
        }

        /// <summary>
        /// Ensures that the path has a trailing slash at the end
        /// </summary>
        /// <param name="path">the path</param>
        /// <param name="separatorChar">the trailing separator char</param>
        /// <returns>the path ending with a trailing slash</returns>
        public static string EnsureTrailingSlash(string path, char separatorChar)
        {
            if (path[path.Length - 1] != separatorChar)
            {
                return path + separatorChar;
            }

            return path;
        }
    }
}

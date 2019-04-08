// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using BuildXL.Utilities;
using BuildXL.FrontEnd.Workspaces.Core;
using TypeScript.Net.Types;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Ide.LanguageServer
{
    /// <summary>
    /// Utilities to convert between URI and various path-like primitives.
    /// </summary>
    public static class UriExtensions
    {
        /// <summary>
        /// Returns the string representation of a URI
        /// </summary>
        public static string ToAbsolutePath(this Uri uri)
        {
            // This is required to handle paths coming from both the VS Code client and the VS client.
            // Example of a Uri.LocalPath from the VS Code LSP client:
            //   "/d:/Repos/BuildXL"
            // Example of a Uri.LocalPath from the VS LSP client:
            //   "D:\Repos\BuildXL"
            // Don't strip the initial "/" if we are on Unix based systems as absolute paths start like that
            return (uri.LocalPath.StartsWith("/") && !OperatingSystemHelper.IsUnixOS)
                ? uri.LocalPath.Substring(1)
                : uri.LocalPath;
        }

        /// <summary>
        /// Gets AbsolutePath representation of URI.
        /// </summary>
        public static AbsolutePath ToAbsolutePath(this Uri uri, PathTable pathTable)
        {
            return AbsolutePath.Create(pathTable, uri.ToAbsolutePath());
        }

        /// <summary>
        /// Gets AbsolutePath representation of a string representation of a URI.
        /// </summary>
        public static AbsolutePath ToAbsolutePath(this string uriAsString, PathTable pathTable)
        {
            var uriFromString = new Uri(uriAsString);
            return uriFromString.ToAbsolutePath(pathTable);
        }

        /// <summary>
        /// Gets URI for a SourceFile's path.
        /// </summary>
        public static Uri ToUri(this ISourceFile sourceFile)
        {
            return GetUriFromPath(sourceFile.FileName);
        }

        /// <summary>
        /// Gets SourceFile contained in <paramref name="workspace"/> corresponding to this <paramref name="uri"/>
        /// </summary>
        /// <remarks>
        /// The source file can be null if you open a file from another build cone
        /// that is NOT part of the known workspace. For example, simply open up a build prject (bp) file from
        /// another repository while having a workspace open from another repository.
        /// You also hit this on "bc", "module.config.bm" and "bl" files as they are not part
        /// of the workspace.
        /// </remarks>
        public static bool TryGetSourceFile(this Uri uri, Workspace workspace, PathTable pathTable, out ISourceFile sourceFile)
        {
            // we use the PathTable to unify potential differences in slashes
            var pathToFile = uri.ToAbsolutePath(pathTable);

            return workspace.TryGetSourceFile(pathToFile, out sourceFile);
        }

        /// <summary>
        /// Gets SourceFile contained in <paramref name="workspace"/> corresponding to this <paramref name="uriAsString"/>
        /// </summary>
        /// <remarks>
        /// The source file can be null if you open a file from another build cone
        /// that is NOT part of the known workspace. For example, simply open up a build prject (bp) file from
        /// another repository while having a workspace open from another repository.
        /// You also hit this on "bc", "module.config.bm" and "bl" files as they are not part
        /// of the workspace.
        /// </remarks>
        public static bool TryGetSourceFile(this string uriAsString, Workspace workspace, PathTable pathTable, out ISourceFile sourceFile)
        {
            var uriFromString = new Uri(uriAsString);
            return uriFromString.TryGetSourceFile(workspace, pathTable, out sourceFile);
        }

        /// <summary>
        /// Gets URI representation of file path.
        /// </summary>
        /// <remarks>
        /// TODO: Find a standardized way to do this.
        /// TODO: This is taken from https://stackoverflow.com/questions/1546419/convert-file-path-to-a-file-uri
        /// </remarks>
        public static Uri GetUriFromPath(string filePath)
        {
            StringBuilder uriString = new StringBuilder();
            foreach (char v in filePath)
            {
                if ((v >= 'a' && v <= 'z') || (v >= 'A' && v <= 'Z') || (v >= '0' && v <= '9') ||
                  v == '+' || v == '/' || v == ':' || v == '.' || v == '-' || v == '_' || v == '~' ||
                  v > '\xFF')
                {
                    uriString.Append(v);
                }
                else if (v == Path.DirectorySeparatorChar || v == Path.AltDirectorySeparatorChar)
                {
                    uriString.Append('/');
                }
                else
                {
                    uriString.Append(string.Format(CultureInfo.InvariantCulture, "%{0:X2}", (int)v));
                }
            }

            if (uriString.Length >= 2 && uriString[0] == '/' && uriString[1] == '/')
            {
                // UNC path
                uriString.Insert(0, "file:");
            }
            else if (uriString[0] == '/' && OperatingSystemHelper.IsUnixOS)
            {
                // Absolute Unix paths start with '/' and the first symbol must be included in the file:// uriString too
                // e.g. file:///Users/testUser/file1.txt
                uriString.Insert(0, "file://");
            }
            else
            {
                uriString.Insert(0, "file:///");
            }

            if (!Uri.TryCreate(uriString.ToString(), UriKind.Absolute, out var uri))
            {
                Debug.Fail(I($"Failed to create Uri for '{filePath}"));
            }

            return uri;
        }
    }
}

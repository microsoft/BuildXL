// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Literals;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    // TODO: this is a good candidate for moving outside the parser!

    /// <summary>
    /// Modul resolver that follows new syntax.
    /// In this case there is no difference between single or double quotes!
    /// import * as X from "PackageName"; // Resolve by package name
    /// import * as Y from "/configspecific.dsc"; // Resolve by config/package relative path
    /// import * as Z from "./local.dsc"; // resolve by file relative path
    /// </summary>
    internal static class ImportPathHelpers
    {
        internal static readonly char[] InvalidPathChars = Path.GetInvalidPathChars();
        internal static readonly string InvalidPathCharsText = string.Join(", ", InvalidPathChars);
        internal static readonly char[] InvalidPackageChars = InvalidPathChars.Concat(new[] { '/', '\\' }).ToArray();

        internal static Expression ResolvePath(RuntimeModelContext context, AbsolutePath specPath, string path, in UniversalLocation location)
        {
            Contract.Requires(!IsPackageName(path));

            var parser = new PathResolver(context.PathTable);
            var parsedPath = parser.ParseRegularPath(path);
            return ResolveProjectImport(context, specPath, path, parsedPath, location);
        }

        /// <summary>
        /// A string is a package name if does not contain any invalid path characters, is not rooted (e.g. 'c:/foo.ds'), and it does not begin with '/' or '.'.
        /// </summary>
        /// <returns>Whether the given string is a package name.</returns>
        internal static bool IsPackageName(string moduleName)
        {
            return moduleName.IndexOfAny(InvalidPackageChars) == -1
                && !System.IO.Path.IsPathRooted(moduleName)
                && moduleName[0] != '.';
        }

        /// <summary>
        /// Resolves project by file absolute, relative or package relative path.
        /// </summary>
        private static PathLiteral ResolveProjectImport(RuntimeModelContext context, AbsolutePath specPath, string moduleName, ParsedPath parsedPath, in UniversalLocation location)
        {
            if (!parsedPath.IsValid)
            {
                context.Logger.ReportProjectPathIsInvalid(context.LoggingContext, location.AsLoggingLocation(), moduleName);
                return null;
            }

            if (parsedPath.IsAbsolutePath)
            {
                return new ResolvedStringLiteral(parsedPath.Absolute, moduleName, location);
            }

            // Current design is a bit weird, because PathLiteral should store absolute path,
            // but relative path (config-relative) is required for scheduling parsing of the next file!
            RelativePath projectRelativePath = GetRelativePathAndPathExpression(context, specPath, parsedPath, location, out PathLiteral pathLiteral);

            if (!projectRelativePath.IsValid)
            {
                // Errors are already logged
                return null;
            }

            return pathLiteral;
        }

        private static RelativePath GetRelativePathAndPathExpression(RuntimeModelContext context, AbsolutePath specPath, ParsedPath parsedPath, in UniversalLocation location, out PathLiteral pathExpression)
        {
            Contract.Requires(!parsedPath.IsAbsolutePath);
            pathExpression = null;

            // Package relative: /foo/boo.dsc
            if (parsedPath.IsPackageRelative)
            {
                pathExpression = new PathLiteral(
                    context.RootPath.Combine(context.PathTable, parsedPath.PackageRelative),
                    location.AsLineInfo());
                return parsedPath.PackageRelative;
            }

            // File relative: folder/proj.dsc or ../../folder/proj.dsc
            if (parsedPath.IsFileRelative)
            {
                var specFolder = specPath.GetParent(context.PathTable);
                var projectAbsolutePath = ComputeAbsolutePath(context, specFolder, parsedPath.FileRelative, parsedPath.ParentCount);

                if (!projectAbsolutePath.IsValid)
                {
                    context.Logger.ReportProjectPathComputationFailed(context.LoggingContext, location.AsLoggingLocation(), parsedPath.ToString());
                    return RelativePath.Invalid;
                }

                // Full path is correct, can create path literal
                pathExpression = new PathLiteral(projectAbsolutePath, location.AsLineInfo());

                if (projectAbsolutePath == context.RootPath)
                {
                    // Trying to import package
                    // TODO:ST: see Parser.cs:907 (method TryJoinPathExpression). Is it correct?
                    return RelativePath.Create(context.StringTable, ".");
                }

                // Now we need to get relative path to current package's file
                if (!context.RootPath.TryGetRelative(context.PathTable, projectAbsolutePath, out RelativePath relativePath))
                {
                    context.Logger.ReportProjectPathComputationFailed(context.LoggingContext, location.AsLoggingLocation(), parsedPath.ToString());
                    return RelativePath.Invalid;
                }

                return relativePath;
            }

            throw new InvalidOperationException("This code should not be reachable");
        }

        private static AbsolutePath ComputeAbsolutePath(RuntimeModelContext context, AbsolutePath currentFolder, RelativePath relativePath, int parentFolderCount)
        {
            // parent folder count is a number of ../ expression in the original path literal
            var folder = currentFolder;
            for (var i = 0; i < parentFolderCount; i++)
            {
                folder = folder.GetParent(context.PathTable);
                if (!folder.IsValid)
                {
                    break;
                }
            }

            if (!folder.IsValid)
            {
                return AbsolutePath.Invalid;
            }

            return folder.Combine(context.PathTable, relativePath);
        }
    }
}

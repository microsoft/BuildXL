// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Core;
using BuildXL.FrontEnd.Script.Expressions;
using BuildXL.FrontEnd.Script.Literals;
using BuildXL.Utilities;
using static TypeScript.Net.Types.NodeExtensions;

namespace BuildXL.FrontEnd.Script.RuntimeModel.AstBridge
{
    /// <summary>
    /// Helper class for converting different kinds of literals form parse AST to evaluation AST.
    /// </summary>
    /// <remarks>
    /// The type is thread-safe and any instance of the type can be used from multiple threads without any synchronization.
    /// </remarks>
    internal sealed class LiteralConverter
    {
        private AstConversionContext Context { get; }

        public LiteralConverter(AstConversionContext context)
        {
            Contract.Requires(context != null);
            Context = context;
        }

        /// <summary>
        /// Converts provided literal from string representation to 32-bit integer.
        /// </summary>
        public static Number TryConvertNumber(string literalText)
        {
            Contract.Requires(!string.IsNullOrEmpty(literalText));

            int fromBase = 10;

            if (literalText.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                fromBase = 16;
            }
            else if (literalText.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
            {
                fromBase = 2;
            }
            else if (literalText.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
            {
                fromBase = 8;
            }

            var text = fromBase == 10 ? literalText : literalText.Substring(2 /*"0x".Length*/);
            try
            {
                return new Number(Convert.ToInt32(text, fromBase));
            }
            catch (FormatException)
            {
                return Number.InvalidFormat();
            }
            catch (OverflowException)
            {
                return Number.Overflow();
            }
        }

        /// <summary>
        /// Convert string literal to string or path based on the configuration.
        /// </summary>
        public static Expression ConvertStringLiteral(string text, in UniversalLocation location)
        {
            return new StringLiteral(text, location.AsLineInfo());
        }

        /// <summary>
        /// Converts string to path literal.
        /// </summary>
        public PathLiteral ConvertPathLiteral(StringSegment text, in UniversalLocation location)
        {
            var path = TryCreateAbsolutePathFrom(text);

            if (!path.IsValid)
            {
                Context.Logger.ReportInvalidPathExpression(Context.LoggingContext, location.AsLoggingLocation(), text.ToString());
                return null;
            }

            return new PathLiteral(path, location.AsLineInfo());
        }

        /// <summary>
        /// Converts string to path-like literal to a path literal.
        /// </summary>
        public PathLiteral ConvertPathLiteral(TypeScript.Net.Types.ILiteralExpression literal, in UniversalLocation location)
        {
            var pathLikeLiteral = literal.As<TypeScript.Net.Types.IPathLikeLiteralExpression>();
            if (pathLikeLiteral != null)
            {
                return ConvertPathLiteral(pathLikeLiteral, location);
            }

            var stringLiteral = literal.Cast<TypeScript.Net.Types.IStringLiteral>();
            return ConvertPathLiteral(stringLiteral.Text, location);
        }

        /// <summary>
        /// Converts string to path literal.
        /// </summary>
        public PathLiteral ConvertPathLiteral(TypeScript.Net.Types.IPathLikeLiteralExpression literal, in UniversalLocation location)
        {
            if (TryConvertToAbsolutePath(out var absolutePath))
            {
                return new PathLiteral(absolutePath, location.AsLineInfo());
            }

            return ConvertPathLiteral(literal.Text, location);

            bool TryConvertToAbsolutePath(out AbsolutePath result)
            {
                switch (literal)
                {
                    case TypeScript.Net.Types.PackageRelativePathLiteralExpression packageRelative:
                        result = ComputeAbsolutePath(Context.RuntimeModelContext.RootPath, packageRelative.Path, 0);
                        return true;
                    case TypeScript.Net.Types.RelativePathLiteralExpression specRelative:
                        result = ComputeAbsolutePath(Context.CurrentSpecFolder, specRelative.Path, 0);
                        return true;
                    case TypeScript.Net.Types.AbsolutePathLiteralExpression absolute:
                        result = absolute.Path;
                        return true;
                    default:
                        result = AbsolutePath.Invalid;
                        return false;
                }
            }
        }

        /// <summary>
        /// Converts string to relative path literal.
        /// </summary>
        public RelativePathLiteral ConvertRelativePathLiteral(TypeScript.Net.Types.ILiteralExpression literal, in UniversalLocation location)
        {
            var pathLikeLiteral = literal.As<TypeScript.Net.Types.IPathLikeLiteralExpression>();
            if (pathLikeLiteral != null)
            {
                return new RelativePathLiteral(
                    pathLikeLiteral.Cast<TypeScript.Net.Types.RelativePathLiteralExpression>().Path,
                    location.AsLineInfo());
            }

            return ConvertRelativePathLiteral(literal.Cast<TypeScript.Net.Types.IStringLiteral>().Text, location);
        }

        /// <summary>
        /// Converts string to relative path literal.
        /// </summary>
        public RelativePathLiteral ConvertRelativePathLiteral(StringSegment text, in UniversalLocation location)
        {
            if (!RelativePath.TryCreate(Context.StringTable, text, out RelativePath relativePath))
            {
                Context.Logger.ReportInvalidRelativePathExpression(Context.LoggingContext, location.AsLoggingLocation());
                return null;
            }

            return new RelativePathLiteral(relativePath, location.AsLineInfo());
        }

        /// <summary>
        /// Converts string to path atom literal.
        /// </summary>
        public PathAtomLiteral ConvertPathAtomLiteral(TypeScript.Net.Types.ILiteralExpression literal, in UniversalLocation location)
        {
            var pathLikeLiteral = literal.As<TypeScript.Net.Types.IPathLikeLiteralExpression>();
            if (pathLikeLiteral != null)
            {
                return new PathAtomLiteral(pathLikeLiteral.Cast<TypeScript.Net.Types.PathAtomLiteralExpression>().Atom, location.AsLineInfo());
            }

            return ConvertPathAtomLiteral(literal.Cast<TypeScript.Net.Types.IStringLiteral>().Text, location);
        }

        /// <summary>
        /// Converts string to path atom literal.
        /// </summary>
        public PathAtomLiteral ConvertPathAtomLiteral(string text, in UniversalLocation location)
        {
            Contract.Requires(text != null);

            if (!PathAtom.TryCreate(Context.StringTable, text, out PathAtom pathAtom))
            {
                Context.Logger.ReportInvalidPathAtomExpression(Context.LoggingContext, location.AsLoggingLocation());
                return null;
            }

            return new PathAtomLiteral(pathAtom, location.AsLineInfo());
        }

        private AbsolutePath TryCreateAbsolutePathFrom(StringSegment text)
        {
            // this is a path for now
            var pathConverter = new PathResolver(Context.PathTable);
            var parsedPath = pathConverter.ParseRegularPath(text);

            if (!parsedPath.IsValid)
            {
                return AbsolutePath.Invalid;
            }

            // Absolute path like: c:/foo/bar.dsc
            if (parsedPath.IsAbsolutePath)
            {
                return parsedPath.Absolute;
            }

            // Package relative like: /c/foo/bar.dsc
            if (parsedPath.IsPackageRelative)
            {
                return Context.RuntimeModelContext.RootPath.Combine(Context.PathTable, parsedPath.PackageRelative);
            }

            // File relative like: foo/bar.dsc or ./bar.dsc
            // Combine path to get 'ThisFileDirPath/f/g/h'.
            return ComputeAbsolutePath(Context.CurrentSpecFolder, parsedPath.FileRelative, parsedPath.ParentCount);
        }

        private AbsolutePath ComputeAbsolutePath(AbsolutePath currentFolder, RelativePath relativePath, int parentFolderCount)
        {
            // parent folder count is a number of ../ expression in the original path literal
            if (!currentFolder.IsValid)
            {
                return AbsolutePath.Invalid;
            }

            var folder = currentFolder;

            for (int i = 0; i < parentFolderCount; i++)
            {
                var tempFolder = folder.GetParent(Context.PathTable);
                // If we go beyond the root directory (tempFolder will be invalid), stay at the root directory. 
                // Ie. C:/AAA/BBB/../../../../.. should be C:/
                if (!tempFolder.IsValid)
                {
                    break;
                }
                folder = tempFolder;
            }

            return folder.Combine(Context.PathTable, relativePath);
        }
    }
}

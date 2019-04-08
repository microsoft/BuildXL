// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using BuildXL.Utilities;
using Microsoft.VisualStudio.LanguageServer.Protocol;
using TypeScript.Net.DScript;
using TypeScript.Net.Extensions;
using TypeScript.Net.Types;

namespace BuildXL.Ide.LanguageServer.Completion
{
    /// <summary>
    /// Class that provides completions for tagged tempalted expressions
    /// </summary>
    public sealed class TaggedTemplatedExpression
    {
        internal static bool ShouldCreateFileCompletionItemsForTaggedTemplatedExpression(CompletionState completionState, INode completionNode)
        {
            var templateExpression = completionState.StartingNode.Parent.Cast<ITaggedTemplateExpression>();
            if (!templateExpression.IsPathInterpolation())
            {
                return false;
            }

            // The a`` (PathAtom) interpolation kind is not supported for completion as it is user specified name.
            // The r`` (RelativePath) is also not supported.
            var interpolationKind = templateExpression.GetInterpolationKind();
            if (interpolationKind == InterpolationKind.PathAtomInterpolation ||
                interpolationKind == InterpolationKind.RelativePathInterpolation)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Attempts to create auto-complete items from the "tagged template" statements for file, path, directory and relative path (f``, d``, p``)
        /// </summary>
        internal static IEnumerable<CompletionItem> TryCreateFileCompletionItemsForTaggedTemplatedExpression(CompletionState completionState, INode completionNode)
        {
            var templateExpression = completionNode.Cast<ITaggedTemplateExpression>();

            // GetTemplateText does a Cast on the expression and then access the "text" property
            // instead of just returning a null or empty string.
            // So, before we can use GetTemplateText, we must check to see if it is actually of
            // the correct type.
            // We cannot use the syntax kind, as ILiteralExpression can cover many syntax kinds -
            // Strings, numbers, etc.
            var literalExprssion = templateExpression?.TemplateExpression?.As<ILiteralExpression>();
            var existingFileExpressionText = literalExprssion?.GetTemplateText();
            if (!string.IsNullOrEmpty(existingFileExpressionText))
            {
                if (!RelativePath.TryCreate(completionState.PathTable.StringTable, existingFileExpressionText, out var normalizedPath))
                {
                    return null;
                }

                existingFileExpressionText = normalizedPath.ToString(completionState.PathTable.StringTable);
            }
            else
            {
                existingFileExpressionText = string.Empty;
            }

            var sourceFileRootPath = Path.GetDirectoryName(completionState.StartingNode.SourceFile.ToUri().LocalPath);

            // Now, for this bit of fun. If the user has started typing a path already, then we still
            // want to complete what they have started typing.
            // If what they have typed does not have a directory separator in it, then we will use
            // that as the search pattern below. If it does however, then we will combine that
            // with the spec directory and then attempt to get the remainder to use as the search path.
            // So... if the user has typed `foo` we will use the spec path as the root search directory
            // and `foo*` as the search pattern.
            // If the user has typed `foo\bar` then we will use `<spec path>\foo` as the search path and
            // "bar" as the search pattern.
            var originalSourceFileRootPath = sourceFileRootPath;
            if (existingFileExpressionText.Contains(Path.DirectorySeparatorChar))
            {
                sourceFileRootPath = Path.Combine(sourceFileRootPath, existingFileExpressionText);
                existingFileExpressionText = Path.GetFileName(sourceFileRootPath);
                sourceFileRootPath = Path.GetDirectoryName(sourceFileRootPath);
            }

            // If the user types just a "\" character, then the source file root path can be null
            if (string.IsNullOrEmpty(sourceFileRootPath))
            {
                return null;
            }

            // If we managed to get a path that is outside of our spec path, then bail. Do not allow auto completion
            if (!sourceFileRootPath.StartsWith(originalSourceFileRootPath, System.StringComparison.OrdinalIgnoreCase) ||
                !Directory.Exists(sourceFileRootPath))
            {
                return null;
            }

            bool isDirectoryTag = templateExpression.GetInterpolationKind() == InterpolationKind.DirectoryInterpolation;

            // Leverage BuildXL's recoverable IO exception extension to ensure that if we hit
            // cases like "access denied" that it does not take down the plugin.
            IEnumerable<string> fileSystemEntries = null;
            try
            {
                fileSystemEntries = ExceptionUtilities.HandleRecoverableIOException(
                    () =>
                    {
                        return isDirectoryTag ?
                            Directory.EnumerateDirectories(sourceFileRootPath, existingFileExpressionText + "*", SearchOption.TopDirectoryOnly) :
                            Directory.EnumerateFileSystemEntries(sourceFileRootPath, existingFileExpressionText + "*", SearchOption.TopDirectoryOnly);
                    },
                    ex =>
                    {
                        throw new BuildXLException(ex.Message);
                    });
            }
            catch (BuildXLException)
            {
            }

            if (fileSystemEntries.IsNullOrEmpty())
            {
                return null;
            }

            // We aren't done yet :)
            // Let's see if we can filter further if we are in an array literal experssion..
            // So, if the user has something like this
            // sources: [
            //   f`fileA.cpp`,
            //   f`fileB.cpp`,
            //
            // And they have files fileC-FileZ.cpp on disk, we filter out fileA.cpp and fileB.cpp.
            // This is very similar to filtering out properties that are already set on an object
            // literal.
            if (templateExpression.Parent?.Kind == SyntaxKind.ArrayLiteralExpression)
            {
                var existingFiles = new List<string>();
                var existingArrayLiteralExpression = templateExpression.Parent.Cast<IArrayLiteralExpression>();
                foreach (var existingFileItem in existingArrayLiteralExpression.Elements)
                {
                    if (existingFileItem.Kind == SyntaxKind.TaggedTemplateExpression)
                    {
                        var existingFileTemplateExpression = existingFileItem.Cast<ITaggedTemplateExpression>();

                        // GetTemplateText does a Cast on the expression and then access the "text" property
                        // instead of just returning a null or empty string.
                        // So, before we can use GetTemplateText, we must check to see if it is actually of
                        // the correct type.
                        // We cannot use the syntax kind, as ILiteralExpression can cover many syntax kinds -
                        // Strings, numbers, etc.
                        var existingFileLiteralExprssion = existingFileTemplateExpression?.TemplateExpression?.As<ILiteralExpression>();
                        var existingFilePathText = existingFileLiteralExprssion?.GetTemplateText();
                        if (!string.IsNullOrEmpty(existingFilePathText))
                        {
                            existingFiles.Add(Path.Combine(originalSourceFileRootPath, existingFilePathText));
                        }
                    }
                }

                fileSystemEntries = fileSystemEntries.Where(possibleEntry => !existingFiles.Contains(possibleEntry, StringComparer.InvariantCultureIgnoreCase));
            }

            return fileSystemEntries.Select(name =>
            {
                var itemLabel = name.Substring(sourceFileRootPath.Length + 1);

                var item = new CompletionItem()
                {
                    Kind = CompletionItemKind.File,
                    Label = itemLabel,
                };

                return item;
            });
        }
    }
}

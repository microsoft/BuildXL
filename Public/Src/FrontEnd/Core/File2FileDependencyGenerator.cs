// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using BuildXL.FrontEnd.Workspaces.Core;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.FrontEnd.Core
{
    /// <summary>
    /// Helper class that dumps to disk file-to-file dependencies between all known dsc specs.
    /// </summary>
    public static class File2FileDependencyGenerator
    {
        internal sealed class Entry
        {
            public int SourcePathId { get; }

            public string SourceFullPath { get; }

            public int TargetPathId { get; }

            public string TargetFullPath { get; }

            public Entry(int sourcePathId, string sourceFullPath, int targetPathId, string targetFullPath)
            {
                SourcePathId = sourcePathId;
                SourceFullPath = sourceFullPath;
                TargetPathId = targetPathId;
                TargetFullPath = targetFullPath;
            }
        }

        /// <summary>
        /// Generate and save spec-2-spec dependency report.
        /// </summary>
        public static void GenerateAndSaveFile2FileDependencies(PathTable pathTable, AbsolutePath destination, Workspace workspace)
        {
            var semanticModel = workspace.GetSemanticModel();
            var allSources = workspace.GetAllSourceFiles();

            var entries = workspace.SpecSources.SelectMany(module =>
            {
                var currentPath = module.Key;
                return semanticModel.GetFileDependenciesOf(module.Value.SourceFile)
                    .Select(index =>
                    {
                        var targetPathStr = allSources[index].Path.AbsolutePath;
                        var targetPath = AbsolutePath.Create(pathTable, targetPathStr);
                        return new Entry(currentPath.RawValue, currentPath.ToString(pathTable), targetPath.RawValue, targetPathStr);
                    });
            })

                // Making the output stable across the runs by sorting the output.
                .OrderBy(r => r.SourceFullPath).ThenBy(r => r.TargetFullPath).ToList();

            SaveEntries(pathTable, destination, entries);
        }

        private static void SaveEntries(PathTable pathTable, AbsolutePath destination, List<Entry> entries)
        {
            var destinationPath = destination.ToString(pathTable);

            ExceptionUtilities.HandleRecoverableIOException(
                () =>
                {
                    // Build the report content. Materializes to the file on a per line basis since the whole content may be very big.
                    using (var stream = new StreamWriter(destinationPath))
                    {
                        stream.WriteLine(GetHeader());

                        foreach (var entry in entries)
                        {
                            stream.WriteLine(GetLine(entry));
                        }
                    }
                },
                ex =>
                {
                    throw new BuildXLException("Error while producing the report. Inner exception reason: " + ex.Message, ex);
                });
        }

        private static string GetHeader()
        {
            return "SourceFullPath;TargetFullPath;";
        }

        private static string GetLine(Entry entry)
        {
            return I($"{entry.SourceFullPath};{entry.TargetFullPath}");
        }
    }
}

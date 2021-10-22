// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Utilities.PackedTable;

namespace BuildXL.Utilities.PackedExecution
{
    /// <summary>
    /// Supplementary data structure providing indices over a PackedExecution.
    /// </summary>
    /// <remarks>
    /// Constructed from a PackedExecution, but actually populating it requires calling the async Initialize
    /// method (because we want to build all the pieces in parallel).
    /// </remarks>
    public class PackedIndex
    {
        #region Fields

        /// <summary>
        /// The underlying execution data.
        /// </summary>
        public PackedExecution PackedExecution { get; private set; }

        /// <summary>
        /// The index of all Strings.
        /// </summary>
        public StringIndex StringIndex { get; private set; }

        /// <summary>Pip name index.</summary>
        public NameIndex PipNameIndex { get; private set; }

        /// <summary>File path index.</summary>
        public NameIndex PathIndex { get; private set; }

        /// <summary>Derived relationship: the inverse of PipDependencies</summary>
        public RelationTable<PipId, PipId> PipDependents { get; private set; }

        /// <summary>Derived relationship: the inverse of ConsumedFiles</summary>
        public RelationTable<FileId, PipId> FileConsumers { get; private set; }

        /// <summary>Derived relationship: the inverse of FileProducer</summary>
        public RelationTable<PipId, FileId> ProducedFiles { get; private set; }

        /// <summary>Derived relationship: the inverse of DeclaredInputFiles</summary>
        public RelationTable<FileId, PipId> InputFileDeclarers { get; private set; }

        /// <summary>Derived relationship: the inverse of DeclaredInputDirectories</summary>
        public RelationTable<DirectoryId, PipId> InputDirectoryDeclarers { get; private set; }

        /// <summary>Derived relationship; the inverse of DirectoryContents</summary>
        public RelationTable<FileId, DirectoryId> ParentDirectories { get; private set; }

        #endregion

        /// <summary>Construct an initially empty PackedIndex; you must await Initialize() for the data to be populated.</summary>
        public PackedIndex(PackedExecution packedExecution)
        {
            PackedExecution = packedExecution;
        }

        /// <summary>
        /// Initialize all the elements of this index, as concurrently as possible.
        /// </summary>
        /// <param name="progressAction">Action called (from an arbitrary Task thread) to report completion of various index parts.</param>
        /// <returns>A task which is complete when the index is fully built.</returns>
        public Task InitializeAsync(Action<string> progressAction)
        {
            // All the things we want to do concurrently to index our data.
            // This is broken out as a separate list since it is useful to run these serially sometimes when debugging
            // (e.g. when the hacky Span sort code you found online starts hitting stack overflows, whereas the .NET 5
            // Span.Sort method just works...).
            List<(string, Action)> actions = new List<(string, Action)>
                {
                    ("Sorted strings", () => StringIndex = new StringIndex(PackedExecution.StringTable)),
                    ("Indexed pip names", () => PipNameIndex = new NameIndex(PackedExecution.PipTable.PipNameTable)),
                    ("Indexed paths", () => PathIndex = new NameIndex(PackedExecution.PathTable)),
                    ("Indexed pip dependents", () => PipDependents = PackedExecution.PipDependencies.Invert()),
                    ("Indexed file consumers", () => FileConsumers = PackedExecution.ConsumedFiles.Invert()),
                    ("Indexed produced files", () => ProducedFiles =
                        RelationTable<FileId, PipId>
                        .FromSingleValueTable(PackedExecution.FileProducer, PackedExecution.PipTable)
                        .Invert()),
                    ("Indexed input-file-declaring pips", () => InputFileDeclarers = PackedExecution.DeclaredInputFiles.Invert()),
                    ("Indexed input-directory-declaring pips", () => InputDirectoryDeclarers = PackedExecution.DeclaredInputDirectories.Invert()),
                    ("Indexed parent directories", () => ParentDirectories = PackedExecution.DirectoryContents.Invert())
                };

            // Concurrently generate all the sorted strings, name indices, and inverse relationships that we need.
            return Task.WhenAll(
                actions
                .Select(tuple => Task.Run(
                    () =>
                    {
                        tuple.Item2();
                        progressAction(tuple.Item1);
                    }))
                .ToArray());
        }
    }
}

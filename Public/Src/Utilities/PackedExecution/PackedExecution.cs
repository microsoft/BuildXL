// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Utilities.PackedTable;
using System.IO;

namespace BuildXL.Utilities.PackedExecution
{
    /// <summary>
    /// Overall data structure representing an entire BXL execution graph.
    /// </summary>
    /// <remarks>
    /// Consists of multiple tables, with methods to construct, save, and load them all.
    /// 
    /// Please keep lists of fields, tables, relations, etc. locally sorted in this file, for better merging as this grows.
    /// </remarks>
    public class PackedExecution
    {
        #region Tables

        /// <summary>
        /// The directories.
        /// </summary>
        public readonly DirectoryTable DirectoryTable;

        /// <summary>
        /// The files.
        /// </summary>
        public readonly FileTable FileTable;

        /// <summary>
        /// The paths.
        /// </summary>
        /// <remarks>
        /// Shared by FileTable and DirectoryTable.
        /// </remarks>
        public readonly NameTable PathTable;

        /// <summary>
        /// The pips.
        /// </summary>
        public readonly PipTable PipTable;

        /// <summary>
        /// The pip executions.
        /// </summary>
        /// <remarks>
        /// Currently this is stored sparsely -- most entries will be empty (uninitialized), since most pips are not
        /// process pips (and if they are, may not get executed).
        /// 
        /// TODO: keep an eye on space usage here, and either support sparse derived tables, or make this data its own
        /// base table and add a joining relation to the pip table.
        /// </remarks>
        public readonly PipExecutionTable PipExecutionTable;

        /// <summary>
        /// The strings.
        /// </summary>
        /// <remarks>
        /// Shared by everything that contains strings (mainly PathTable).
        /// </remarks>
        public readonly PackedTable.StringTable StringTable;

        /// <summary>
        /// The workers.
        /// </summary>
        public readonly WorkerTable WorkerTable;

        #endregion

        #region Relations

        /// <summary>
        /// The produced file relation (from executed pips towards the files they produced).
        /// </summary>
        public RelationTable<PipId, FileId> ConsumedFiles { get; private set; }

        /// <summary>
        /// The static input directory relation (from executed pips towards their statically declared input directories).
        /// </summary>
        public RelationTable<PipId, DirectoryId> DeclaredInputDirectories { get; private set; }

        /// <summary>
        /// The static input file relation (from executed pips towards their statically declared input files).
        /// </summary>
        public RelationTable<PipId, FileId> DeclaredInputFiles { get; private set; }

        /// <summary>
        /// The directory contents relation (from directories towards the files they contain).
        /// </summary>
        public RelationTable<DirectoryId, FileId> DirectoryContents { get; private set; }

        /// <summary>
        /// The pip dependency relation (from the dependent pip, towards the dependency pip).
        /// </summary>
        public RelationTable<PipId, PipId> PipDependencies { get; private set; }

        #endregion

        /// <summary>
        /// Construct a PackedExecution with empty base tables.
        /// </summary>
        /// <remarks>
        /// After creating these tables, create their Builders (inner classes) to populate them.
        /// Note that calling ConstructRelationTables() is necessary after these are fully built,
        /// before the relations can then be built.
        /// </remarks>
        public PackedExecution()
        {
            StringTable = new PackedTable.StringTable();
            PathTable = new NameTable('\\', StringTable);
            DirectoryTable = new DirectoryTable(PathTable);
            FileTable = new FileTable(PathTable);
            PipTable = new PipTable(StringTable);
            PipExecutionTable = new PipExecutionTable(PipTable);
            WorkerTable = new WorkerTable(StringTable);
        }

        private static readonly string s_directoryTableFileName = $"{nameof(DirectoryTable)}.bin";
        private static readonly string s_fileTableFileName = $"{nameof(FileTable)}.bin";
        private static readonly string s_pathTableFileName = $"{nameof(PathTable)}.bin";
        private static readonly string s_pipTableFileName = $"{nameof(PipTable)}.bin";
        private static readonly string s_pipExecutionTableFileName = $"{nameof(PipExecutionTable)}.bin";
        private static readonly string s_stringTableFileName = $"{nameof(StringTable)}.bin";
        private static readonly string s_workerTableFileName = $"{nameof(WorkerTable)}.bin";

        private static readonly string s_consumedFilesFileName = $"{nameof(ConsumedFiles)}.bin";
        private static readonly string s_declaredInputDirectoriesFileName = $"{nameof(DeclaredInputDirectories)}.bin";
        private static readonly string s_declaredInputFilesFileName = $"{nameof(DeclaredInputFiles)}.bin";
        private static readonly string s_directoryContentsFileName = $"{nameof(DirectoryContents)}.bin";
        private static readonly string s_pipDependenciesFileName = $"{nameof(PipDependencies)}.bin";

        /// <summary>
        /// After the base tables are populated, construct the (now properly sized) relation tables.
        /// </summary>
        public void ConstructRelationTables()
        {
            //System.Diagnostics.ContractsLight.Contract.Requires(ConsumedFiles == null, "Must only construct relation tables once");

            ConsumedFiles = new RelationTable<PipId, FileId>(PipTable, FileTable);
            DeclaredInputDirectories = new RelationTable<PipId, DirectoryId>(PipTable, DirectoryTable);
            DeclaredInputFiles = new RelationTable<PipId, FileId>(PipTable, FileTable);
            DirectoryContents = new RelationTable<DirectoryId, FileId>(DirectoryTable, FileTable);
            PipDependencies = new RelationTable<PipId, PipId>(PipTable, PipTable);
        }

        /// <summary>
        /// Save the whole data set as a series of files in the given directory.
        /// </summary>
        public void SaveToDirectory(string directory)
        {
            DirectoryTable.SaveToFile(directory, s_directoryTableFileName);
            FileTable.SaveToFile(directory, s_fileTableFileName);
            PathTable.SaveToFile(directory, s_pathTableFileName);
            PipTable.SaveToFile(directory, s_pipTableFileName);
            PipExecutionTable.SaveToFile(directory, s_pipExecutionTableFileName);
            StringTable.SaveToFile(directory, s_stringTableFileName);
            WorkerTable.SaveToFile(directory, s_workerTableFileName);

            ConsumedFiles?.SaveToFile(directory, s_consumedFilesFileName);
            DeclaredInputDirectories?.SaveToFile(directory, s_declaredInputDirectoriesFileName);
            DeclaredInputFiles?.SaveToFile(directory, s_declaredInputFilesFileName);
            DirectoryContents?.SaveToFile(directory, s_directoryContentsFileName);
            PipDependencies?.SaveToFile(directory, s_pipDependenciesFileName);
        }

        /// <summary>
        /// Load the whole data set from a series of files in the given directory.
        /// </summary>
        public void LoadFromDirectory(string directory)
        {
            DirectoryTable.LoadFromFile(directory, s_directoryTableFileName);
            FileTable.LoadFromFile(directory, s_fileTableFileName);
            PathTable.LoadFromFile(directory, s_pathTableFileName);
            PipTable.LoadFromFile(directory, s_pipTableFileName);
            PipExecutionTable.LoadFromFile(directory, s_pipExecutionTableFileName);
            StringTable.LoadFromFile(directory, s_stringTableFileName);
            WorkerTable.LoadFromFile(directory, s_workerTableFileName);

            ConstructRelationTables();

            loadRelationTableIfExists(directory, s_consumedFilesFileName, ConsumedFiles);
            loadRelationTableIfExists(directory, s_declaredInputDirectoriesFileName, DeclaredInputDirectories);
            loadRelationTableIfExists(directory, s_declaredInputFilesFileName, DeclaredInputFiles);
            loadRelationTableIfExists(directory, s_directoryContentsFileName, DirectoryContents);
            loadRelationTableIfExists(directory, s_pipDependenciesFileName, PipDependencies);

            void loadRelationTableIfExists<TFromId, TToId>(string directory, string fileName, RelationTable<TFromId, TToId> relation)
                where TFromId : unmanaged, Id<TFromId>
                where TToId : unmanaged, Id<TToId>
            {
                if (File.Exists(Path.Combine(directory, fileName)))
                {
                    relation.LoadFromFile(directory, fileName);
                }
            }
        }

        /// <summary>
        /// Build an entire PackedExecution by collecting all the builders for each piece.
        /// </summary>
        public class Builder
        {
            /// <summary>
            /// The PackedExecution being built.
            /// </summary>
            public readonly PackedExecution PackedExecution;

            /// <summary>
            /// Builder for DirectoryTable.
            /// </summary>
            public readonly DirectoryTable.CachingBuilder DirectoryTableBuilder;
            /// <summary>
            /// Builder for FileTable.
            /// </summary>
            public readonly FileTable.CachingBuilder FileTableBuilder;
            /// <summary>
            /// Builder for PathTable.
            /// </summary>
            public readonly NameTable.Builder PathTableBuilder;
            /// <summary>
            /// Builder for PipTable.
            /// </summary>
            public readonly PipTable.Builder PipTableBuilder;
            
            // There is deliberately no PipExecutionTableBuilder; just call FillToBaseTableCount on it and then set values in it.

            /// <summary>
            /// Builder for StringTable..
            /// </summary>
            public readonly PackedTable.StringTable.CachingBuilder StringTableBuilder;
            /// <summary>
            /// Builder for WOrkerTable.
            /// </summary>
            public readonly WorkerTable.CachingBuilder WorkerTableBuilder;

            /// <summary>
            /// Builder for ConsumedFiles relation.
            /// </summary>
            public readonly RelationTable<PipId, FileId>.Builder ConsumedFilesBuilder;
            /// <summary>
            /// Builder for DeclaredInputDirectories relation.
            /// </summary>
            public readonly RelationTable<PipId, DirectoryId>.Builder DeclaredInputDirectoriesBuilder;
            /// <summary>
            /// Builder for DeclaredInputFiles relation.
            /// </summary>
            public readonly RelationTable<PipId, FileId>.Builder DeclaredInputFilesBuilder;
            /// <summary>
            /// Builder for DirectoryContents relation.
            /// </summary>
            public readonly RelationTable<DirectoryId, FileId>.Builder DirectoryContentsBuilder;
            /// <summary>
            /// Builder for PipDependencies relation.
            /// </summary>
            public readonly RelationTable<PipId, PipId>.Builder PipDependenciesBuilder;

            /// <summary>
            /// Construct a Builder.
            /// </summary>
            public Builder(PackedExecution packedExecution)
            {
                PackedExecution = packedExecution;

                // these are sorted as much as possible given construction order constraints
                StringTableBuilder = new PackedTable.StringTable.CachingBuilder(PackedExecution.StringTable);
                PathTableBuilder = new NameTable.Builder(PackedExecution.PathTable, StringTableBuilder);
                DirectoryTableBuilder = new DirectoryTable.CachingBuilder(PackedExecution.DirectoryTable, PathTableBuilder);
                FileTableBuilder = new FileTable.CachingBuilder(PackedExecution.FileTable, PathTableBuilder);
                PipTableBuilder = new PipTable.Builder(PackedExecution.PipTable, StringTableBuilder);               
                WorkerTableBuilder = new WorkerTable.CachingBuilder(PackedExecution.WorkerTable, StringTableBuilder);

                if (packedExecution.ConsumedFiles != null)
                {
                    ConsumedFilesBuilder = new RelationTable<PipId, FileId>.Builder(packedExecution.ConsumedFiles);
                    DeclaredInputDirectoriesBuilder = new RelationTable<PipId, DirectoryId>.Builder(packedExecution.DeclaredInputDirectories);
                    DeclaredInputFilesBuilder = new RelationTable<PipId, FileId>.Builder(packedExecution.DeclaredInputFiles);
                    DirectoryContentsBuilder = new RelationTable<DirectoryId, FileId>.Builder(packedExecution.DirectoryContents);
                    PipDependenciesBuilder = new RelationTable<PipId, PipId>.Builder(packedExecution.PipDependencies);
                }
            }
        }
    }
}

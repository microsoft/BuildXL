// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.IO;
using BuildXL.Utilities.PackedTable;

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

        /// <summary>The directories.</summary>
        public readonly DirectoryTable DirectoryTable;

        /// <summary>The files.</summary>
        public readonly FileTable FileTable;

        /// <summary>The paths.</summary>
        /// <remarks>Shared by FileTable and DirectoryTable.</remarks>
        public readonly NameTable PathTable;

        /// <summary>The pips.</summary>
        public readonly PipTable PipTable;

        /// <summary>The pip executions.</summary>
        public readonly PipExecutionTable PipExecutionTable;

        /// <summary>The process executions.</summary>
        public readonly ProcessExecutionTable ProcessExecutionTable;

        /// <summary>The process pip executions.</summary>
        public readonly ProcessPipExecutionTable ProcessPipExecutionTable;

        /// <summary>The strings.</summary>
        /// <remarks>Shared by everything that contains strings (mainly PathTable and PipTable.PipNameTable).</remarks>
        public readonly PackedTable.StringTable StringTable;

        /// <summary>The workers.</summary>
        public readonly WorkerTable WorkerTable;

        #endregion

        #region Relations

        /// <summary>The file producer relation (from each file to the single pip that produced it).</summary>
        public SingleValueTable<FileId, PipId> FileProducer { get; private set; }

        /// <summary>The consumed file relation (from executed pips to the files they consumed).</summary>
        /// <remarks>
        /// This relation includes all of the DeclaredInputFiles, as well as any files that were actually observed to be read as inputs.
        /// Note that without tracking all observed file accesses, we cannot be sure that all declared inputs were actually read by the pip.
        /// But since any changes to declared inputs will result in the pip being re-executed, it's a conservative assumption that all declared inputs were consumed.
        /// </remarks>
        public RelationTable<PipId, FileId> ConsumedFiles { get; private set; }

        /// <summary>The static input directory relation (from executed pips to their statically declared input directories).</summary>
        public RelationTable<PipId, DirectoryId> DeclaredInputDirectories { get; private set; }

        /// <summary>The static input file relation (from executed pips to their statically declared input files).</summary>
        public RelationTable<PipId, FileId> DeclaredInputFiles { get; private set; }

        /// <summary>The directory contents relation (from directories to the files they contain).</summary>
        public RelationTable<DirectoryId, FileId> DirectoryContents { get; private set; }

        /// <summary>The pip dependency relation (from the dependent pip, to the dependency pip).</summary>
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
            ProcessExecutionTable = new ProcessExecutionTable(PipTable);
            ProcessPipExecutionTable = new ProcessPipExecutionTable(PipTable);
            WorkerTable = new WorkerTable(StringTable);
        }

        private static readonly string s_directoryTableFileName = $"{nameof(DirectoryTable)}.bin";
        private static readonly string s_fileTableFileName = $"{nameof(FileTable)}.bin";
        private static readonly string s_pathTableFileName = $"{nameof(PathTable)}.bin";
        private static readonly string s_pipTableFileName = $"{nameof(PipTable)}.bin";
        private static readonly string s_pipExecutionTableFileName = $"{nameof(PipExecutionTable)}.bin";
        private static readonly string s_processExecutionTableFileName = $"{nameof(ProcessExecutionTable)}.bin";
        private static readonly string s_processPipExecutionTableFileName = $"{nameof(ProcessPipExecutionTable)}.bin";
        private static readonly string s_stringTableFileName = $"{nameof(StringTable)}.bin";
        private static readonly string s_workerTableFileName = $"{nameof(WorkerTable)}.bin";

        private static readonly string s_fileProducerFileName = $"{nameof(FileProducer)}.bin";
        private static readonly string s_consumedFilesFileName = $"{nameof(ConsumedFiles)}.bin";
        private static readonly string s_declaredInputDirectoriesFileName = $"{nameof(DeclaredInputDirectories)}.bin";
        private static readonly string s_declaredInputFilesFileName = $"{nameof(DeclaredInputFiles)}.bin";
        private static readonly string s_directoryContentsFileName = $"{nameof(DirectoryContents)}.bin";
        private static readonly string s_pipDependenciesFileName = $"{nameof(PipDependencies)}.bin";

        /// <summary>After the base tables are populated, construct the (now properly sized) relation tables.</summary>
        public void ConstructRelationTables()
        {
            FileProducer = new SingleValueTable<FileId, PipId>(FileTable);
            ConsumedFiles = new RelationTable<PipId, FileId>(PipTable, FileTable);
            DeclaredInputDirectories = new RelationTable<PipId, DirectoryId>(PipTable, DirectoryTable);
            DeclaredInputFiles = new RelationTable<PipId, FileId>(PipTable, FileTable);
            DirectoryContents = new RelationTable<DirectoryId, FileId>(DirectoryTable, FileTable);
            PipDependencies = new RelationTable<PipId, PipId>(PipTable, PipTable);
        }

        /// <summary>Save all tables to the given directory.</summary>
        public void SaveToDirectory(string directory)
        {
            DirectoryTable.SaveToFile(directory, s_directoryTableFileName);
            FileTable.SaveToFile(directory, s_fileTableFileName);
            PathTable.SaveToFile(directory, s_pathTableFileName);
            PipTable.SaveToFile(directory, s_pipTableFileName);
            PipExecutionTable.SaveToFile(directory, s_pipExecutionTableFileName);
            ProcessExecutionTable.SaveToFile(directory, s_processExecutionTableFileName);
            ProcessPipExecutionTable.SaveToFile(directory, s_processPipExecutionTableFileName);
            StringTable.SaveToFile(directory, s_stringTableFileName);
            WorkerTable.SaveToFile(directory, s_workerTableFileName);

            FileProducer?.SaveToFile(directory, s_fileProducerFileName);
            ConsumedFiles?.SaveToFile(directory, s_consumedFilesFileName);
            DeclaredInputDirectories?.SaveToFile(directory, s_declaredInputDirectoriesFileName);
            DeclaredInputFiles?.SaveToFile(directory, s_declaredInputFilesFileName);
            DirectoryContents?.SaveToFile(directory, s_directoryContentsFileName);
            PipDependencies?.SaveToFile(directory, s_pipDependenciesFileName);
        }

        /// <summary>Load all tables from the given directory.</summary>
        public void LoadFromDirectory(string directory)
        {
            DirectoryTable.LoadFromFile(directory, s_directoryTableFileName);
            FileTable.LoadFromFile(directory, s_fileTableFileName);
            PathTable.LoadFromFile(directory, s_pathTableFileName);
            PipTable.LoadFromFile(directory, s_pipTableFileName);
            PipExecutionTable.LoadFromFile(directory, s_pipExecutionTableFileName);
            ProcessExecutionTable.LoadFromFile(directory, s_processExecutionTableFileName);
            ProcessPipExecutionTable.LoadFromFile(directory, s_processPipExecutionTableFileName);
            StringTable.LoadFromFile(directory, s_stringTableFileName);
            WorkerTable.LoadFromFile(directory, s_workerTableFileName);

            ConstructRelationTables();

            if (File.Exists(Path.Combine(directory, s_fileProducerFileName)))
            {
                FileProducer.LoadFromFile(directory, s_fileProducerFileName);
            }

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

        /// <summary>Build a PackedExecution (by providing builders for all its tables).</summary>
        public class Builder
        {
            /// <summary>The full data set.</summary>
            public readonly PackedExecution PackedExecution;

            /// <nodoc />
            public readonly DirectoryTable.CachingBuilder DirectoryTableBuilder;
            /// <nodoc />
            public readonly FileTable.CachingBuilder FileTableBuilder;
            /// <nodoc />
            public readonly NameTable.Builder PathTableBuilder;
            /// <nodoc />
            public readonly PipTable.Builder PipTableBuilder;
            /// <nodoc />
            public readonly PipExecutionTable.Builder<PipExecutionTable> PipExecutionTableBuilder;
            /// <nodoc />
            public readonly ProcessExecutionTable.Builder<ProcessExecutionTable> ProcessExecutionTableBuilder;
            /// <nodoc />
            public readonly ProcessPipExecutionTable.Builder<ProcessPipExecutionTable> ProcessPipExecutionTableBuilder;
            /// <nodoc />
            public readonly PackedTable.StringTable.CachingBuilder StringTableBuilder;
            /// <nodoc />
            public readonly WorkerTable.CachingBuilder WorkerTableBuilder;

            /// <nodoc />
            public readonly RelationTable<PipId, FileId>.Builder ConsumedFilesBuilder;
            /// <nodoc />
            public readonly RelationTable<PipId, DirectoryId>.Builder DeclaredInputDirectoriesBuilder;
            /// <nodoc />
            public readonly RelationTable<PipId, FileId>.Builder DeclaredInputFilesBuilder;
            /// <nodoc />
            public readonly RelationTable<DirectoryId, FileId>.Builder DirectoryContentsBuilder;

            /// <nodoc />
            public Builder(PackedExecution packedExecution)
            {
                PackedExecution = packedExecution;

                // these are sorted as much as possible given construction order constraints
                StringTableBuilder = new PackedTable.StringTable.CachingBuilder(PackedExecution.StringTable);
                PathTableBuilder = new NameTable.Builder(PackedExecution.PathTable, StringTableBuilder);
                DirectoryTableBuilder = new DirectoryTable.CachingBuilder(PackedExecution.DirectoryTable, PathTableBuilder);
                FileTableBuilder = new FileTable.CachingBuilder(PackedExecution.FileTable, PathTableBuilder);
                PipTableBuilder = new PipTable.Builder(PackedExecution.PipTable, StringTableBuilder);
                PipExecutionTableBuilder = new PipExecutionTable.Builder<PipExecutionTable>(PackedExecution.PipExecutionTable);
                ProcessExecutionTableBuilder = new ProcessExecutionTable.Builder<ProcessExecutionTable>(PackedExecution.ProcessExecutionTable);
                ProcessPipExecutionTableBuilder = new ProcessPipExecutionTable.Builder<ProcessPipExecutionTable>(PackedExecution.ProcessPipExecutionTable);
                PipTableBuilder = new PipTable.Builder(PackedExecution.PipTable, StringTableBuilder);
                WorkerTableBuilder = new WorkerTable.CachingBuilder(PackedExecution.WorkerTable, StringTableBuilder);

                if (packedExecution.ConsumedFiles != null)
                {
                    ConsumedFilesBuilder = new RelationTable<PipId, FileId>.Builder(packedExecution.ConsumedFiles);
                    DeclaredInputDirectoriesBuilder = new RelationTable<PipId, DirectoryId>.Builder(packedExecution.DeclaredInputDirectories);
                    DeclaredInputFilesBuilder = new RelationTable<PipId, FileId>.Builder(packedExecution.DeclaredInputFiles);
                    DirectoryContentsBuilder = new RelationTable<DirectoryId, FileId>.Builder(packedExecution.DirectoryContents);
                }
            }

            /// <summary>
            /// Complete the builders that need completing.
            /// </summary>
            /// <remarks>
            /// The builders that need completing are MultiValueTable.Builders, or builders derived therefrom.
            /// </remarks>
            public void Complete()
            {
                PipExecutionTableBuilder.Complete();
                ProcessExecutionTableBuilder.Complete();
                ProcessPipExecutionTableBuilder.Complete();

                if (ConsumedFilesBuilder != null)
                {
                    ConsumedFilesBuilder.Complete();
                    DeclaredInputFilesBuilder.Complete();
                    DeclaredInputDirectoriesBuilder.Complete();
                    DirectoryContentsBuilder.Complete();
                }
            }
        }
    }
}

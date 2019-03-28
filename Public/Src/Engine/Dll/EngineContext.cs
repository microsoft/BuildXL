// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Pips.Operations;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using BuildXL.Utilities.Instrumentation.Common;
using BuildXL.Utilities.Qualifier;
using BuildXL.Utilities.Tracing;
using JetBrains.Annotations;
using BuildXL.FrontEnd.Sdk;
using BuildXL.FrontEnd.Sdk.FileSystem;

namespace BuildXL.Engine
{
    /// <summary>
    /// This object carries the state the engine needs to hand down to components it hosts.
    /// </summary>
    public sealed class EngineContext : BuildXLContext
    {
        /// <summary>
        /// Envelope for serialization
        /// </summary>
        public static readonly FileEnvelope HistoricTableSizesFileEnvelope = new FileEnvelope(name: "HistoricTableSizes", version: 1);

        /// <summary>
        /// Pool of PipDataBuilder instances.
        /// </summary>
        public ObjectPool<PipDataBuilder> PipDataBuilderPool { get; private set; }

        ///<nodoc/>
        public IFileSystem FileSystem { get; }

        /// <summary>
        /// Historic table sizes.
        ///
        /// This property is a list of historic data points, where each data point
        /// keeps values of the 'Count' and 'SizeInBytes' properties of the following
        /// tables from this context: PathTable, SymbolTable, and StringTable.
        /// </summary>
        [NotNull]
        public HistoricTableSizes HistoricTableSizes { get; }

        /// <summary>
        /// Creates a fresh instance of the <see cref="Engine.HistoricTableSizes"/> class containing
        /// all data points from <see cref="HistoricTableSizes"/> plus a new <see cref="HistoricDataPoint"/>
        /// corresponding to the current table sizes of this context.
        /// </summary>
        [NotNull]
        public HistoricTableSizes NextHistoricTableSizes => HistoricTableSizes.ConcatDataPoint(GetHistoricDataPoint());

        /// <summary>
        /// The counters for the engine
        /// </summary>
        internal CounterCollection<EngineCounter> EngineCounters { get; private set; }

        /// <summary>
        /// Creates the engine context
        /// </summary>
        internal EngineContext(
            CancellationToken cancellationToken,
            PathTable pathTable,
            SymbolTable symbolTable,
            QualifierTable qualifierTable,
            IFileSystem fileSystem,
            TokenTextTable tokenTextTable,
            CounterCollection<EngineCounter> engineCounters = null,
            HistoricTableSizes historicTableSizes = null)
            : base(cancellationToken, pathTable.StringTable, pathTable, symbolTable, qualifierTable, tokenTextTable)
        {
            Contract.Requires(pathTable != null);
            Contract.Requires(symbolTable != null);
            Contract.Requires(fileSystem != null);
            Contract.Requires(tokenTextTable != null);
            Contract.Requires(pathTable.StringTable == symbolTable.StringTable);
            Contract.Requires(pathTable.StringTable == qualifierTable.StringTable);

            PipDataBuilderPool = new ObjectPool<PipDataBuilder>(() => new PipDataBuilder(StringTable), builder => builder.Clear());
            EngineCounters = engineCounters ?? new CounterCollection<EngineCounter>();
            FileSystem = fileSystem;

            var historicData = (IReadOnlyList<HistoricDataPoint>)historicTableSizes ?? CollectionUtilities.EmptyArray<HistoricDataPoint>();
            HistoricTableSizes = new HistoricTableSizes(historicData);
        }

        /// <summary>
        /// Creates a new engine context
        /// </summary>
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "BuildXLContext takes ownership for disposal.")]
        public static EngineContext CreateNew(CancellationToken cancellationToken, PathTable pathTable, IFileSystem fileSystem)
        {
            Contract.Requires(pathTable != null);

            var stringTable = pathTable.StringTable;
            var symbolTable = new SymbolTable(stringTable);
            var tokenTextTable = new TokenTextTable();

            return new EngineContext(cancellationToken, pathTable, symbolTable, new QualifierTable(pathTable.StringTable), fileSystem, tokenTextTable);
        }

        /// <summary>
        /// Creates a new <see cref="FrontEndContext"/> and copies <see cref="PathTable"/>,
        /// <see cref="SymbolTable"/>, and <see cref="CancellationToken"/> over to it.
        /// </summary>
        public FrontEndContext ToFrontEndContext(LoggingContext loggingContext)
        {
            return new FrontEndContext(this, loggingContext, FileSystem);
        }

        /// <inheritdoc/>
        public override void Invalidate()
        {
            base.Invalidate();
            PipDataBuilderPool = null;
        }

        /// <summary>
        /// Returns current statistics.
        /// </summary>
        private HistoricDataPoint GetHistoricDataPoint()
        {
            return new HistoricDataPoint(
                pathTableStats: TableToStats(PathTable),
                symbolTableStats: TableToStats(SymbolTable),
                stringTableStats: new TableStats(count: StringTable.Count, sizeInBytes: StringTable.SizeInBytes));
        }

        private static TableStats TableToStats(HierarchicalNameTable table)
        {
            return new TableStats(count: table.Count, sizeInBytes: table.SizeInBytes);
        }
    }
}

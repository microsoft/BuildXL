// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// A Pip that represents a value
    /// </summary>
    public sealed partial class ValuePip : Pip
    {
        /// <summary>
        /// Symbol
        /// </summary>
        public FullSymbol Symbol { get; }

        /// <summary>
        /// Qualifier
        /// </summary>
        public QualifierId Qualifier { get; }

        /// <summary>
        /// LocationData
        /// </summary>
        public LocationData LocationData { get; }

        /// <summary>
        /// Get the key used to compare the value pip
        /// </summary>
        public (FullSymbol symbol, QualifierId qualifier, AbsolutePath path) Key => (Symbol, Qualifier, LocationData.Path);

        /// <summary>
        /// Constructs a new Value pip
        /// </summary>
        public ValuePip(
            FullSymbol symbol,
            QualifierId qualifier,
            LocationData locationData)
        {
            Contract.Requires(symbol.IsValid);
            Contract.Requires(locationData.IsValid);

            Symbol = symbol;
            Qualifier = qualifier;
            LocationData = locationData;
        }

        /// <summary>
        /// SpecFile
        /// </summary>
        public FileArtifact SpecFile => new FileArtifact(LocationData.Path);

        /// <inheritdoc />
        public override ReadOnlyArray<StringId> Tags => default(ReadOnlyArray<StringId>);

        /// <inheritdoc />
        public override PipProvenance Provenance => null;

        /// <inheritdoc />
        public override PipType PipType => PipType.Value;

        #region Serialization

        /// <inheritdoc />
        internal override void InternalSerialize(PipWriter writer)
        {
            writer.Write(Symbol);
            writer.Write(Qualifier);
            writer.Write(LocationData);
        }

        /// <summary>
        /// Deserialize
        /// </summary>
        internal static ValuePip InternalDeserialize(PipReader reader)
        {
            return new ValuePip(
                symbol: reader.ReadFullSymbol(),
                qualifier: reader.ReadQualifierId(),
                locationData: reader.ReadLocationData());
        }
        #endregion
    }
}

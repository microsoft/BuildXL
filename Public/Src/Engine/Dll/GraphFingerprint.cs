// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.IO;
using BuildXL.Cache.MemoizationStore.Interfaces.Sessions;
using BuildXL.Engine.Cache.Fingerprints;
using BuildXL.Engine.Tracing;
using BuildXL.FrontEnd.Sdk;
using BuildXL.Storage;
using BuildXL.Utilities;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Engine
{
    /// <summary>
    /// Fingerprint of a graph
    /// </summary>
    public sealed class GraphFingerprint
    {
        /// <summary>
        /// The exact fingerprint of the build. This is the fingerprint that must be used when saving the graph
        /// </summary>
        public readonly CompositeGraphFingerprint ExactFingerprint;

        /// <summary>
        /// A compatible fingerprint for the build. This is an alternate graph that may be used when loading the graph
        /// </summary>
        public readonly CompositeGraphFingerprint CompatibleFingerprint;

        /// <summary>
        /// Creates a new GraphFingerprint
        /// </summary>
        public GraphFingerprint(CompositeGraphFingerprint exactFingerprint, CompositeGraphFingerprint compatibleFingerprint)
        {
            ExactFingerprint = exactFingerprint;
            CompatibleFingerprint = compatibleFingerprint;
        }
    }

    /// <summary>
    /// A fingerprint for a graph exposing some of its constituent members for the sake of identifying what changed
    /// </summary>
    public struct CompositeGraphFingerprint : IEquatable<CompositeGraphFingerprint>
    {
        /// <summary>
        /// Default value to use instead of default(CompositeGraphFingerprint) or new CompositeGraphFingerprint().
        /// </summary>
        public static readonly CompositeGraphFingerprint Zero = new CompositeGraphFingerprint
        {
            OverallFingerprint = ContentFingerprint.Zero,
            BuildEngineHash = FingerprintUtilities.ZeroFingerprint,
            ConfigFileHash = FingerprintUtilities.ZeroFingerprint,
            QualifierHash = FingerprintUtilities.ZeroFingerprint,
            FilterHash = FingerprintUtilities.ZeroFingerprint,
        };

        /// <summary>
        /// The overall fingerprint. This may include additional variance beyond what is broken out in other fields of
        /// this struct.
        /// </summary>
        public ContentFingerprint OverallFingerprint { get; set; }

        /// <summary>
        /// Hash of the build engine
        /// </summary>
        public Fingerprint BuildEngineHash { get; set; }

        /// <summary>
        /// Hash of the config file
        /// </summary>
        public Fingerprint ConfigFileHash { get; set; }

        /// <summary>
        /// Hash of qualifiers
        /// </summary>
        public Fingerprint QualifierHash { get; set; }

        /// <summary>
        /// Hash of values
        /// </summary>
        public Fingerprint FilterHash { get; set; }

        /// <summary>
        /// Evaluation filter
        /// </summary>
        public IEvaluationFilter EvaluationFilter { get; set; }

        /// <summary>
        /// Returns a new instance of <see cref="CompositeGraphFingerprint"/> with the same content as the current instance plus with the given <paramref name="filter"/>.
        /// </summary>
        public CompositeGraphFingerprint WithEvaluationFilter(IEvaluationFilter filter)
        {
            return new CompositeGraphFingerprint
            {
                OverallFingerprint = OverallFingerprint,
                BuildEngineHash = BuildEngineHash,
                ConfigFileHash = ConfigFileHash,
                QualifierHash = QualifierHash,
                FilterHash = FilterHash,
                EvaluationFilter = filter,
            };
        }

        /// <summary>
        /// Serializes the object
        /// </summary>
        public void Serialize(BinaryWriter writer)
        {
            OverallFingerprint.WriteTo(writer);
            BuildEngineHash.WriteTo(writer);
            ConfigFileHash.WriteTo(writer);
            QualifierHash.WriteTo(writer);
            FilterHash.WriteTo(writer);

            writer.Write(EvaluationFilter != null);
            EvaluationFilter?.Serialize(writer);
        }

        /// <summary>
        /// Writes as text.
        /// </summary>
        public void WriteText(TextWriter writer)
        {
            writer.WriteLine(I($"Overall fingerprint: {OverallFingerprint.ToString()}"));
            writer.WriteLine(I($"Build engine hash: {BuildEngineHash.ToString()}"));
            writer.WriteLine(I($"Config file hash: {ConfigFileHash.ToString()}"));
            writer.WriteLine(I($"Qualifier hash: {QualifierHash.ToString()}"));
            writer.WriteLine(I($"Filter hash: {FilterHash.ToString()}"));
            

            if (EvaluationFilter != null)
            {
                writer.WriteLine(I($"Filter:"));
                writer.WriteLine(EvaluationFilter.ToDisplayString());
            }
        }

        /// <summary>
        /// Tries to deserialize a graph fingerprint from the <paramref name="reader"/>.
        /// </summary>
        public static CompositeGraphFingerprint Deserialize(BinaryReader reader)
        {
            CompositeGraphFingerprint fingerprint = Zero;

            fingerprint.OverallFingerprint = new ContentFingerprint(reader);
            fingerprint.BuildEngineHash = FingerprintUtilities.CreateFrom(reader);
            fingerprint.ConfigFileHash = FingerprintUtilities.CreateFrom(reader);
            fingerprint.QualifierHash = FingerprintUtilities.CreateFrom(reader);
            fingerprint.FilterHash = FingerprintUtilities.CreateFrom(reader);

            bool filterExists = reader.ReadBoolean();
            if (filterExists)
            {
                fingerprint.EvaluationFilter = BuildXL.FrontEnd.Sdk.EvaluationFilter.Deserialize(reader);
            }

            return fingerprint;
        }

        /// <summary>
        /// Compares to another <see cref="CompositeGraphFingerprint"/>.
        /// </summary>
        public GraphCacheMissReason CompareFingerprint(CompositeGraphFingerprint newFingerprint)
        {
            if (OverallFingerprint == newFingerprint.OverallFingerprint)
            {
                return GraphCacheMissReason.NoMiss;
            }

            if (BuildEngineHash != FingerprintUtilities.ZeroFingerprint && BuildEngineHash != newFingerprint.BuildEngineHash)
            {
                return GraphCacheMissReason.BuildEngineChanged;
            }

            if (ConfigFileHash != FingerprintUtilities.ZeroFingerprint && ConfigFileHash != newFingerprint.ConfigFileHash)
            {
                return GraphCacheMissReason.ConfigFileChanged;
            }

            if (QualifierHash != FingerprintUtilities.ZeroFingerprint && QualifierHash != newFingerprint.QualifierHash)
            {
                return GraphCacheMissReason.QualifierChanged;
            }

            if (FilterHash != FingerprintUtilities.ZeroFingerprint && FilterHash != newFingerprint.FilterHash)
            {
                if (EvaluationFilter == null && newFingerprint.EvaluationFilter != null)
                {
                    // The old graph has no filter but the new one has. This is a graph hit as well, because any filter is a subset of 'no filter'.
                    return GraphCacheMissReason.NoMiss;
                }

                // FilterHash represents a hash of the filter. Need to compare the filters to check, maybe the new filter is just a subset of the old one.
                if (EvaluationFilter != null && newFingerprint.EvaluationFilter != null)
                {
                    // Both old and new filters are presented.
                    if (newFingerprint.EvaluationFilter.IsSubSetOf(EvaluationFilter))
                    {
                        return GraphCacheMissReason.NoMiss;
                    }
                }

                return GraphCacheMissReason.EvaluationFilterChanged;
            }

            return GraphCacheMissReason.FingerprintChanged;
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return OverallFingerprint.GetHashCode();
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <summary>
        /// Indicates whether two object instances are equal.
        /// </summary>
        public static bool operator ==(CompositeGraphFingerprint left, CompositeGraphFingerprint right)
        {
            return left.Equals(right);
        }

        /// <summary>
        /// Indicates whether two objects instances are not equal.
        /// </summary>
        public static bool operator !=(CompositeGraphFingerprint left, CompositeGraphFingerprint right)
        {
            return !left.Equals(right);
        }

        /// <summary>
        /// Whether a ValueDependency equals this one
        /// </summary>
        public bool Equals(CompositeGraphFingerprint other)
        {
            return OverallFingerprint == other.OverallFingerprint;
        }
    }
}

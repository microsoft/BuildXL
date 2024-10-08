// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using System.Text;
using BuildXL.Scheduler.Fingerprints;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Configuration;
using static BuildXL.Utilities.Core.FormattableStringEx;

namespace BuildXL.Scheduler.IncrementalScheduling
{
    /// <summary>
    /// Graph-agnostic incremental scheduling state id.
    /// </summary>
    /// <remarks>
    /// Subst source and targets, are included in the identity to avoid underbuild.
    /// Graph-agnostic incremental scheduling relies on pip static fingerprints for pip identification. Such fingerprints
    /// only use file/directory paths (and not their contents) as parts of the fingerprints. Suppose that there are
    /// two identical repos, one under D:\Repo1\ and the other under D:\Repo2\. The subst target is the same, i.e., B:\, but
    /// the subst sources are different. Suppose further that one build D:\Repo1\ first, and wants to use the incremental
    /// scheduling state for building D:\Repo2\. But now, because pips in D:\Repo2\ have the same fingerprints as those
    /// in D:\Repo1\, according to graph-agnostic state, all pips in D:\Repo2\ are all clean; but nothing has been built
    /// in D:\Repo2\.
    /// </remarks>
    public sealed class GraphAgnosticIncrementalSchedulingStateId : IEquatable<GraphAgnosticIncrementalSchedulingStateId>
    {
        private readonly string m_machineName;
        private readonly string m_substSource;
        private readonly string m_substTarget;
        private readonly PreserveOutputsInfo m_preserveOutputSalt;
        private readonly string m_observationRulesSalt;
        private readonly int m_hashCode;

        private GraphAgnosticIncrementalSchedulingStateId(
            string machineName,
            string substSource,
            string substTarget,
            PreserveOutputsInfo preserveOutputSalt,
            string observationRulesHash)
        {
            Contract.Requires(machineName != null);

            m_machineName = machineName;
            m_substSource = substSource;
            m_substTarget = substTarget;
            m_preserveOutputSalt = preserveOutputSalt;
            m_observationRulesSalt = observationRulesHash;
            m_hashCode = ComputeHashCode();
        }

        /// <summary>
        /// Checks for equality.
        /// </summary>
        public bool Equals(GraphAgnosticIncrementalSchedulingStateId otherId) => EqualsModuloPreserveOutputSalt(otherId) && m_preserveOutputSalt == otherId.m_preserveOutputSalt;

        /// <summary>
        /// Checks if this instance of id is as safe or safer than the other instance.
        /// </summary>
        public bool IsAsSafeOrSaferThan(GraphAgnosticIncrementalSchedulingStateId otherId)
        {
            return EqualsModuloPreserveOutputSalt(otherId) 
                && (m_preserveOutputSalt.IsAsSafeOrSaferThan(otherId.m_preserveOutputSalt) || m_preserveOutputSalt == UnsafeOptions.PreserveOutputsNotUsed);

        }

        private bool EqualsModuloPreserveOutputSalt(GraphAgnosticIncrementalSchedulingStateId otherId)
        {
            return otherId != null
                && otherId.m_observationRulesSalt == m_observationRulesSalt
                && string.Equals(m_machineName, otherId.m_machineName, StringComparison.Ordinal)
                && string.Equals(m_substSource, otherId.m_substSource, OperatingSystemHelper.PathComparison)
                && string.Equals(m_substTarget, otherId.m_substTarget, OperatingSystemHelper.PathComparison);
        }

        /// <summary>
        /// Creates an instance of <see cref="GraphAgnosticIncrementalSchedulingStateId" /> from <see cref="IConfiguration" />.
        /// </summary>
        public static GraphAgnosticIncrementalSchedulingStateId Create(PathTable pathTable, IConfiguration configuration, PreserveOutputsInfo preserveOutputSalt)
        {
            return new GraphAgnosticIncrementalSchedulingStateId(
                Environment.MachineName,
                configuration.Logging.SubstSource.ToString(pathTable).ToCanonicalizedPath(),
                configuration.Logging.SubstTarget.ToString(pathTable).ToCanonicalizedPath(),
                preserveOutputSalt,
                ObservationReclassifier.ComputeObservationReclassificationRulesHash(configuration).ToString());
        }

        /// <inheritdoc />
        public override bool Equals(object obj) => Equals(obj as GraphAgnosticIncrementalSchedulingStateId);

        /// <inheritdoc />
        public override int GetHashCode() => m_hashCode;

        private int ComputeHashCode()
        {
            return HashCodeHelper.Combine(
                m_machineName.GetHashCode(),
                m_substSource.GetHashCode(),
                m_substTarget.GetHashCode(),
                m_preserveOutputSalt.GetHashCode(),
                m_observationRulesSalt.GetHashCode());
        }

        /// <summary>
        /// Serializes <see cref="GraphAgnosticIncrementalSchedulingStateId" />.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            writer.Write(m_machineName);
            writer.Write(m_substSource);
            writer.Write(m_substTarget);
            m_preserveOutputSalt.Serialize(writer);
            writer.Write(m_observationRulesSalt);
        }

        /// <summary>
        /// Deserializes into <see cref="GraphAgnosticIncrementalSchedulingStateId" />.
        /// </summary>
        public static GraphAgnosticIncrementalSchedulingStateId Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            var machineName = reader.ReadString();
            var substSource = reader.ReadString();
            var substTarget = reader.ReadString();
            var preserveOutputSalt = new PreserveOutputsInfo(reader);
            var reclassificationRulesSalt = reader.ReadString();

            return new GraphAgnosticIncrementalSchedulingStateId(
                machineName,
                substSource,
                substTarget,
                preserveOutputSalt,
                reclassificationRulesSalt);
        }

        /// <summary>
        /// Gets the string representation of <see cref="GraphAgnosticIncrementalSchedulingStateId"/>.
        /// </summary>
        public override string ToString()
        {
            var stringBuilder = new StringBuilder();
            stringBuilder.AppendLine("[");
            stringBuilder.AppendLine(I($"\tMachine name         : {m_machineName}"));
            stringBuilder.AppendLine(I($"\tSubst source         : {m_substSource}"));
            stringBuilder.AppendLine(I($"\tSubst target         : {m_substTarget}"));
            stringBuilder.AppendLine(I($"\tPreserve output salt : {m_preserveOutputSalt.ToString()}"));
            stringBuilder.AppendLine(I($"\tObservation rules salt : {m_observationRulesSalt.ToString()}"));
            stringBuilder.AppendLine("]");

            return stringBuilder.ToString();
        }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using BuildXL.Pips;
using BuildXL.Utilities;
using static BuildXL.Scheduler.FileMonitoringViolationAnalyzer;

namespace BuildXL.Scheduler
{
    /// <summary>
    /// A file access violation for reporting
    /// </summary>
    public readonly struct ReportedViolation : IEquatable<ReportedViolation>
    {
        /// <nodoc/>
        public readonly bool IsError;

        /// <nodoc/>
        public readonly PipId? RelatedPipId;

        /// <nodoc/>
        public readonly AbsolutePath Path;

        /// <nodoc/>
        public readonly DependencyViolationType Type;

        /// <nodoc/>
        public readonly PipId ViolatorPipId;

        /// <nodoc/>
        public readonly AbsolutePath ProcessPath;

        /// <nodoc/>
        public readonly bool ViolationMakesPipUncacheable;

        /// <nodoc/>
        public ReportedViolation(bool isError, DependencyViolationType type, AbsolutePath path, PipId violatorPipId, PipId? relatedPipId, AbsolutePath processPath, bool violationMakesPipUncacheable = true)
        {
            IsError = isError;
            Type = type;
            Path = path;
            ViolatorPipId = violatorPipId;
            RelatedPipId = relatedPipId;
            ProcessPath = processPath;
            ViolationMakesPipUncacheable = violationMakesPipUncacheable;
        }

        #region MessageRendering
        /// <summary>
        /// A simplified violation type for sake of displaying in the DisallowedFileAccess summary message
        /// </summary>
        public SimplifiedViolationType ReportingType
        {
            get
            {
                switch (Type)
                {
                    case DependencyViolationType.DoubleWrite:
                        return SimplifiedViolationType.DoubleWrite;
                    case DependencyViolationType.ReadRace:
                    case DependencyViolationType.UndeclaredOrderedRead:
                    case DependencyViolationType.MissingSourceDependency:
                    case DependencyViolationType.UndeclaredReadCycle:
                    case DependencyViolationType.ReadUndeclaredOutput:
                        return SimplifiedViolationType.Read;
                    case DependencyViolationType.UndeclaredOutput:
                    case DependencyViolationType.WriteInSourceSealDirectory:
                    case DependencyViolationType.WriteInUndeclaredSourceRead:
                    case DependencyViolationType.WriteInExistingFile:
                    case DependencyViolationType.WriteToTempPathInsideSharedOpaque:
                    case DependencyViolationType.WriteOnAbsentPathProbe:
                        return SimplifiedViolationType.Write;
                    case DependencyViolationType.AbsentPathProbeUnderUndeclaredOpaque:
                        return SimplifiedViolationType.Probe;
                    default:
                        throw new NotImplementedException("Need to implement for: " + Type.ToString());
                }
            }
        }

        /// <summary>
        /// Renders the violation for display in the DFA summary
        /// </summary>
        public string RenderForDFASummary(PathTable pathTable)
        {
            return $" {ReportingType.ToAbbreviation()} {Path.ToString(pathTable)}";
        }

        /// <summary>
        /// Legend informaiton to display in the DFA summary
        /// </summary>
        public string LegendText
        {
            get
            {
                switch (ReportingType)
                {
                    case SimplifiedViolationType.Probe:
                        return $"{ReportingType.ToAbbreviation()} = Probe to an absent path";
                    case SimplifiedViolationType.Read:
                        return $"{ReportingType.ToAbbreviation()} = Read";
                    case SimplifiedViolationType.Write:
                        return $"{ReportingType.ToAbbreviation()} = Write";
                    case SimplifiedViolationType.DoubleWrite:
                        return $"{ReportingType.ToAbbreviation()} = Double Write";
                    default:
                        throw new NotImplementedException("Need to implement for: " + ReportingType.ToString());
                }
            }
        }

        #endregion

        #region IEquatable
        /// <nodoc/>
        public bool Equals(ReportedViolation other)
        {
            return IsError == other.IsError &&
                RelatedPipId?.Value == other.RelatedPipId?.Value &&
                Path == other.Path &&
                Type == other.Type &&
                ViolatorPipId == other.ViolatorPipId &&
                ProcessPath == other.ProcessPath &&
                ViolationMakesPipUncacheable == other.ViolationMakesPipUncacheable;
        }

        /// <nodoc/>
        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        /// <nodoc/>
        public static bool operator ==(ReportedViolation left, ReportedViolation right)
        {
            return left.Equals(right);
        }

        /// <nodoc/>
        public static bool operator !=(ReportedViolation left, ReportedViolation right)
        {
            return !left.Equals(right);
        }

        /// <nodoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(
                IsError.GetHashCode(),
                RelatedPipId.GetHashCode(),
                Path.GetHashCode(),
                Type.GetHashCode(),
                ViolatorPipId.GetHashCode(),
                ProcessPath.GetHashCode(),
                ViolationMakesPipUncacheable.GetHashCode());
        }
        #endregion
    }

    /// <summary>
    /// A simplified summary of the violation that controls what gets rendered in the DFA summary message
    /// </summary>
    /// <remarks>
    /// These need to remain ordered in terms of increasing precedence 
    /// </remarks>
    public enum SimplifiedViolationType : byte
    {
        /// <nodoc/>
        Probe = 0,

        /// <nodoc/>
        Read = 1,

        /// <nodoc/>
        Write = 2,

        /// <nodoc/>
        DoubleWrite = 3,
    }

    /// <nodoc/>
    public static class SimplifiedViolationTypeExtensions
    {
        /// <summary>
        /// Gets an abbreviation for the violation type. The abbreviation is guarenteed to be a fixed width
        /// with respect to other abbreviations
        /// </summary>
        public static string ToAbbreviation(this SimplifiedViolationType violation)
        {
            switch (violation)
            {
                case SimplifiedViolationType.Probe:
                    return "P ";
                case SimplifiedViolationType.Read:
                    return "R ";
                case SimplifiedViolationType.Write:
                    return "W ";
                case SimplifiedViolationType.DoubleWrite:
                    return "DW";
                default:
                    throw new NotImplementedException("Need to implement for: " + violation.ToString());
            }
        }
    }
}
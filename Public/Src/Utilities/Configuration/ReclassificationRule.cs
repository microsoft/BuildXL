// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using BuildXL.Utilities.Core;
using BuildXL.Utilities.Collections;
using System.Linq;
using System;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// CODESYNC: These types correspond to their counterparts in DScript defined in Prelude.Configuration.dsc
    /// </summary>
    public enum ObservationType
    {
        /// <summary>
        /// A path was probed, but did not exist.
        /// </summary>
        AbsentPathProbe,

        /// <summary>
        /// A file with known contents was read.
        /// </summary>
        FileContentRead,

        /// <summary>
        /// A directory was enumerated (kind of like a directory read).
        /// </summary>
        DirectoryEnumeration,

        /// <summary>
        /// An existing directory probe.
        /// </summary>
        ExistingDirectoryProbe,

        /// <summary>
        /// An existing file probe.
        /// </summary>
        ExistingFileProbe,

        /// <summary>
        /// This special value is used as a wildcard and matches any other type
        /// </summary>
        All
    }

    /// <summary>
    /// A rule to reclassify observations
    /// </summary>
    public interface IReclassificationRule : IReclassificationRuleData
    {
        /// <summary>
        /// A unique descriptor for this rule, used for fingerprinting
        /// </summary>
        public string Descriptor();

        /// <nodoc />
        public void Serialize(BuildXLWriter writer);
    }

    /// <nodoc />
    public class ReclassificationRule : IReclassificationRule
    {
        /// <inheritdoc />
        public string Name { get; init; }

        /// <inheritdoc />
        public string PathRegex { get; init; }

        /// <inheritdoc />
        public IReadOnlyList<ObservationType> ResolvedObservationTypes { get; init; }

        /// <inheritdoc />
        public DiscriminatingUnion<ObservationType, UnitValue> ReclassifyTo { get; init; }

        /// <inheritdoc />
        public void Serialize(BuildXLWriter writer)
        {
            writer.WriteNullableString(Name);
            writer.Write(PathRegex);
            writer.Write(ResolvedObservationTypes != null);
            if (ResolvedObservationTypes != null)
            {
                writer.Write(ResolvedObservationTypes.ToReadOnlyArray(), (w, v) => w.Write((int)v));
            }

            object executablePathType = ReclassifyTo?.GetValue();
            switch (executablePathType)
            {
                case null:
                    writer.Write((byte)0);
                    break;
                case ObservationType observationType:
                    writer.Write((byte)1);
                    writer.Write((byte)observationType);
                    break;
                case UnitValue:
                    writer.Write((byte)2);
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected value for executablePathType: {executablePathType}");
            }
        }

        /// <nodoc />
        public static IReclassificationRule Deserialize(BuildXLReader reader)
        {
            var name = reader.ReadNullableString();
            var pathRegex = reader.ReadString();
            var observationTypes = reader.ReadBoolean() ? reader.ReadReadOnlyArray(r => (ObservationType)r.ReadInt32()) : null;


            var reclassifyToType = reader.ReadByte();
            DiscriminatingUnion<ObservationType, UnitValue> reclassifyTo = null;

            switch (reclassifyToType)
            {
                case 0:
                    reclassifyTo = null;
                    break;
                case 1:
                    ObservationType observationType = (ObservationType)reader.ReadByte();
                    reclassifyTo = new DiscriminatingUnion<ObservationType, UnitValue>(observationType);
                    break;
                case 2:
                    reclassifyTo = new DiscriminatingUnion<ObservationType, UnitValue>(UnitValue.Unit);
                    break;
                default:
                    throw new InvalidOperationException($"Unexpected value for executablePathType: {reclassifyToType}");
            }

            return new ReclassificationRule()
            {
                Name = name,
                PathRegex = pathRegex,
                ResolvedObservationTypes = observationTypes,
                ReclassifyTo = reclassifyTo
            };
        }

        /// <inheritdoc />
        public string Descriptor()
        {
            // This text is included in cache miss analysis, so let's make it 'readable'.

            var nameDescriptor = Name?.ToUpper() ?? "NULL";
            var fromDescriptor = string.Join(",", ResolvedObservationTypes?.SelectArray(r => r.ToString()).OrderBy(static r => r)) ?? string.Empty;
            string reclassifyToDescriptor = "NULL";
            if (ReclassifyTo != null)
            {
                if (ReclassifyTo.GetValue() is ObservationType observationType)
                {
                    reclassifyToDescriptor = observationType.ToString();
                }
                else // Unit means ignore
                {
                    reclassifyToDescriptor = "Ignore";
                }
            }

            return $"Name:{nameDescriptor}|Regex:{PathRegex}|From:{fromDescriptor}|To:{reclassifyToDescriptor}";
        }
    }
}

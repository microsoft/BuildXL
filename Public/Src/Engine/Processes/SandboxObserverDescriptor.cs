// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// Descriptor for observer.
    /// </summary>
    public class SandboxObserverDescriptor
    {
        /// <summary>
        /// Warning regex.
        /// </summary>
        public ExpandedRegexDescriptor WarningRegex { get; set; }

        /// <summary>
        /// Logs output to console.
        /// </summary>
        public bool LogOutputToConsole { get; set; }

        /// <summary>
        /// Logs error to console.
        /// </summary>
        public bool LogErrorToConsole { get; set; }

        /// <summary>
        /// Serializes this instance to a given <paramref name="writer"/>.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            writer.Write(WarningRegex, (w, v) => v.Serialize(w));
            writer.Write(LogOutputToConsole);
            writer.Write(LogErrorToConsole);
        }

        /// <summary>
        /// Deserializes an instance of <see cref="SandboxObserverDescriptor"/>.
        /// </summary>
        public static SandboxObserverDescriptor Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            return new SandboxObserverDescriptor()
            {
                WarningRegex = reader.ReadNullable(r => ExpandedRegexDescriptor.Deserialize(r)),
                LogOutputToConsole = reader.ReadBoolean(),
                LogErrorToConsole = reader.ReadBoolean()
            };
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return !(obj is null) && (ReferenceEquals(this, obj) || ((obj is SandboxObserverDescriptor descriptor) && Equals(descriptor)));
        }

        /// <summary>
        /// Checks for equality.
        /// </summary>
        public bool Equals(SandboxObserverDescriptor descriptor)
        {
            return !(descriptor is null)
                && (ReferenceEquals(this, descriptor)
                    || (LogErrorToConsole == descriptor.LogErrorToConsole && LogOutputToConsole == descriptor.LogOutputToConsole && WarningRegex == descriptor.WarningRegex));
        }

        /// <summary>
        /// Checks for equality.
        /// </summary>
        public static bool operator ==(SandboxObserverDescriptor descriptor1, SandboxObserverDescriptor descriptor2)
        {
            if (ReferenceEquals(descriptor1, descriptor2))
            {
                return true;
            }

            if (descriptor1 is null)
            {
                return false;
            }

            return descriptor1.Equals(descriptor2);
        }

        /// <summary>
        /// Checks for disequality.
        /// </summary>
        public static bool operator !=(SandboxObserverDescriptor descriptor1, SandboxObserverDescriptor descriptor2) => !(descriptor1 == descriptor2);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(WarningRegex.GetHashCode(), LogErrorToConsole ? 1 : 0, LogOutputToConsole ? 1 : 0);
        }
    }
}

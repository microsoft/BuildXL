// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using System.Text.RegularExpressions;
using BuildXL.Utilities;

namespace BuildXL.Processes
{
    /// <summary>
    /// Regex descriptor.
    /// </summary>
    public class ExpandedRegexDescriptor
    {
        /// <summary>
        /// Pattern.
        /// </summary>
        public string Pattern { get; }

        /// <summary>
        /// Regex option.
        /// </summary>
        public RegexOptions Options { get; }

        /// <summary>
        /// Creates an instance of <see cref="ExpandedRegexDescriptor"/>.
        /// </summary>
        public ExpandedRegexDescriptor(string pattern, RegexOptions options)
        {
            Contract.Requires(pattern != null);

            Pattern = pattern;
            Options = options;
        }

        /// <summary>
        /// Serializes this instance to a given <paramref name="writer"/>.
        /// </summary>
        public void Serialize(BuildXLWriter writer)
        {
            Contract.Requires(writer != null);

            writer.Write(Pattern);
            writer.Write((uint)Options);
        }

        /// <summary>
        /// Deserializes an instance of <see cref="ExpandedRegexDescriptor"/>.
        /// </summary>
        public static ExpandedRegexDescriptor Deserialize(BuildXLReader reader)
        {
            Contract.Requires(reader != null);

            return new ExpandedRegexDescriptor(pattern: reader.ReadString(), options: (RegexOptions)reader.ReadUInt32());
        }

        /// <inheritdoc />
        public override bool Equals(object obj)
        {
            return !(obj is null) && (ReferenceEquals(this, obj) || ((obj is ExpandedRegexDescriptor descriptor) && Equals(descriptor)));
        }

        /// <summary>
        /// Checks for equality.
        /// </summary>
        public bool Equals(ExpandedRegexDescriptor descriptor)
        {
            return !(descriptor is null)
                && (ReferenceEquals(this, descriptor)
                    || (string.Equals(Pattern, descriptor.Pattern) && Options == descriptor.Options));
        }

        /// <summary>
        /// Checks for equality.
        /// </summary>
        public static bool operator ==(ExpandedRegexDescriptor descriptor1, ExpandedRegexDescriptor descriptor2)
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
        public static bool operator !=(ExpandedRegexDescriptor descriptor1, ExpandedRegexDescriptor descriptor2) => !(descriptor1 == descriptor2);

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Pattern.GetHashCode(), (int)Options);
        }
    }
}

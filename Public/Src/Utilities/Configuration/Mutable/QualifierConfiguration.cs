// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;

namespace BuildXL.Utilities.Configuration.Mutable
{
    /// <nodoc />
    public sealed class QualifierConfiguration : IQualifierConfiguration
    {
        /// <nodoc />
        public QualifierConfiguration()
        {
            DefaultQualifier = new Dictionary<string, string>();
            NamedQualifiers = new Dictionary<string, IReadOnlyDictionary<string, string>>();
        }

        /// <nodoc />
        public QualifierConfiguration(IQualifierConfiguration template)
        {
            Contract.Assume(template != null);

            DefaultQualifier = new Dictionary<string, string>();
            foreach (var kv in template.DefaultQualifier)
            {
                DefaultQualifier.Add(kv.Key, kv.Value);
            }

            NamedQualifiers = new Dictionary<string, IReadOnlyDictionary<string, string>>();
            foreach (var kv in template.NamedQualifiers)
            {
                NamedQualifiers.Add(kv.Key, kv.Value);
            }
        }

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public Dictionary<string, string> DefaultQualifier { get; set; }

        /// <inheritdoc />
        IReadOnlyDictionary<string, string> IQualifierConfiguration.DefaultQualifier => DefaultQualifier;

        /// <nodoc />
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public Dictionary<string, IReadOnlyDictionary<string, string>> NamedQualifiers { get; set; }

        /// <inheritdoc />
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> IQualifierConfiguration.NamedQualifiers => NamedQualifiers;
    }
}

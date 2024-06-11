// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.ContractsLight;
using System.Linq;
using System.Net;
using BuildXL.Utilities.Collections;

namespace BuildXL.Utilities
{
    /// <summary>
    /// This class provides a centralized source to manage utility functionality associated with the /pipProperty flag.
    /// This includes various methods and properties to manipulate and retrieve information and settings related to these pip properties.
    /// </summary>
    public class PipSpecificPropertiesConfig
    {
        /// <summary>
        /// Maps pipSpecificProperties with their respective semistablehashes and property values.
        /// </summary>
        private IReadOnlyDictionary<PipSpecificProperty, IReadOnlyDictionary<long, string>> m_semistableHashesPropertiesAndValues;

        private static readonly IReadOnlyDictionary<PipSpecificProperty, IReadOnlyDictionary<long, string>> s_emptyPipPropertiesAndValues = new ReadOnlyDictionary<PipSpecificProperty, IReadOnlyDictionary<long, string>>(new Dictionary<PipSpecificProperty, IReadOnlyDictionary<long, string>>());

        /// <summary>
        /// List of pip specific properties which are allowed to be passed via /pipProperty flag.
        /// </summary>
        public enum PipSpecificProperty
        {
            /// <summary>
            /// Semistable hashes that will be forced to have cache misses.
            /// </summary>
            ForcedCacheMiss = 1,

            /// <summary>
            /// Semistable hashes for pips which will have verbose sandbox logging enabled. 
            /// </summary>
            EnableVerboseProcessLogging = 2,

            /// <summary>
            /// Enables fingerprint salting for specific pips.
            /// Ex: /pipProperty:Pip00000[PipFingerprintSalt=tooSalty]
            /// </summary>
            PipFingerprintSalt = 3,

            /// <summary>
            /// Enable verbose tracing for ObservedInputProcessor. Debugging / investigation purposes. 
            /// </summary>
            ObservedInputProcessorTracing = 4,
        }

        /// <summary>
        /// For a given property, retrieves a set of PipIds associated with the property.
        /// </summary>
        public IEnumerable<long> GetPipIdsForProperty(PipSpecificProperty pipProperty) =>
            m_semistableHashesPropertiesAndValues.TryGetValue(pipProperty, out var semistableHashesAndValues) ? semistableHashesAndValues.Keys : Enumerable.Empty<long>();

        /// <summary>
        /// Retrieves the property value for a given property and semi-stable hash.
        /// Example: /Pip000019[foo=bar], in such cases, we can pass the property we want to query along with the pipId to get the value.
        /// </summary>
        public string GetPipSpecificPropertyValue(PipSpecificProperty propertyName, long semiStableHash)
        {
            if (m_semistableHashesPropertiesAndValues.TryGetValue(propertyName, out var semistableHashesAndValues))
            {
                if (semistableHashesAndValues.TryGetValue(semiStableHash, out var pipValue))
                {
                    return pipValue;
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// Checks if the specified semi-stable hash has the specified property or not.
        /// </summary>
        public bool PipHasProperty(PipSpecificProperty propertyName, long semiStableHash) =>
            RetrievePipSemistableHashesWithValues(propertyName).ContainsKey(semiStableHash);
 
        /// <summary>
        /// Obtains a map of PipIds and their respective values for a given pipSpecificProperty
        /// </summary>
        public IReadOnlyDictionary<long, string> RetrievePipSemistableHashesWithValues(PipSpecificProperty propertyName)
        {
            if (m_semistableHashesPropertiesAndValues.TryGetValue(propertyName, out var semistableHashesAndValues))
            {
                return semistableHashesAndValues;
            }
            return CollectionUtilities.EmptyDictionary<long, string>();
        }

        /// <summary>
        /// Create an instance of PipSpecificPropertiesConfig.
        /// </summary>
        public PipSpecificPropertiesConfig(IReadOnlyList<PipSpecificPropertyAndValue> pipPropertiesAndValues)
        {
            m_semistableHashesPropertiesAndValues = s_emptyPipPropertiesAndValues;
            // Maps propertyName with respective PipSemiStableHashes and their values.
            // Grouping it here additionally by PipSemiStableHash to ensure the latest value of for that is obtained.
            // Example: /pipProperty:Pip0000000[foo=bar] /pipProperty:Pip0000000[foo=stool]
            // In this case it picks stool as the property value for that entry.
            if (pipPropertiesAndValues != null)
            {
                m_semistableHashesPropertiesAndValues = pipPropertiesAndValues.GroupBy(pipSpecificPropertyAndValue => pipSpecificPropertyAndValue.PropertyName)
                             .ToDictionary(
                                      entry => entry.Key,
                                      entry => (IReadOnlyDictionary<long, string>)new Dictionary<long, string>(
                                                     entry.GroupBy(pipSpecificPropertyAndValue => pipSpecificPropertyAndValue.PipSemiStableHash)
                                                                     .ToDictionary(
                                                                                   semistableHashesAndValues => semistableHashesAndValues.Key,
                                                                                     semistableHashesAndValues => semistableHashesAndValues.Last().PropertyValue)));
            } 
        }
    }
}

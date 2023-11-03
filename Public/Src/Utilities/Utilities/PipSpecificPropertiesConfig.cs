// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
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
        /// Maps pipProperties with their respective semistable hashes and property values.
        /// </summary>
        private readonly Dictionary<PipSpecificProperty, IReadOnlyList<(long PipSemiStableHash, string PropertyValue)>> m_pipIdPropertiesAndValues;

        /// <summary>
        /// Maintains a set of pipProperties and pipSemistableHash values of the pip.
        /// </summary>
        private readonly Dictionary<PipSpecificProperty, IReadOnlySet<long>> m_propertiesAndIds;

        private static readonly IReadOnlySet<long> s_emptySet = new HashSet<long>().ToReadOnlySet();

        /// <summary>
        /// List of pip specific properties which are allowed to be passed via /pipProperty flag.
        /// </summary>
        public enum PipSpecificProperty
        {
            /// <summary>
            /// Semistable hashes that will be forced to have cache misses.
            /// </summary>
            ForcedCacheMiss,
            /// <summary>
            /// Semistable hashes for pips which will have verbose sandbox logging enableed. 
            /// </summary>
            Debug_EnableVerboseProcessLogging
        }

        /// <summary>
        /// For a given property, retrieves a set of PipIds associated with the property.
        /// </summary>
        public IReadOnlySet<long> GetPipIdsForProperty(PipSpecificProperty pipProperty)
        {
            if (m_propertiesAndIds.TryGetValue(pipProperty, out var pipIds))
            {
                return pipIds;
            }
            return s_emptySet;
        }

        /// <summary>
        /// Retrieves the property value for a given property and semistablehash.
        /// Example: /Pip000019[foo=bar], in such case we can pass the property we want to query along with the pipId to get the value.
        /// </summary>
        public string GetPropertyValue(PipSpecificProperty propertyName, long semiStableHash)
        {
            if (m_pipIdPropertiesAndValues.TryGetValue(propertyName, out var pipIdsAndValues))
            {
                foreach (var pipIdAndValue in pipIdsAndValues)
                {
                    if (pipIdAndValue.PipSemiStableHash.Equals(semiStableHash))
                    {
                        return pipIdAndValue.PropertyValue;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Checks if the specified SemistableHash has the property or not.
        /// </summary>
        public bool PipHasProperty(PipSpecificProperty propertyName, long semiStableHash)
        {
            if (m_propertiesAndIds.TryGetValue(propertyName, out var pipIds))
            {
                return pipIds.Contains(semiStableHash);
            }
            return false;
        }

        /// <summary>
        /// Create an instance of PipSpecificPropertiesConfig.
        /// </summary>
        public PipSpecificPropertiesConfig(IReadOnlyList<PipSpecificPropertyAndValue> pipPropertiesAndValues) 
        {
            m_pipIdPropertiesAndValues = new Dictionary<PipSpecificProperty, IReadOnlyList<(long, string)>>();
            m_propertiesAndIds = new Dictionary<PipSpecificProperty, IReadOnlySet<long>>();
            if (pipPropertiesAndValues != null && pipPropertiesAndValues.Count() > 0)
            {
                // Maps propertyName with respective PipSemiStableHashes and their values.
                // Grouping it here additionally by PipSemiStableHash to ensure the latest value of for that is obtained.
                // Example: /pipProperty:Pip0000000[foo=bar] /pipProperty:Pip0000000[foo=stool]
                // In this case it picks stool as the property value for that entry.
                m_pipIdPropertiesAndValues = pipPropertiesAndValues
                                                .GroupBy(pipSpecificPropertyAndValue => pipSpecificPropertyAndValue.PropertyName)
                                                .ToDictionary(
                                                    entry => entry.Key,
                                                    entry => (IReadOnlyList<(long, string)>)entry
                                                              .GroupBy(pipSpecificPropertyAndValue => pipSpecificPropertyAndValue.PipSemiStableHash)
                                                              .Select(idGroup => idGroup.Last())
                                                              .Select(pipSpecificPropertyAndValue => (pipSpecificPropertyAndValue.PipSemiStableHash, pipSpecificPropertyAndValue.PropertyValue))
                                                              .ToList()
                                                              .AsReadOnly());

                m_propertiesAndIds = m_pipIdPropertiesAndValues.ToDictionary(
                                                                             entry => entry.Key,
                                                                             entry => (IReadOnlySet<long>)entry.Value.Select(item => item.PipSemiStableHash).ToReadOnlySet());                
            }
        }
    }
}

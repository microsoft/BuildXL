// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Reflection;

namespace BuildXL.Cache.ImplementationSupport
{
    /// <summary>
    /// Abstract base class for counters that provides the export operation
    /// </summary>
    public abstract class BaseCounters
    {
        /// <summary>
        /// Combine an optional prefix with the adjusted name of a field
        /// into a single string name
        /// </summary>
        /// <param name="prefix">Optional prefix</param>
        /// <param name="field">The field from which to get the name</param>
        /// <returns>A string with the optional prefix prepended</returns>
        /// <remarks>
        /// The separation character between prefix and name is "_"
        ///
        /// The name is adjusted from the field name by removing the "m_"
        /// and uppercasing the first character.  If the name does not
        /// start with "m_" it is used unchanged.
        /// </remarks>
        private static string CombinePrefix(string prefix, FieldInfo field)
        {
            string name = field.Name;

            if (name.Length > 2 && name.StartsWith(@"m_", StringComparison.Ordinal))
            {
                // Uppercase the first character of the name after the "m_"
                name = char.ToUpperInvariant(name[2]) + name.Substring(3);
            }

            if (string.IsNullOrEmpty(prefix))
            {
                return name;
            }

            return prefix + "_" + name;
        }

        /// <summary>
        /// Export the counters into the given dictionary
        /// </summary>
        /// <param name="output">The dictionary to fill in</param>
        /// <param name="prefix">The prefix for these counters, null for no prefix</param>
        /// <remarks>
        /// This automatically walks counters and pulls the values into the dictionary
        /// as they are found.  The dictionary keys are made up of the value field names
        /// with the "m_" removed and the first character upper-cased for those that are
        /// of that form.
        ///
        /// This works on fields that are double, long, SafeDouble, and SafeLong plus
        /// recurses into nested BaseCounter fields
        /// </remarks>
        public void Export(Dictionary<string, double> output, string prefix)
        {
            foreach (FieldInfo field in GetType().GetFields(BindingFlags.DeclaredOnly | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
            {
                var fieldValue = field.GetValue(this);
                string name = CombinePrefix(prefix, field);

                if (field.FieldType == typeof(SafeDouble))
                {
                    output[name] = ((SafeDouble)fieldValue).Value;
                }
                else if (field.FieldType == typeof(SafeLong))
                {
                    output[name] = ((SafeLong)fieldValue).Value;
                }
                else if (field.FieldType == typeof(double))
                {
                    output[name] = (double)fieldValue;
                }
                else if (field.FieldType == typeof(long))
                {
                    output[name] = (long)fieldValue;
                }
                else
                {
                    (fieldValue as BaseCounters)?.Export(output, name);
                }
            }
        }
    }
}

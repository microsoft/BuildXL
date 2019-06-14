// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Linq;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Various utility methods/classes related to handling system environment variables.
    /// </summary>
    public static class BuildParameters
    {
        // ==================================================================================
        // == Public Constants
        // ==================================================================================

        /// <summary>
        /// Name of the (psuedo-)environment variable indicating if the build is running 'elevated' or not.
        /// </summary>
        public const string IsElevatedVariableName = "BUILDXL_IS_ELEVATED";

        /// <summary>
        /// Name of the (psuedo-)environment variable indicating if the build is running in CloudBuild or not.
        /// </summary>
        /// <remarks>
        /// This environment variable is meant to replace the boolean switch in the configuration. This environment
        /// variable allows the frontend to track, in the input tracker, whether the build is performed in CloudBuild or not.
        /// </remarks>
        public const string IsInCloudBuildVariableName = "BUILDXL_IS_IN_CLOUDBUILD";

        /// <summary>
        /// These environment variables should not be read from config, since
        /// they refer to temporary directories that we reserve the right to redirect.
        /// </summary>
        // TODO: this should have the same value for all platforms
        public static readonly IReadOnlyList<string> DisallowedTempVariables = OperatingSystemHelper.IsUnixOS
            ? new[] { "TEMP", "TMP", "TMPDIR" }
            : new[] { "TEMP", "TMP" };

        // ==================================================================================
        // == Public Types
        // ==================================================================================

        /// <summary>
        ///     Callback type, used to report cases of duplicate build parameters.
        /// </summary>
        /// <param name="key"><see cref="DuplicateBuildParameter.Key"/>.</param>
        /// <param name="firstValue"><see cref="DuplicateBuildParameter.Value1"/>.</param>
        /// <param name="secondValue"><see cref="DuplicateBuildParameter.Value2"/>.</param>
        public delegate void OnDuplicateParameter(string key, string firstValue, string secondValue);

        /// <summary>
        ///     Abstraction for build parameters (aka environment variables), abstracting over any OS-specific
        ///     differences that may exist (e.g., relevancy name casing).
        /// </summary>
        public interface IBuildParameters
        {
            /// <summary>
            ///     Returns a new <see cref="IBuildParameters"/> instance that contains exactly the parameters
            ///     from this <see cref="IBuildParameters"/> whose names are found in <paramref name="keys"/>.
            /// </summary>
            /// <remarks>
            /// <para>
            ///     If <paramref name="keys"/> contains any "duplicates" (by whatever definition of "duplicate"
            ///     that is implemented by this <see cref="IBuildParameters"/>), those duplicates should be reported
            ///     to any reporter set via <see cref="GetFactory(OnDuplicateParameter)"/>.
            /// </para>
            /// <para>
            ///     The casing of the keys should be appropriate for the target OS (on Windows it won't matter,
            ///     but on Unix-like systems it matters).  The underlying IBuildParameters implementation cannot
            ///     magically convert to appropriate casing for systems where casing matters (it could implement
            ///     a heuristic though, e.g., for Unix-like systems, where these variables are typically upper-cased,
            ///     but such heuristic would be unsound).
            /// </para>
            /// </remarks>
            [NotNull]
            IBuildParameters Select([NotNull]IEnumerable<string> keys);

            /// <summary>
            ///     Returns a new <see cref="IBuildParameters"/> instance that retains all parameters from this
            ///     <see cref="IBuildParameters"/> and adds <paramref name="parameters"/> on top of them (overwriting
            ///     any existing ones).
            /// </summary>
            /// <remarks>
            /// <para>
            ///     Since this method overrides any existing parameters, the returned instance should not contain
            ///     any duplicates.
            /// </para>
            /// <para>
            ///     The casing of the keys in the <paramref name="parameters"/> dictionary matters, and should be
            ///     appropriate for the target OS (see remarks for <see cref="IBuildParameters.Select"/>).
            /// </para>
            /// </remarks>
            [NotNull]
            IBuildParameters Override([NotNull]IEnumerable<KeyValuePair<string, string>> parameters);

            /// <summary>
            ///     Returns the content of this instance as a dictionary from parameter name to variable value. The
            ///     keys of the returned dictionary should not contain any "duplicates" (by whatever definition of
            ///     "duplicate" that is implemented by this <see cref="IBuildParameters"/>).
            /// </summary>
            /// <remarks>
            ///     The clients of this method must not assume anything about case-sensitivity of the keys in the
            ///     returned dictionary; different implementations of this interface (e.g., for different target
            ///     operating systems) may choose to use either a case-sensitive or case-insensitive implementation).
            /// </remarks>
            [NotNull]
            IReadOnlyDictionary<string, string> ToDictionary();

            /// <summary>
            ///     Retrieves the value of a parameter named <paramref name="key"/>, if one is found;
            ///     otherwise throws <see cref="KeyNotFoundException"/>.
            /// </summary>
            string this[[NotNull]string key] { get; }

            /// <summary>
            ///     Returns whether this instance contains a parameter with a given name.
            /// </summary>
            bool ContainsKey([NotNull]string key);
        }

        // ==================================================================================
        // == Public Factory Methods
        // ==================================================================================

        /// <summary>
        ///     Returns a preconfigured factory for handling <see cref="IBuildParameters"/>.
        ///     If <paramref name="callback"/> is specified, it is called every time upon
        ///     creation of a new <see cref="IBuildParameters"/> instance to report any
        ///     encountered duplicates.
        /// </summary>
        public static Factory GetFactory(OnDuplicateParameter callback = null)
        {
            return new Factory(callback);
        }

        // ==================================================================================
        // == Public Factory Implementation
        // ==================================================================================

        /// <summary>
        ///     Factory class for creating appropriate <see cref="IBuildParameters"/>.
        /// </summary>
        public sealed class Factory
        {
            private readonly OnDuplicateParameter m_reporter;

            internal Factory(OnDuplicateParameter reporter)
            {
                m_reporter = reporter;
            }

            /// <summary>
            ///     Creates and returns a new instance of <see cref="IBuildParameters"/> containing all defined
            ///     system environment variables (<see cref="Environment.GetEnvironmentVariables()"/>), minus the temp
            ///     variables (<see cref="DisallowedTempVariables"/>), plus a special BuildXL pseudo-variable
            ///     for checking if BuildXL is running elevated (<see cref="IsElevatedVariableName"/>).
            /// </summary>
            /// <remarks>
            ///     Any duplicates found in <see cref="Environment.GetEnvironmentVariables()"/> are reported only if
            ///     they have different values.
            /// </remarks>
            public IBuildParameters PopulateFromEnvironment()
            {
                Contract.Ensures(Contract.Result<IBuildParameters>() != null);

                return Create(m_environmentVariables.Value);
            }

            /// <summary>
            ///     Creates and returns a new instance of <see cref="IBuildParameters"/> parameters given
            ///     as a dictionary (<paramref name="parameters"/>).
            /// </summary>
            /// <remarks>
            ///     Any duplicates found in <paramref name="parameters"/> are reported only if they
            ///     have different values.
            /// </remarks>
            public IBuildParameters PopulateFromDictionary(IEnumerable<KeyValuePair<string, string>> parameters)
            {
                Contract.Ensures(Contract.Result<IBuildParameters>() != null);

                return Create(parameters);
            }

            private readonly Lazy<Dictionary<string, string>> m_environmentVariables = Lazy.Create(() =>
            {
                var result = new Dictionary<string, string>();

                // Capture the environment variables
                foreach (DictionaryEntry envVar in Environment.GetEnvironmentVariables())
                {
                    var key = (string)envVar.Key;
                    string value = (string)envVar.Value;

                    if (DisallowedTempVariables.Contains(key))
                    {
                        continue;
                    }

                    result[key] = value;
                }

                // Add some pseudo-variables (lower precedence than real things in the environment block or user overrides).
                if (!result.ContainsKey(IsElevatedVariableName))
                {
                    result.Add(IsElevatedVariableName, CurrentProcess.IsElevated.ToString());
                }

                return result;
            });

            private IBuildParameters Create(IEnumerable<KeyValuePair<string, string>> value)
            {
                Contract.Ensures(Contract.Result<IBuildParameters>() != null);

                return CaseInsensitiveBuildParameters.PopulateFromDictionary(value, m_reporter);
            }
        }

        // ==================================================================================
        // == Private Types and Concrete IBuildParameter Implementations
        // ==================================================================================

        /// <summary>
        ///     Simple memento class holding a name of a build parameter (<see cref="Key"/>) and two
        ///     values considered equivalent (<see cref="Value1"/>, <see cref="Value2"/>).
        /// </summary>
        /// <remarks>
        ///     Strongly immutable.
        /// </remarks>
        private sealed class DuplicateBuildParameter
        {
            /// <summary>Parameter name.</summary>
            public string Key { get; }

            /// <summary>First parameter value.</summary>
            public string Value1 { get; }

            /// <summary>Second parameter value, considered the same as <see cref="Value1"/>.</summary>
            public string Value2 { get; }

            /// <nodoc/>
            public DuplicateBuildParameter(string key, string value1, string value2)
            {
                Key = key;
                Value1 = value1;
                Value2 = value2;
            }
        }

        /// <summary>
        ///     An implementation of <see cref="IBuildParameters"/> that treats parameter names as case-insensitive.
        /// </summary>
        private sealed class CaseInsensitiveBuildParameters : IBuildParameters
        {
            private static readonly StringComparer s_parametersKeyComparer = StringComparer.OrdinalIgnoreCase;

            private readonly IReadOnlyDictionary<string, string> m_parameters;
            private readonly OnDuplicateParameter m_reporter;

            /// <summary>
            ///     Factory method.  Any duplicates found in <paramref name="parameters"/> are reported to <paramref name="reporter"/>.
            /// </summary>
            public static IBuildParameters PopulateFromDictionary(IEnumerable<KeyValuePair<string, string>> parameters, OnDuplicateParameter reporter)
            {
                Contract.Requires(parameters != null);
                Contract.Ensures(Contract.Result<IBuildParameters>() != null);

                return Deduplicate(parameters, reporter);
            }

            /// <inheritdoc/>
            public IBuildParameters Select(IEnumerable<string> keys)
            {
                Contract.Requires(keys != null);

                var result = new List<KeyValuePair<string, string>>(keys.Count());
                foreach (var key in keys)
                {
                    if (m_parameters.ContainsKey(key))
                    {
                        result.Add(new KeyValuePair<string, string>(key, m_parameters[key]));
                    }
                }

                return Deduplicate(result, m_reporter);
            }

            /// <inheritdoc/>
            public IBuildParameters Override(IEnumerable<KeyValuePair<string, string>> parameters)
            {
                Contract.Requires(parameters != null);

                var result = new Dictionary<string, string>(s_parametersKeyComparer);

                var allParams = m_parameters.Concat(parameters).ToArray();
                foreach (var kv in allParams)
                {
                    result[kv.Key] = kv.Value;
                }

                return new CaseInsensitiveBuildParameters(result, CollectionUtilities.EmptyArray<DuplicateBuildParameter>(), m_reporter);
            }

            /// <inheritdoc/>
            public IReadOnlyDictionary<string, string> ToDictionary() => m_parameters;

            /// <inheritdoc/>
            public string this[string key]
            {
                get
                {
                    Contract.Requires(key != null);
                    return m_parameters[key];
                }
            }

            /// <inheritdoc/>
            public bool ContainsKey(string key)
            {
                Contract.Requires(key != null);
                return m_parameters.ContainsKey(key);
            }

            private static CaseInsensitiveBuildParameters Deduplicate(IEnumerable<KeyValuePair<string, string>> parameters, OnDuplicateParameter reporter)
            {
                var duplicates = new List<DuplicateBuildParameter>();

                var result = new Dictionary<string, string>(s_parametersKeyComparer);
                foreach (var envVar in parameters)
                {
                    string existingValue;
                    if (!result.TryGetValue(envVar.Key, out existingValue))
                    {
                        result.Add(envVar.Key, envVar.Value);
                    }
                    else if (!string.Equals(existingValue, envVar.Value))
                    {
                        duplicates.Add(new DuplicateBuildParameter(envVar.Key, existingValue, envVar.Value));
                    }
                }

                return new CaseInsensitiveBuildParameters(result, duplicates.AsReadOnly(), reporter);
            }

            private CaseInsensitiveBuildParameters(IReadOnlyDictionary<string, string> parameters, IReadOnlyList<DuplicateBuildParameter> duplicates, OnDuplicateParameter reporter)
            {
                m_reporter = reporter;
                m_parameters = parameters;

                ReportDuplicates(duplicates, reporter);
            }

            private static void ReportDuplicates(IReadOnlyList<DuplicateBuildParameter> duplicates, OnDuplicateParameter callback)
            {
                Contract.Requires(duplicates != null);

                if (callback != null)
                {
                    foreach (var duplicate in duplicates)
                    {
                        callback(duplicate.Key, duplicate.Value1, duplicate.Value2);
                    }
                }
            }
        }
    }
}

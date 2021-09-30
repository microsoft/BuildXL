// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using BuildXL.Cache.Host.Configuration;

#nullable enable

namespace BuildXL.Cache.ContentStore.Tracing
{
    /// <nodoc />
    public static class LogManager
    {
        private static Dictionary<(string, string), OperationLoggingConfiguration> Configuration = new Dictionary<(string, string), OperationLoggingConfiguration>();

        /// <nodoc />
        public static OperationLoggingConfiguration? GetConfiguration(string component, [CallerMemberName] string? caller = null)
        {
            if (string.IsNullOrWhiteSpace(component))
            {
                component = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(caller))
            {
                caller = string.Empty;
            }

            if (!Configuration.TryGetValue(((string)component, (string)caller!), out var configuration))
            {
                return null;
            }

            return configuration;
        }

        private static readonly Regex NameRegex = new Regex(@"(?<component>[^\.]+)(?:\.(?<operation>[^\.]+))?", RegexOptions.Compiled);

        /// <nodoc />
        public static void Update(LogManagerConfiguration? configuration)
        {
            if (configuration is null)
            {
                return;
            }

            Configuration = configuration.Logs.ToDictionary(kvp =>
            {
                var key = kvp.Key;

                var match = NameRegex.Match(key);
                if (!match.Success)
                {
                    throw new ArgumentException($"Attempt to parse function name `{key}` failed");
                }

                var component = string.Empty;
                if (match.Groups["component"].Success)
                {
                    component = match.Groups["component"].Value;
                }

                var operation = string.Empty;
                if (match.Groups["operation"].Success)
                {
                    operation = match.Groups["operation"].Value;
                }

                (string, string) parsed = (component, operation);
                return parsed;
            }, kvp => kvp.Value);
        }
    }
}

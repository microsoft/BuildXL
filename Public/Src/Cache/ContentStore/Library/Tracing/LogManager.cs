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
    public class LogManager
    {
        private static readonly Lazy<LogManager> _lazyInstance = new Lazy<LogManager>(() => new LogManager());

        private Dictionary<(string, string), OperationLoggingConfiguration> _configuration = new Dictionary<(string, string), OperationLoggingConfiguration>();

        /// <summary>
        /// Gets a global instance of a log manager.
        /// </summary>
        public static LogManager Instance => _lazyInstance.Value;

        /// <summary>
        /// Gets the configuration for a given <paramref name="operationName"/> and <paramref name="component"/>.
        /// </summary>
        public OperationLoggingConfiguration? GetOperationConfiguration(string component, [CallerMemberName] string? operationName = null)
        {
            if (string.IsNullOrWhiteSpace(component))
            {
                component = string.Empty;
            }

            if (string.IsNullOrWhiteSpace(operationName))
            {
                operationName = string.Empty;
            }

            if (!_configuration.TryGetValue(((string)component, (string)operationName!), out var configuration)
                && !_configuration.TryGetValue(((string)component, "*"), out configuration))
            {
                return null;
            }

            return configuration;
        }

        private static readonly Regex NameRegex = new Regex(@"(?<component>[^\.]+)(?:\.(?<operation>[^\.]+))?", RegexOptions.Compiled);

        /// <nodoc />
        public LogManager Update(LogManagerConfiguration? configuration)
        {
            if (configuration is null)
            {
                return this;
            }

            _configuration = configuration.Logs.ToDictionary(kvp =>
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

            return this;
        }
    }
}

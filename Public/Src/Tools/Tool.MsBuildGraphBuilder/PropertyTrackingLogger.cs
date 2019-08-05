// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Microsoft.Build.Framework;

namespace ProjectGraphBuilder
{
    /// <summary>
    /// Logger that handles property tracking events.
    /// </summary>
    internal sealed class PropertyTrackingLogger : ILogger
    {
        private readonly ConcurrentDictionary<string, object> m_variablesRead = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, object> m_sdkResolversNotTrackingEnvVars = new ConcurrentDictionary<string, object>();

        public void Initialize(IEventSource eventSource)
        {
            eventSource.AnyEventRaised += (sender, args) =>
                                          {
                                              if (args is EnvironmentVariableReadEventArgs evrArgs)
                                              {
                                                  m_variablesRead[evrArgs.EnvironmentVariableName] = null;
                                              }
                                              else if (args is UninitializedPropertyReadEventArgs upArgs)
                                              {
                                                  m_variablesRead[upArgs.PropertyName] = null;
                                              }
                                              else if (args is SdkResolverDoesNotTrackEnvironmentVariablesEventArgs trackArgs)
                                              {
                                                  m_sdkResolversNotTrackingEnvVars[trackArgs.SdkResolverName] = null;
                                              }
                                          };
        }

        public void Shutdown()
        {
        }

        public LoggerVerbosity Verbosity { get; set; }

        public string Parameters { get; set; }

        /// <summary>
        /// The list of environment variables that would be read by the build system if present.
        /// </summary>
        public Possible<IReadOnlyCollection<string>> PotentialEnvironmentVariableReads
        {
            get
            {
                if (m_sdkResolversNotTrackingEnvVars.Count == 0)
                {
                    return new Possible<IReadOnlyCollection<string>>(m_variablesRead.Keys.ToArray());
                }

                string errorMessage = null;

                lock (m_sdkResolversNotTrackingEnvVars)
                {
                    errorMessage = $"Unreliable environment variable tracking due to SdkResolvers not participating in tracking. Offenders: {string.Join(",", m_sdkResolversNotTrackingEnvVars)}";
                }

                return new Failure<string>(errorMessage);
            }
        }
    }
}

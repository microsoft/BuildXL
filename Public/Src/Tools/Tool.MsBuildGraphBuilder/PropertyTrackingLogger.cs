// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Microsoft.Build.Framework;

namespace ProjectGraphBuilder
{
    /// <summary>
    /// 
    /// </summary>
    internal sealed class PropertyTrackingLogger : ILogger
    {
        private readonly HashSet<string> m_variablesRead = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> m_sdkResolversNotTrackingEnvVars = new HashSet<string>();

        public IEnumerable<string> PotentialEnvironmentVariablesReads
        {
            get
            {
                // Return a copy of what's there to minimize threading issues.
                lock (m_variablesRead)
                {
                    return m_variablesRead.ToArray();
                }
            }
        }

        public void Initialize(IEventSource eventSource)
        {
            eventSource.AnyEventRaised += (sender, args) =>
                                          {
                                              if (args is EnvironmentVariableReadEventArgs evrArgs)
                                              {
                                                  lock(m_variablesRead)
                                                  {
                                                      m_variablesRead.Add(evrArgs.EnvironmentVariableName);
                                                  }
                                              }
                                              else if (args is UninitializedPropertyReadEventArgs upArgs)
                                              {
                                                  lock(m_variablesRead)
                                                  {
                                                      m_variablesRead.Add(upArgs.PropertyName);
                                                  }
                                              }
                                              else if (args is SdkResolverDoesNotTrackEnvironmentVariablesEventArgs trackArgs)
                                              {
                                                  lock (m_sdkResolversNotTrackingEnvVars)
                                                  {
                                                      m_sdkResolversNotTrackingEnvVars.Add(trackArgs.SdkResolverName);
                                                  }
                                              }
                                          };
        }

        public void Shutdown()
        {
        }

        public LoggerVerbosity Verbosity { get; set; }

        public string Parameters { get; set; }

        public Possible<IReadOnlyCollection<string>> PotentialEnvironmentVariableReads
        {
            get
            {
                if (m_sdkResolversNotTrackingEnvVars.Count == 0)
                {
                    string[] copy;

                    lock (m_variablesRead)
                    {
                        copy = m_variablesRead.ToArray();
                    }

                    return new Possible<IReadOnlyCollection<string>>(copy);
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

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
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
        private bool m_nonTrackingSdkResolversExist = false;

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
                                              else if (args is BuildWarningEventArgs warnArgs && warnArgs.Code == "MSB4263")
                                              {
                                                  // We just want to know whether or not we can trust the above tracking data.
                                                  m_nonTrackingSdkResolversExist = true;
                                              }
                                          };
        }

        public void Shutdown()
        {
        }

        public LoggerVerbosity Verbosity { get; set; }

        public string Parameters { get; set; }

        public IReadOnlyCollection<string> BuildAffectingEnvironmentVariables
        {
            get
            {
                string[] copy;

                lock(m_variablesRead)
                {
                    copy = m_variablesRead.ToArray();
                }

                return copy;
            }
        }

        public bool NonTrackingSdkResolversExist => m_nonTrackingSdkResolversExist;
    }
}

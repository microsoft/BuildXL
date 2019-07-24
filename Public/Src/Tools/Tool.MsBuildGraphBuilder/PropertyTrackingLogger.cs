// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using BuildXL.Utilities.Collections;
using Microsoft.Build.Framework;

namespace ProjectGraphBuilder
{
    internal sealed class PropertyTrackingLogger : ILogger
    {
        private readonly HashSet<string> _variablesRead = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private bool _nonTrackingSdkResolversExist = false;

        public IEnumerable<string> PotentialEnvironmentVariablesReads
        {
            get
            {
                // Return a copy of what's there to minimize threading issues.
                lock (_variablesRead)
                {
                    return _variablesRead.ToArray();
                }
            }
        }

        public void Initialize(IEventSource eventSource)
        {
            eventSource.AnyEventRaised += (sender, args) =>
                                          {
                                              if (args is EnvironmentVariableReadEventArgs evrArgs)
                                              {
                                                  lock(_variablesRead)
                                                  {
                                                      _variablesRead.Add(evrArgs.EnvironmentVariableName);
                                                  }
                                              }
                                              else if (args is UninitializedPropertyReadEventArgs upArgs)
                                              {
                                                  lock(_variablesRead)
                                                  {
                                                      _variablesRead.Add(upArgs.PropertyName);
                                                  }
                                              }
                                              else if (args is BuildWarningEventArgs warnArgs && warnArgs.Code == "MSB4263")
                                              {
                                                  // We just want to know whether or not we can trust the above tracking data.
                                                  _nonTrackingSdkResolversExist = true;
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
                IEnumerable<string> copy;

                lock(_variablesRead)
                {
                    copy = _variablesRead.ToArray();
                }

                return copy.AsReadOnlyCollection();
            }
        }

        public bool NonTrackingSdkResolversExist => _nonTrackingSdkResolversExist;
    }
}

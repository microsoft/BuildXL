// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using Microsoft.Win32;

namespace BuildXL.Utilities
{
    /// <summary>
    /// State of Defender monitoring for a path
    /// </summary>
    public enum MonitoringState
    {
        /// <summary>
        /// State is unknown
        /// </summary>
        Unknown,

        /// <summary>
        /// Defender will scan the path
        /// </summary>
        Enabled,

        /// <summary>
        /// Defender will not scan the path
        /// </summary>
        Disabled,
    }

    /// <summary>
    /// Checks to see if Windows defender is enabled for specific paths
    /// </summary>
    public sealed class DefenderChecker
    {
        private readonly MonitoringState m_state;
        private readonly List<string> m_excludedPaths;

        private DefenderChecker(MonitoringState state, List<string> excludedPaths)
        {
            m_state = state;
            m_excludedPaths = excludedPaths;
        }

        private DefenderChecker() { }

        /// <nodoc/>
        public static DefenderChecker Create()
        {
            MonitoringState state = MonitoringState.Unknown;
            List<string> excludedPaths = new List<string>();

            try
            {
                // First check if defender is enabled
                using (RegistryKey realTimeProtection = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Defender\Real-Time Protection"))
                {
                    if (realTimeProtection != null)
                    {
                        state = MonitoringState.Enabled;
                        object realTimeDisabled = realTimeProtection.GetValue("DisableRealtimeMonitoring");

                        if (realTimeDisabled != null && realTimeDisabled.GetType() == typeof(int))
                        {
                            if ((int)realTimeDisabled == 1)
                            {
                                state = MonitoringState.Disabled;
                            }
                        }

                        // Then look up excluded paths
                        using (RegistryKey key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Defender\Exclusions\Paths"))
                        {
                            if (key != null)
                            {
                                foreach (string excludedPath in key.GetValueNames())
                                {
                                    excludedPaths.Add(excludedPath);
                                }
                            }
                        }
                    }
                }
            }
#pragma warning disable ERP022 // TODO: This should catch specific exceptions
            catch
            {
                state = MonitoringState.Unknown;
            }
#pragma warning restore ERP022

            return new DefenderChecker(state, excludedPaths);
        }

        /// <summary>
        /// Checks to see if defender is enabled for a path
        /// </summary>
        public MonitoringState CheckStateForPath(string path)
        {
            Contract.Requires(path != null);

            if (m_state == MonitoringState.Enabled)
            {
                foreach (string excludePath in m_excludedPaths)
                {
                    if (path.StartsWith(excludePath, StringComparison.OrdinalIgnoreCase))
                    {
                        return MonitoringState.Disabled;
                    }
                }
            }

            return m_state;
        }
    }
}

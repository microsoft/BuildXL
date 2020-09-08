// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Plugin
{
    /// <summary>
    /// Result of plugin response
    /// </summary>
    public enum PluginResponseState
    {
        /// <nodoc />
        Succeeded,
        /// <nodoc />
        Failed,
        /// <nodoc />
        Cancelled,
        /// <nodoc />
        Fatal
    }
    /// <summary>
    /// Collateral inforamtion of Plugin Response
    /// </summary>
    public class PluginResponseResult<T>
    {
        private readonly T m_value;

        private readonly Failure m_failure;

        /// <nodoc />
        public readonly PluginResponseState State;

        /// <nodoc />
        public readonly uint Attempts;

        /// <nodoc />
        public readonly string RequestId;

        /// <nodoc />
        public bool Succeeded => State == PluginResponseState.Succeeded;

        /// <nodoc />
        public T Value
        {
            get
            {
                Contract.Requires(Succeeded);
                return m_value;
            }
        }

        /// <nodoc />
        public Failure Failure
        {
            get
            {
                Contract.Requires(!Succeeded);
                return m_failure;
            }
        }

        /// <nodoc />
        public PluginResponseResult(T value, PluginResponseState state, string requestId, uint attempts)
        {
            m_value = value;
            State = state;
            Attempts = attempts;
            RequestId = requestId;
        }

        /// <nodoc />
        public PluginResponseResult(PluginResponseState state, string requestId, uint attempts,  Failure failure)
        {
            State = state;
            m_failure = failure;
            Attempts = attempts;
            RequestId = requestId;
        }
    }

}

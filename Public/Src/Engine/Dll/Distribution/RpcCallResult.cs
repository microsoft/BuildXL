// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Engine.Distribution
{
    /// <summary>
    /// Result of RPC call
    /// </summary>
    public enum RpcCallResultState
    {
        /// <summary>
        /// Indicates the the call failed due to network error
        /// </summary>
        Failed,

        /// <summary>
        /// Indicates that the call completed successfully
        /// </summary>
        Succeeded,

        /// <summary>
        /// Indicates that the call was canceled
        /// </summary>
        Cancelled,
    }

    /// <summary>
    /// Captures information about and result of RPC call
    /// </summary>
    public sealed class RpcCallResult<T>
    {
        /// <summary>
        /// Returns true if the call succeeded
        /// </summary>
        public bool Succeeded => State == RpcCallResultState.Succeeded;

        /// <summary>
        /// The state
        /// </summary>
        public readonly RpcCallResultState State;

        /// <summary>
        /// The number of attempts for the call
        /// </summary>
        public readonly uint Attempts;

        /// <summary>
        /// The duration of the call
        /// </summary>
        public readonly TimeSpan WaitForConnectionDuration;

        /// <summary>
        /// The duration of the call
        /// </summary>
        public readonly TimeSpan Duration;

        private T m_value;
        private Failure m_lastFailure;

        /// <summary>
        /// The value returned by the call if the call was successful
        /// </summary>
        public T Value
        {
            get
            {
                Contract.Requires(State == RpcCallResultState.Succeeded);
                return m_value;
            }
        }

        /// <summary>
        /// The failure returned by the last call if the call failed
        /// </summary>
        public Failure LastFailure
        {
            get
            {
                Contract.Requires(State == RpcCallResultState.Failed);
                return m_lastFailure;
            }
        }

        /// <summary>
        /// Class constructor
        /// </summary>
        public RpcCallResult(T value, uint attempts, TimeSpan duration, TimeSpan waitForConnectionDuration)
        {
            m_value = value;
            State = RpcCallResultState.Succeeded;
            Attempts = attempts;
            Duration = duration;
            WaitForConnectionDuration = waitForConnectionDuration;
        }

        /// <summary>
        /// Class constructor
        /// </summary>
        public RpcCallResult(RpcCallResultState state, uint attempts, TimeSpan duration, TimeSpan waitForConnectionDuration, Failure lastFailure = null)
        {
            Contract.Requires(state != RpcCallResultState.Succeeded);
            Contract.Requires(lastFailure == null || state == RpcCallResultState.Failed);

            State = state;
            Attempts = attempts;
            Duration = duration;
            m_lastFailure = lastFailure;
            WaitForConnectionDuration = waitForConnectionDuration;
        }
    }
}

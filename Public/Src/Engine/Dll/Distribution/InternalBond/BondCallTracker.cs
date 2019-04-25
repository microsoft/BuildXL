// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.
#if !DISABLE_FEATURE_BOND_RPC

using System;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using BuildXL.Engine.Tracing;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Engine.Distribution.InternalBond
{
    /// <summary>
    /// The current state of the bond proxy call
    /// </summary>
    /// <remarks>
    /// WARNING: SYNC WITH BondCallStateExtensions.AsString
    /// </remarks>
    internal enum BondCallState
    {
        Started,
        WaitingForChildCall,
        Converted,
        Converting,
        InitiatedRequest,
        WaitingForConnection,
        RecreateConnection,
        CompletedWaitForConnection,
        Failed,
        Succeeded,
        Canceled,
        HeartbeatTimerShutdown,
        HeartbeatTimerInactive,
        HeartbeatBeforeCall,
        HeartbeatAfterCall,
        HeartbeatSuccess,
        HeartbeatAfterActivateConnection,
        HeartbeatDeactivateTimer,
        HeartbeatQueueTimer,
    }

    internal static class BondCallStateExtensions
    {
        public static string AsString(this BondCallState callState)
        {
            switch (callState)
            {
                case BondCallState.Canceled:
                    return nameof(BondCallState.Canceled);
                case BondCallState.CompletedWaitForConnection:
                    return nameof(BondCallState.CompletedWaitForConnection);
                case BondCallState.Converted:
                    return nameof(BondCallState.Converted);
                case BondCallState.Converting:
                    return nameof(BondCallState.Converting);
                case BondCallState.Failed:
                    return nameof(BondCallState.Failed);
                case BondCallState.HeartbeatAfterActivateConnection:
                    return nameof(BondCallState.HeartbeatAfterActivateConnection);
                case BondCallState.HeartbeatAfterCall:
                    return nameof(BondCallState.HeartbeatAfterCall);
                case BondCallState.HeartbeatBeforeCall:
                    return nameof(BondCallState.HeartbeatBeforeCall);
                case BondCallState.HeartbeatDeactivateTimer:
                    return nameof(BondCallState.HeartbeatDeactivateTimer);
                case BondCallState.HeartbeatQueueTimer:
                    return nameof(BondCallState.HeartbeatQueueTimer);
                case BondCallState.HeartbeatSuccess:
                    return nameof(BondCallState.HeartbeatSuccess);
                case BondCallState.HeartbeatTimerInactive:
                    return nameof(BondCallState.HeartbeatTimerInactive);
                case BondCallState.HeartbeatTimerShutdown:
                    return nameof(BondCallState.HeartbeatTimerShutdown);
                case BondCallState.InitiatedRequest:
                    return nameof(BondCallState.InitiatedRequest);
                case BondCallState.RecreateConnection:
                    return nameof(BondCallState.RecreateConnection);
                case BondCallState.Started:
                    return nameof(BondCallState.Started);
                case BondCallState.Succeeded:
                    return nameof(BondCallState.Succeeded);
                case BondCallState.WaitingForChildCall:
                    return nameof(BondCallState.WaitingForChildCall);
                case BondCallState.WaitingForConnection:
                    return nameof(BondCallState.WaitingForConnection);
                default:
                    throw new NotImplementedException("Unknown BondCallState type: " + callState);
            }
        }
    }

    /// <summary>
    /// Tracks the state of a bond call
    /// </summary>
    internal class BondCallTracker
    {
        /// <summary>
        /// The service function name
        /// </summary>
        public string FunctionName { get; private set; }

        /// <summary>
        /// The unique call id
        /// </summary>
        public Guid CallId { get; private set; }

        /// <summary>
        /// The current duration of the call since last state change
        /// </summary>
        public TimeSpan TotalDuration { get; private set; }

        /// <summary>
        /// The duration of the last state
        /// </summary>
        public TimeSpan StateDuration { get; private set; }

        private Stopwatch m_stopwatch;
        private TimeSpan m_startTime;
        private TimeSpan m_lastStateTime;
        private BondCallState m_lastState;

        /// <summary>
        /// The amount of times the call has been tried
        /// </summary>
        public uint TryCount { get; internal set; }

        public void Initialize(Stopwatch stopwatch, string functionName, Guid callId)
        {
            Contract.Requires(stopwatch != null);
            Contract.Requires(functionName != null);
            Contract.Assert(m_stopwatch == null);

            m_stopwatch = stopwatch;
            m_startTime = stopwatch.Elapsed;
            m_lastStateTime = m_startTime;
            FunctionName = functionName;
            CallId = callId;
            OnStateChanged(BondCallState.Started);
        }

        /// <summary>
        /// Called when a bond call changes state
        /// </summary>
        public void OnStateChanged(BondCallState toState)
        {
            var currentTime = m_stopwatch.Elapsed;
            StateDuration = currentTime - m_lastStateTime;
            TotalDuration = currentTime - m_startTime;
            OnStateChanged(m_lastState, toState, stateTime: StateDuration, totalTime: TotalDuration);
            m_lastStateTime = currentTime;
            m_lastState = toState;
        }

        /// <summary>
        /// Called when a bond call changes state
        /// </summary>
        /// <param name="fromState">the current state</param>
        /// <param name="toState">the new state</param>
        /// <param name="stateTime">the amount of time spent in the old state</param>
        /// <param name="totalTime">the total amount of time the call has been active</param>
        protected virtual void OnStateChanged(BondCallState fromState, BondCallState toState, TimeSpan stateTime, TimeSpan totalTime)
        {
        }

        /// <summary>
        /// Logs formatted message
        /// </summary>
        public virtual void LogMessage(string formatMessage, params object[] formatArgs)
        {
        }
    }

    /// <summary>
    /// Call tracker which logs state changes for calls
    /// </summary>
    internal sealed class LoggingBondCallTracker : BondCallTracker
    {
        private readonly LoggingContext m_loggingContext;
        private readonly RpcMachineData m_receiverData;
        private readonly string m_description;

        /// <summary>
        /// Class constructor
        /// </summary>
        /// <param name="loggingContext">the logging context</param>
        /// <param name="receiverData">the server name receiving the call</param>
        /// <param name="description">the description for the call</param>
        public LoggingBondCallTracker(LoggingContext loggingContext, RpcMachineData receiverData, string description = null)
        {
            m_loggingContext = loggingContext;
            m_receiverData = receiverData;
            m_description = description;
        }

        protected override void OnStateChanged(BondCallState fromState, BondCallState toState, TimeSpan stateTime, TimeSpan totalTime)
        {
            switch (toState)
            {
                case BondCallState.Started:
                    LogMessage("Starting call({1}) (try count {0})", TryCount, m_description ?? string.Empty);
                    return;
            }

            if (ShouldLogStateChange(toState))
            {
                LogMessage("CallState: {0}, StateTime: {1}, TotalTime: {2}", toState.AsString(), stateTime, totalTime);
            }
        }

        /// <summary>
        /// Logs child call invocation
        /// </summary>
        public void LogChildTracker(BondCallTracker childTracker)
        {
            LogMessage("Child call initiated: #{0}", childTracker.CallId);
            OnStateChanged(BondCallState.WaitingForChildCall);
        }

        /// <inheritdoc />
        public override void LogMessage(string formatMessage, params object[] formatArgs)
        {
            Logger.Log.DistributionSendBondCallFormat(m_loggingContext, m_receiverData, FunctionName, CallId, formatMessage, formatArgs);
        }

        private static bool ShouldLogStateChange(BondCallState toState)
        {
            // Here we decide which states are logged for bond calls. Most states are not logged to
            // reduce log spam but states may be added back if they are considered necessary.
            // Also consider this decision being based on verbosity/diagnostic keywords.
            switch (toState)
            {
                case BondCallState.Started:
                case BondCallState.Failed:
                case BondCallState.Succeeded:
                case BondCallState.Canceled:
                case BondCallState.HeartbeatSuccess:
                case BondCallState.HeartbeatDeactivateTimer:
                case BondCallState.RecreateConnection:
                    return true;
                case BondCallState.WaitingForChildCall:
                case BondCallState.Converted:
                case BondCallState.Converting:
                case BondCallState.InitiatedRequest:
                case BondCallState.WaitingForConnection:
                case BondCallState.CompletedWaitForConnection:
                case BondCallState.HeartbeatTimerShutdown:
                case BondCallState.HeartbeatTimerInactive:
                case BondCallState.HeartbeatBeforeCall:
                case BondCallState.HeartbeatAfterCall:
                case BondCallState.HeartbeatAfterActivateConnection:
                case BondCallState.HeartbeatQueueTimer:
                default:
                    return false;
            }
        }
    }
}
#endif

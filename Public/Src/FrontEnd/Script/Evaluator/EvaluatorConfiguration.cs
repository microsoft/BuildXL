// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.FrontEnd.Script.Evaluator
{
    /// <summary>
    /// Configuration object used to fine-tune DScript evaluation.
    /// </summary>
    public sealed class EvaluatorConfiguration
    {
        private const int DefaultCallStackThreshold = 100;

        /// <nodoc />
        public EvaluatorConfiguration(
            bool trackMethodInvocations,
            TimeSpan cycleDetectorStartupDelay,
            int callStackThreshold = DefaultCallStackThreshold)
        {
            CallStackThreshold = callStackThreshold;
            TrackMethodInvocations = trackMethodInvocations;
            CycleDetectorStartupDelay = cycleDetectorStartupDelay;
        }

        /// <summary>
        /// Limits the size of call stack.
        /// </summary>
        public int CallStackThreshold { get; }

        /// <summary>
        /// If specified all method invocations will be tracked and top N most frequently methods will be captured in the log.
        /// </summary>
        public bool TrackMethodInvocations { get; }

        /// <summary>
        /// Suggested waiting time before starting the cycle detection.
        /// </summary>
        public TimeSpan CycleDetectorStartupDelay { get; }

        /// <summary>
        /// Suggested waiting time before increasing priority for cycle detector.
        /// </summary>
        public TimeSpan CycleDetectorIncreasePriorityDelay => CycleDetectorStartupDelay;
    }
}

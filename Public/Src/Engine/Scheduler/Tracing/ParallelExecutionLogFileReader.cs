// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.CodeAnalysis;
using BuildXL.Utilities;
using BuildXL.Utilities.Tracing;

namespace BuildXL.Scheduler.Tracing
{
    /// <summary>
    /// Replays events in parallel (as persisted by <see cref="ExecutionLogFileTarget"/>) to an <see cref="IExecutionLogTarget"/>.
    /// </summary>
    public sealed class ParallelExecutionLogFileReader : ExecutionLogFileReader
    {
        /// <nodoc />
        [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope", Justification = "The base class handles disposal of the ParallelBinaryLogReader instance")]
        public ParallelExecutionLogFileReader(string logFilename, PipExecutionContext context, IExecutionLogTarget target)
            : base(new ParallelBinaryLogReader(logFilename, context), target)
        {
        }

        /// <summary>
        /// Attempts to read every event, dispatching each to the target. Events may be dispatched out of order.
        /// Returns 'false' in the event of a condition such as <see cref="BinaryLogReader.EventReadResult.UnexpectedEndOfStream"/>.
        /// </summary>
        public bool ReadAllEventsInParallel()
        {
            BinaryLogReader.EventReadResult result = ((ParallelBinaryLogReader)Reader).ReadAllEvents();

            return result == BinaryLogReader.EventReadResult.EndOfStream;
        }
    }
}

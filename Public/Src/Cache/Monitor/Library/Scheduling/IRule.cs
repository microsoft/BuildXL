// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;

namespace BuildXL.Cache.Monitor.App.Scheduling
{
    /// <summary>
    /// Basic interface for a rule. Rules are run in a single-threaded fashion.
    /// </summary>
    public interface IRule
    {
        /// <summary>
        /// Unique identifier for a given rule instance. It is expected to remain the same across program runs, and
        /// depend only on configuration parameters.
        ///
        /// Used by the scheduler to determine last time a rule was run and when it needs to be run again.
        /// </summary>
        string Identifier { get; }

        /// <summary>
        /// The concurrency bucket is used by the scheduler to limit the amount of rules in the same bucket that can
        /// be run concurrently.
        /// </summary>
        string ConcurrencyBucket { get; }

        Task Run(RuleContext context);
    }
}

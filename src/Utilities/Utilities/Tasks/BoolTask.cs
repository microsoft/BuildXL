// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Threading.Tasks;

namespace BuildXL.Utilities.Tasks
{
    /// <summary>
    /// Provides completed tasks for 'true' and 'false'
    /// </summary>
    public static class BoolTask
    {
        /// <summary>
        /// A completed task with the result 'true'
        /// </summary>
        public static readonly Task<bool> True;

        /// <summary>
        /// A completed task with the result 'false'
        /// </summary>
        public static readonly Task<bool> False;

        /// <summary>
        /// Task completion source with a completed 'true' task.
        /// </summary>
        public static readonly TaskSourceSlim<bool> TrueCompletion;

        /// <summary>
        /// Task completion source with a completed 'false' task.
        /// </summary>
        public static readonly TaskSourceSlim<bool> FalseCompletion;
        
        /// <summary>
        /// Task completion source with a completed 'true' task.
        /// </summary>
        public static readonly TaskCompletionSource<bool> TrueCompletionSource;

        /// <summary>
        /// Task completion source with a completed 'false' task.
        /// </summary>
        public static readonly TaskCompletionSource<bool> FalseCompletionSource;

        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Performance", "CA1810:InitializeReferenceTypeStaticFieldsInline")]
        static BoolTask()
        {
            TrueCompletion = TaskSourceSlim.Create<bool>();
            TrueCompletion.SetResult(true);
            True = TrueCompletion.Task;

            TrueCompletionSource = new TaskCompletionSource<bool>();
            TrueCompletionSource.SetResult(true);

            FalseCompletion = TaskSourceSlim.Create<bool>();
            FalseCompletion.SetResult(false);
            False = FalseCompletion.Task;

            FalseCompletionSource = new TaskCompletionSource<bool>(false);
            FalseCompletionSource.SetResult(false);
        }
    }
}

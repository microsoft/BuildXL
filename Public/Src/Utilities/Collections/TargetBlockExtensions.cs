// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BuildXL.Utilities.Collections
{
    /// <summary>
    /// Useful extensions to ActionBlock class
    /// </summary>
    public static class TargetBlockExtensions
    {
        private static void PostOrThrow<T>(this ITargetBlock<T> actionBlock, T input)
        {
            bool success = actionBlock.Post(input);

            Contract.Assert(success, "Could not post to ActionBlock");
        }

        /// <summary>
        /// Post all given items to the actionBlock and return a task of the completion.
        /// </summary>
        public static Task PostAllAndComplete<T>(this ITargetBlock<T> actionBlock, IEnumerable<T> inputs)
        {
            Contract.Requires(inputs != null);

            foreach (T input in inputs)
            {
                actionBlock.PostOrThrow(input);
            }

            actionBlock.Complete();
            return actionBlock.Completion;
        }

        /// <summary>
        /// Post all given items to the actionBlock.
        /// </summary>
        public static void PostAll<T>(this ITargetBlock<T> actionBlock, IEnumerable<T> inputs)
        {
            Contract.Requires(inputs != null);

            foreach (T input in inputs)
            {
                actionBlock.PostOrThrow(input);
            }
        }
    }
}

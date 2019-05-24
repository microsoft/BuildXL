// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Utilities;

namespace BuildXL.Cache.Interfaces
{
    /// <summary>
    /// Some simple extension methods that are useful for the
    /// cache interface but may really be useful elsewhere too.
    /// This will likely be something that needs to move
    /// </summary>
    public static class Extensions
    {
        // Ugly helper that waits for at least one task to
        // complete in the list of tasks and then returns all of
        // the completed tasks, removing them from the list of tasks.
        private static IEnumerable<T> RemoveDone<T>(List<T> tasks)
            where T : Task
        {
            Contract.Requires(tasks != null);

            // The Task.WaitAny must take an array with no empty slots
            T[] taskArray = tasks.ToArray();
            Task.WaitAny(taskArray);

            // Now return any/all tasks that have completed,
            // and remove those from the List<T>
            foreach (T task in taskArray)
            {
                if (task.IsCompleted)
                {
                    tasks.Remove(task);
                    yield return task;
                }
            }
        }

        /// <summary>
        /// An out-of-order parallel enumeration of Tasks
        /// </summary>
        /// <remarks>
        /// This enumeration takes an enumeration of Tasks and returns them
        /// in roughly the order in which they complete, not the order in which
        /// they are created.  As a side effect, this will only return tasks
        /// which are complete (Task.IsCompleted).
        /// </remarks>
        /// <typeparam name="T">Type of the enumerable - must be a Task type</typeparam>
        /// <param name="tasks">The input enumeration of tasks</param>
        /// <param name="maxPending">The maximum outstanding items in the enumeration - minimum 1</param>
        /// <returns>An enumeration of the completed tasks, in roughly the order in which they complete</returns>
        public static IEnumerable<T> OutOfOrderTasks<T>(this IEnumerable<T> tasks, int maxPending = 16)
            where T : Task
        {
            Contract.Requires(tasks != null);
            Contract.Requires(maxPending > 0);

            List<T> pending = new List<T>(maxPending);

            foreach (T task in tasks)
            {
                while (pending.Count >= maxPending)
                {
                    foreach (T done in RemoveDone(pending))
                    {
                        yield return done;
                    }
                }

                pending.Add(task);
            }

            while (pending.Count > 0)
            {
                foreach (T done in RemoveDone(pending))
                {
                    yield return done;
                }
            }
        }

        /// <summary>
        /// Returns the result of the first completed task and removes it from the list
        /// </summary>
        private static async Task<T> RemoveWhenAnySlow<T>(List<Task<T>> tasks)
        {
            Contract.Requires(tasks != null);

            Task<T> result = await Task.WhenAny(tasks);
            tasks.Remove(result);
            return await result;
        }

        /// <summary>
        /// Returns the result of the first completed task and removes it from the list
        /// </summary>
        private static Task<T> RemoveWhenAny<T>(List<Task<T>> tasks)
        {
            Contract.Requires(tasks != null);

            // Fast path: If a task is completed, just remove it and return it
            foreach (var task in tasks)
            {
                if (task.IsCompleted)
                {
                    tasks.Remove(task);
                    return task;
                }
            }

            // Slow path: await Task.WhenAny, remove the completed task, and return the result
            return RemoveWhenAnySlow(tasks);
        }

        /// <summary>
        /// An out-of-order parallel enumeration of Tasks.
        /// </summary>
        /// <remarks>
        /// This enumeration takes an enumeration of Tasks and returns them
        /// in roughly the order in which they complete, not the order in which
        /// they are created. This is an asynchronous version of <see cref="OutOfOrderTasks"/>
        /// which does not return the original taks but instead returns their results
        /// </remarks>
        /// <typeparam name="T">Type of the task results</typeparam>
        /// <param name="tasks">The input enumeration of tasks</param>
        /// <param name="maxPending">The maximum outstanding items in the enumeration - minimum 1</param>
        /// <returns>An enumeration of the async task results, in roughly the order in which they complete</returns>
        public static IEnumerable<Task<T>> OutOfOrderResultsAsync<T>(this IEnumerable<Task<T>> tasks, int maxPending = 16)
        {
            Contract.Requires(tasks != null);
            Contract.Requires(maxPending > 0);

            List<Task<T>> pending = new List<Task<T>>(maxPending);

            foreach (Task<T> task in tasks)
            {
                while (pending.Count >= maxPending)
                {
                    yield return RemoveWhenAny(pending);
                }

                pending.Add(task);
            }

            while (pending.Count > 0)
            {
                yield return RemoveWhenAny(pending);
            }
        }

        /// <summary>
        /// A simple IEnumerable "delay line"
        /// </summary>
        /// <remarks>
        /// This is useful for keeping a number of enumerated items
        /// in flight, which is mostly useful if they have some internal
        /// async behavior, such as with Task&lt;T&gt;.  This allows
        /// those items to execute "in parallel" while still returning
        /// them in order.
        ///
        /// To get better parallelism with unbalanced workloads,
        /// you really want to use the OutOfOrderTasks method.  They
        /// are similar in purpose but this one does not specifically
        /// depend on the Task type and does not change the order of
        /// the enumeration.
        /// </remarks>
        /// <typeparam name="T">Type of the enumerable</typeparam>
        /// <param name="items">The input enumeration</param>
        /// <param name="delay">The maximum outstanding items in the enumeration minimum 1</param>
        /// <returns>An enumeration of the same type and same order as the input</returns>
        public static IEnumerable<T> DelayLine<T>(this IEnumerable<T> items, int delay = 16)
        {
            Contract.Requires(items != null);
            Contract.Requires(delay > 0);

            Queue<T> queue = new Queue<T>(delay);

            foreach (T item in items)
            {
                while (queue.Count >= delay)
                {
                    yield return queue.Dequeue();
                }

                queue.Enqueue(item);
            }

            while (queue.Count > 0)
            {
                yield return queue.Dequeue();
            }
        }

        /// <summary>
        /// Adds a failure if the object is null.
        /// </summary>
        public static void AddFailureIfNull(this IList<Failure> list, object obj, string name)
        {
            if (obj is null)
            {
                list.Add(new Failure<string>($"{name} cannot be null."));
            }
        }

        /// <summary>
        /// Adds a failure if the object is null.
        /// </summary>
        public static void AddFailureIfNullOrWhitespace(this IList<Failure> list, string str, string name)
        {
            if (string.IsNullOrWhiteSpace(str))
            {
                list.Add(new Failure<string>($"{name} cannot be null or whitespace."));
            }
        }
    }
}

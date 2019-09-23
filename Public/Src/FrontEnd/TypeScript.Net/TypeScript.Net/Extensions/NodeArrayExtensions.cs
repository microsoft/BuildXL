// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BuildXL.Utilities.Collections;
using JetBrains.Annotations;
using TypeScript.Net.Types;

namespace TypeScript.Net.Extensions
{
    /// <nodoc/>
    public static class NodeArrayExtensions
    {
        /// <summary>
        /// Returns true when <paramref name="array"/> is null or empty.
        /// </summary>
        public static bool IsNullOrEmpty<T>([CanBeNull]this INodeArray<T> array)
        {
            return array == null || array.Count == 0;
        }

        /// <summary>
        /// Custom implementation for 'Any' LINQ-like method that accepts <code>null</code> as a collection value
        /// and avoid allocations during collection enumeration.
        /// </summary>
        public static bool Any<TElement>([CanBeNull]INodeArray<TElement> sequence, Func<TElement, bool> callback)
        {
            if (sequence == null)
            {
                return false;
            }

            for (int i = 0; i < sequence.Count; i++)
            {
                var result = callback(sequence[i]);
                if (result)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Custom implementation for 'Any' LINQ-like method that accepts <code>null</code> as a collection value
        /// and avoid allocations during collection enumeration.
        /// </summary>
        public static bool Any<TElement>([CanBeNull]IReadOnlyList<TElement> array, Func<TElement /*element*/, bool> callback)
        {
            if (array != null)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    var result = callback(array[i]);
                    if (result)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Custom implementation for 'Any' LINQ-like method that accepts <code>null</code> as a collection value
        /// and avoid allocations during collection enumeration.
        /// </summary>
        public static bool Any<TElement, TState>([CanBeNull]IReadOnlyList<TElement> array, TState state, Func<TElement, TState, bool> callback)
        {
            if (array != null)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    var result = callback(array[i], state);
                    if (result)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// Iterates through <paramref name="array"/> and performs the callback on each element of array until the callback
        /// returns a truthy value, then returns that value.
        /// If no such value is found, the callback is applied to each element of array and default(T) is returned.
        /// </summary>
        public static TResult ForEachUntil<TElement, TResult>([CanBeNull]INodeArray<TElement> array, Func<TElement, TResult> callback)
        {
            if (array != null)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    var result = callback(array[i]);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }

            return default(TResult);
        }

        /// <summary>
        /// Iterates through <paramref name="array"/> and performs the callback on each element of array until the callback
        /// returns a truthy value, then returns that value.
        /// If no such value is found, the callback is applied to each element of array and default(T) is returned.
        /// </summary>
        public static TResult ForEachUntil<TElement, TResult>([CanBeNull]IReadOnlyList<TElement> array, Func<TElement, TResult> callback)
        {
            if (array != null)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    var result = callback(array[i]);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }

            return default(TResult);
        }

        /// <nodoc />
        public static TResult ForEachUntil<TElement, TResult, TState>([CanBeNull]IReadOnlyList<TElement> array, TState state, Func<TElement, TState, TResult> callback)
        {
            if (array != null)
            {
                for (int i = 0; i < array.Count; i++)
                {
                    var result = callback(array[i], state);
                    if (result != null)
                    {
                        return result;
                    }
                }
            }

            return default(TResult);
        }

        /// <summary>
        /// Calls a given callback for every element in a <paramref name="sequence"/>.
        /// </summary>
        public static void ForEach<TElement>([CanBeNull]INodeArray<TElement> sequence, Action<TElement> callback)
        {
            if (sequence == null)
            {
                return;
            }

            for (int i = 0; i < sequence.Count; i++)
            {
                callback(sequence[i]);
            }
        }

        /// <summary>
        /// Calls a given callback for every element in a <paramref name="sequence"/>.
        /// </summary>
        public static void ForEach<TElement, TState>([CanBeNull]INodeArray<TElement> sequence, TState state, Action<TElement, TState> callback)
        {
            if (sequence == null)
            {
                return;
            }

            for (int i = 0; i < sequence.Count; i++)
            {
                callback(sequence[i], state);
            }
        }

        /// <summary>
        /// Calls a given callback for every element in a <paramref name="sequence"/>.
        /// </summary>
        public static void ForEach<TElement>([CanBeNull]IReadOnlyList<TElement> sequence, Action<TElement> callback)
        {
            if (sequence == null)
            {
                return;
            }

            for (int i = 0; i < sequence.Count; i++)
            {
                callback(sequence[i]);
            }
        }

        /// <summary>
        /// Calls a given callback for every element in a <paramref name="sequence"/>.
        /// </summary>
        public static void ForEach<TElement, TState>([CanBeNull]IReadOnlyList<TElement> sequence, TState state, Action<TElement, TState> callback)
        {
            if (sequence == null)
            {
                return;
            }

            for (int i = 0; i < sequence.Count; i++)
            {
                callback(sequence[i], state);
            }
        }

        /// <summary>
        /// Calls a given callback for every element in a <paramref name="sequence"/>.
        /// </summary>
        public static void ForEach<TElement>([CanBeNull]TElement[] sequence, Action<TElement> callback, int degreeOfParallelism)
        {
            if (sequence == null)
            {
                return;
            }

            if (degreeOfParallelism <= 1)
            {
                foreach (var element in sequence)
                {
                    callback(element);
                }
            }
            else
            {
                Parallel.For(
                    0, sequence.Length,
                    new ParallelOptions() { MaxDegreeOfParallelism = degreeOfParallelism },
                    (idx, state) =>
                    {
                        callback(sequence[idx]);
                    });
            }
        }

        /// <summary>
        /// Calls a given callback for every element in a <paramref name="sequence"/>.
        /// </summary>
        public static void ForEach<TElement>([CanBeNull]TElement[] sequence, Action<int, TElement> callback, int degreeOfParallelism)
        {
            if (sequence == null)
            {
                return;
            }

            if (degreeOfParallelism <= 1)
            {
                int index = 0;
                foreach (var element in sequence)
                {
                    callback(index, element);
                    index++;
                }
            }
            else
            {
                Parallel.For(
                    0, sequence.Length,
                    new ParallelOptions() { MaxDegreeOfParallelism = degreeOfParallelism },
                    (idx, state) =>
                    {
                        callback(idx, sequence[idx]);
                    });
            }
        }

        /// <nodoc />
        public static NodeArray.NodeArraySelectorEnumerable<TSource, TResult> Select<TSource, TResult>(this INodeArray<TSource> sequence, Func<TSource, TResult> selector)
        {
            return new NodeArray.NodeArraySelectorEnumerable<TSource, TResult>(sequence, selector);
        }

        /// <nodoc />
        [NotNull]
        public static IReadOnlyList<TResult> ToList<TSource, TResult>(this NodeArray.NodeArraySelectorEnumerable<TSource, TResult> selectEnumerable)
        {
            var result = new List<TResult>(selectEnumerable.ArraySize);

            foreach (var e in selectEnumerable)
            {
                result.Add(e);
            }

            return result;
        }

        /// <nodoc />
        public static TResult FirstOrDefault<TSource, TResult>(this NodeArray.NodeArraySelectorEnumerable<TSource, TResult> selectEnumerable, Func<TResult, bool> predicate)
        {
            foreach (var e in selectEnumerable)
            {
                var predicateResult = predicate(e);
                if (predicateResult)
                {
                    return e;
                }
            }

            return default(TResult);
        }

        /// <nodoc />
        [NotNull]
        public static IReadOnlyList<TResult> ToList<TSource, TResult>(this NodeArray.NodeArraySelectorEnumerable<TSource, TResult>? selectEnumerable)
        {
            if (selectEnumerable == null)
            {
                return CollectionUtilities.EmptyArray<TResult>();
            }

            return ToList(selectEnumerable.Value);
        }

        /// <nodoc />
        public static IEnumerable<TResult> Where<TSource, TResult>(this NodeArray.NodeArraySelectorEnumerable<TSource, TResult> selectEnumerable, Func<TResult, bool> predicate)
        {
            // If the array is empty it make no sense to allocate anything.
            if (selectEnumerable.ArraySize == 0)
            {
                return CollectionUtilities.EmptyArray<TResult>();
            }

            return Where();
            IEnumerable<TResult> Where()
            {
                foreach (var e in selectEnumerable)
                {
                    if (predicate(e))
                    {
                        yield return e;
                    }
                }
            }
        }

        /// <nodoc />
        public static List<T> ToList<T>(this NodeArray<T> @this) => @this.Elements.ToList();

        /// <nodoc />
        public static T ElementAtOrDefault<T>(this INodeArray<T> @this, int index)
        {
            if (index < @this.Count)
            {
                return @this[index];
            }

            return default(T);
        }

        /// <nodoc />
        public static T LastOrDefault<T>(this INodeArray<T> @this)
        {
            return @this.Count == 0 ? default(T) : @this[@this.Count - 1];
        }

        /// <nodoc />
        public static T FirstOrDefault<T>(this INodeArray<T> @this)
        {
            return @this.Count == 0 ? default(T) : @this[0];
        }

        /// <nodoc />
        [NotNull]
        public static T First<T>(this INodeArray<T> @this)
        {
            if (@this.Count == 0)
            {
                throw new InvalidOperationException("The sequence contains no elements");
            }

            return @this[0];
        }
    }
}

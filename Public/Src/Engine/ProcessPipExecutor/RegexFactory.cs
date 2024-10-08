// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System;
using BuildXL.Processes;
using BuildXL.Utilities.Core;

namespace BuildXL.ProcessPipExecutor
{
    /// <summary>
    /// Builds a <see cref="Regex"/> with caching
    /// </summary>
    public static class RegexFactory
    {
        private static readonly ConcurrentDictionary<ExpandedRegexDescriptor, Lazy<Task<Regex>>> s_regexTasks = new();

        /// <summary>
        /// Constructs a regex from a <see cref="ExpandedRegexDescriptor"/>, caching the result
        /// </summary>
        public static Task<Regex> GetRegexAsync(ExpandedRegexDescriptor descriptor)
        {
            // ConcurrentDictionary.GetOrAdd might evaluate the delegate multiple times for the same key
            // if it's called concurrently for the same key and there isn't an entry in the dictionary yet.
            // However, only one is actually stored in the dictionary.
            // To avoid creating multiple tasks that do redundant expensive work, we wrap the task creation in a Lazy.
            // While multiple lazies might get created, only one of them is actually evaluated: the one that was actually inserted into the dictionary.
            // All others are forgotten, and thus, we only ever do the expensive Regex work once.
            // The overhead of the Lazy is irrelevant in practice given that the race itself is unlikely, and considering that the actual
            // Regex object dwarfs the size of the Lazy overhead by several orders of magnitude.
            return s_regexTasks.GetOrAdd(
                descriptor,
                descriptor2 => Lazy.Create(
                    () => Task.Factory.StartNew(
                        () => new Regex(descriptor2.Pattern, descriptor2.Options | RegexOptions.Compiled | RegexOptions.CultureInvariant)))).Value;
        }
    }
}

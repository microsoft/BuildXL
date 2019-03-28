// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Set of extension methods for the <see cref="Process"/> class.
    /// </summary>
    /// <remarks>
    /// Extracting some logic out of the main class helps to follow Interface Segregation Principle
    /// and simplifies original class by removing unnecessary code out of it that is used only by limited set of clients.
    /// </remarks>
    public static class ProcessExtensions
    {
        /// <summary>
        /// Returns all outputs for the <paramref name="process"/> in a form of <see cref="BuildXL.Utilities.FileArtifact"/>.
        /// </summary>
        public static IEnumerable<FileArtifact> GetOutputs(this Process process)
        {
            Contract.Requires(process != null);

            foreach (FileArtifactWithAttributes fileArtifact in process.FileOutputs)
            {
                yield return fileArtifact.ToFileArtifact();
            }
        }

        /// <summary>
        /// Returns all cacheable outputs for the <paramref name="process"/> in a form of <see cref="BuildXL.Utilities.FileArtifact"/>.
        /// </summary>
        public static IEnumerable<FileArtifact> GetCacheableOutputs(this Process process)
        {
            Contract.Requires(process != null);

            foreach (FileArtifactWithAttributes fileArtifact in process.FileOutputs)
            {
                if (fileArtifact.CanBeReferencedOrCached())
                {
                    yield return fileArtifact.ToFileArtifact();
                }
            }
        }

        /// <summary>
        /// Returns number of items in the <paramref name="process"/> outputs that should be presented in cache.
        /// </summary>
        public static int GetCacheableOutputsCount(this Process process)
        {
            Contract.Requires(process != null);

            var outputs = process.FileOutputs;
            return GetCacheableOutputsCount(outputs);
        }

        /// <summary>
        /// Returns number of items in <paramref name="outputs"/> that should be presented in cache.
        /// </summary>
        public static int GetCacheableOutputsCount(ReadOnlyArray<FileArtifactWithAttributes> outputs)
        {
            int count = 0;

            foreach (FileArtifactWithAttributes fileArtifact in outputs)
            {
                if (fileArtifact.CanBeReferencedOrCached())
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>
        /// The number of units for a normalized percentage resource
        /// </summary>
        /// <remarks>
        /// We use 10000 instead of 100 for percent to get finer granularity of resource usage
        /// 85% is therefore represented as 8500
        /// </remarks>
        public const int PercentageResourceLimit = 10000;

        /// <summary>
        /// Gets a resource normalized to a percentage capping out the the total specified
        /// </summary>
        public static ProcessSemaphoreInfo GetNormalizedPercentageResource(StringId name, int usage, int total)
        {
            usage = Math.Min(usage, total);

            // Calculate the percentage with minimum of 1 which the least required value
            int value = Math.Max(1, total == 0 ? int.MinValue : (int)(((long)PercentageResourceLimit * usage) / total));
            return new ProcessSemaphoreInfo(name, value: value, limit: PercentageResourceLimit);
        }

        /// <summary>
        /// Gets the ItemResources for the given process's semaphores using the given semaphore set
        /// </summary>
        public static ItemResources GetSemaphoreResources(
            this Process process,
            SemaphoreSet<StringId> semaphoreSet,
            Func<ProcessSemaphoreInfo, int> getLimit = null,
            IReadOnlyList<ProcessSemaphoreInfo> customSemaphores = null)
        {
            return GetSemaphoreResources(
                semaphoreSet,
                semaphores: customSemaphores == null ?
                    process.Semaphores :
                    ReadOnlyArray<ProcessSemaphoreInfo>.FromWithoutCopy(process.Semaphores.ConcatAsArray(customSemaphores)),
                getLimit: getLimit);
        }

        /// <summary>
        /// Gets the ItemResources for the given process's semaphores using the given semaphore set
        /// </summary>
        public static ItemResources GetSemaphoreResources<TList>(
            SemaphoreSet<StringId> semaphoreSet,
            TList semaphores,
            Func<ProcessSemaphoreInfo, int> getLimit = null)
            where TList : IReadOnlyList<ProcessSemaphoreInfo>
        {
            if (semaphores.Count == 0)
            {
                return ItemResources.Empty;
            }

            int max = -1;
            for (int i = 0; i < semaphores.Count; i++)
            {
                var semaphore = semaphores[i];
                var limit = getLimit?.Invoke(semaphore) ?? semaphore.Limit;
                max = Math.Max(max, semaphoreSet.CreateSemaphore(semaphore.Name, limit));
            }

            if (max < 0)
            {
                return ItemResources.Empty;
            }

            int[] semaphoreIncrements = new int[max + 1];
            for (int i = 0; i < semaphores.Count; i++)
            {
                var semaphore = semaphores[i];
                var limit = getLimit?.Invoke(semaphore) ?? semaphore.Limit;
                int semaphoreIndex = semaphoreSet.CreateSemaphore(semaphore.Name, limit);
                semaphoreIncrements[semaphoreIndex] = semaphore.Value;
            }

            return ItemResources.Create(semaphoreIncrements);
        }
    }
}

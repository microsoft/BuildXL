// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;

namespace Tool.MimicGenerator
{
    public abstract class Pip
    {
        public int? OriginalPipId;
        public int PipId;
        public readonly string Spec;
        public readonly string StableId;

        protected Pip(int pipId, string stableId, string spec)
        {
            PipId = pipId;
            StableId = stableId;
            Spec = spec;
        }
    }

    /// <summary>
    /// Data for a declared semaphore
    /// </summary>
    public sealed class SemaphoreInfo
    {
        /// <summary>
        /// The resource name
        /// </summary>
        public string Name;

        /// <summary>
        /// The semaphore value
        /// </summary>
        public int Value;

        /// <summary>
        /// The maximum value
        /// </summary>
        public int Limit;
    }

    public sealed class Process : Pip
    {
        public readonly List<int> Produces;
        public readonly List<int> Consumes;
        public readonly List<SemaphoreInfo> Semaphores;

        // TODO: Using a default wall time of 10 seconds. This should go away once execution time is required
        public int ProcessWallTimeMs = 10000;

        public Process(int pipId, string stableId, string spec, List<int> produces, List<int> consumes, List<SemaphoreInfo> semaphores)
            : base(pipId, stableId, spec)
        {
            Produces = produces;
            Consumes = consumes;
            Semaphores = semaphores;
        }
    }

    public sealed class WriteFile : Pip
    {
        public readonly int Destination;

        public WriteFile(int pipId, string stableId, string spec, int destination)
            : base(pipId, stableId, spec)
        {
            Destination = destination;
        }
    }

    public sealed class CopyFile : Pip
    {
        public readonly int Source;
        public readonly int Destination;

        public CopyFile(int pipId, string stableId, string spec, int source, int destination)
            : base(pipId, stableId, spec)
        {
            Source = source;
            Destination = destination;
        }
    }

    public sealed class SealDirectory : Pip
    {
        public readonly int Directory;

        public SealDirectory(int pipId, string stableId, string spec, int directory)
            : base(pipId, stableId, spec)
        {
            Directory = directory;
        }
    }

    public sealed class ObservedAccess
    {
        public ObservedAccessType ObservedAccessType { get; set; }

        public string Path { get; set; }

        public string ContentHash { get; set; }
    }

    public enum ObservedAccessType
    {
        DirectoryEnumeration,
        AbsentPathProbe,
        FileContentRead,
    }
}

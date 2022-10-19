// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using BuildXL.Utilities;

namespace BuildXL.Scheduler.Distribution
{
    /// <summary>
    /// Different tracked resources for workers (higher values take precedence for determining overall limiting resource kind)
    /// </summary>
    internal readonly struct WorkerResource : IEquatable<WorkerResource>
    {
        /// <summary>
        /// See <see cref="Worker.TotalProcessSlots"/>
        /// </summary>
        public static readonly WorkerResource TotalProcessSlots = new WorkerResource(nameof(TotalProcessSlots), Precedence.TotalProcessSlots);

        /// <summary>
        /// See <see cref="Worker.TotalCacheLookupSlots"/>
        /// </summary>
        public static readonly WorkerResource TotalCacheLookupSlots = new WorkerResource(nameof(TotalCacheLookupSlots), Precedence.TotalCacheLookupSlots);

        /// <summary>
        /// See <see cref="Worker.AcquiredProcessSlots"/>
        /// </summary>
        public static readonly WorkerResource AvailableProcessSlots = new WorkerResource(nameof(AvailableProcessSlots), Precedence.AvailableProcessSlots);

        /// <summary>
        /// See <see cref="Worker.AcquiredCacheLookupSlots"/>
        /// </summary>
        public static readonly WorkerResource AvailableCacheLookupSlots = new WorkerResource(nameof(AvailableCacheLookupSlots), Precedence.AvailableCacheLookupSlots);

        /// <summary>
        /// See <see cref="Worker.AcquiredLightSlots"/>
        /// </summary>
        public static readonly WorkerResource AvailableLightSlots = new WorkerResource(nameof(AvailableLightSlots), Precedence.AvailableLightSlots);

        /// <summary>
        /// See <see cref="Worker.AcquiredMaterializeInputSlots"/>
        /// </summary>
        public static readonly WorkerResource AvailableMaterializeInputSlots = new WorkerResource(nameof(AvailableMaterializeInputSlots), Precedence.AvailableMaterializeInputSlots);

        /// <summary>
        /// See <see cref="Worker.TotalRamMb"/>
        /// </summary>
        public static readonly WorkerResource AvailableMemoryMb = new WorkerResource(nameof(AvailableMemoryMb), Precedence.AvailableMemoryMb);

        /// <summary>
        /// See <see cref="Worker.TotalCommitMb"/>
        /// </summary>
        public static readonly WorkerResource AvailableCommitMb = new WorkerResource(nameof(AvailableCommitMb), Precedence.AvailableCommitMb);

        /// <summary>
        /// See <see cref="LocalWorker.MemoryResourceAvailable"/>
        /// </summary>
        public static readonly WorkerResource MemoryResourceAvailable = new WorkerResource(nameof(MemoryResourceAvailable), Precedence.MemoryResourceAvailable);

        /// <nodoc/>
        public static readonly WorkerResource ModuleAffinity = new WorkerResource(nameof(ModuleAffinity), Precedence.ModuleAffinity);

        /// <summary>
        /// See <see cref="Worker.Status"/>
        /// </summary>
        public static readonly WorkerResource Status = new WorkerResource(nameof(Status), Precedence.Status);

        public readonly string Name;
        internal readonly Precedence PrecedenceType;

        private WorkerResource(string name, Precedence precedence)
        {
            Name = name;
            PrecedenceType = precedence;
        }

        internal static WorkerResource CreateSemaphoreResource(string name)
        {
            return new WorkerResource(name, Precedence.SemaphorePrecedence);
        }

        public bool Equals(WorkerResource other)
        {
            if (PrecedenceType != other.PrecedenceType)
            {
                return false;
            }
            else if (PrecedenceType == Precedence.SemaphorePrecedence)
            {
                // Only semaphore resources need to be compared by name. For other
                // resources the precedence has a 1 to 1 mapping to identity
                return Name.Equals(other.Name);
            }

            return true;
        }

        public override bool Equals(object obj)
        {
            return StructUtilities.Equals(this, obj);
        }

        public override int GetHashCode()
        {
            return PrecedenceType != Precedence.SemaphorePrecedence ?
                (int)PrecedenceType :
                Name.GetHashCode();
        }

        public override string ToString()
        {
            return PrecedenceType == Precedence.SemaphorePrecedence ?
                ("Semaphore." + Name) :
                Name;
        }

        public static bool operator ==(WorkerResource left, WorkerResource right)
        {
            return left.Equals(right);
        }

        public static bool operator !=(WorkerResource left, WorkerResource right)
        {
            return !left.Equals(right);
        }

        internal enum Precedence
        {
            Status,
            TotalCacheLookupSlots,
            TotalProcessSlots,
            AvailableLightSlots,
            AvailableCacheLookupSlots,
            AvailableProcessSlots,
            AvailableMemoryMb,
            AvailableCommitMb,
            MemoryResourceAvailable,
            AvailableMaterializeInputSlots,
            ModuleAffinity,
            SemaphorePrecedence,
        }
    }
}

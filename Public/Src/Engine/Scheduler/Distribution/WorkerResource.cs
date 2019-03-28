// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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
        /// See <see cref="Worker.TotalMemoryMb"/>
        /// </summary>
        public static readonly WorkerResource AvailableMemoryMb = new WorkerResource(nameof(AvailableMemoryMb), Precedence.AvailableMemoryMb);

        /// <summary>
        /// See <see cref="Worker.ResourcesAvailable"/>
        /// </summary>
        public static readonly WorkerResource ResourcesAvailable = new WorkerResource(nameof(ResourcesAvailable), Precedence.ResourcesAvailable);

        /// <summary>
        /// See <see cref="Worker.Status"/>
        /// </summary>
        public static readonly WorkerResource Status = new WorkerResource(nameof(Status), Precedence.Status);

        public readonly string Name;
        private readonly Precedence m_precedence;

        private WorkerResource(string name, Precedence precedence)
        {
            Name = name;
            m_precedence = precedence;
        }

        internal static WorkerResource CreateSemaphoreResource(string name)
        {
            return new WorkerResource(name, Precedence.SemaphorePrecedence);
        }

        public bool Equals(WorkerResource other)
        {
            if (m_precedence != other.m_precedence)
            {
                return false;
            }
            else if (m_precedence == Precedence.SemaphorePrecedence)
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
            return m_precedence != Precedence.SemaphorePrecedence ?
                (int)m_precedence :
                Name.GetHashCode();
        }

        public override string ToString()
        {
            return m_precedence == Precedence.SemaphorePrecedence ?
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

        private enum Precedence
        {
            Status,
            TotalCacheLookupSlots,
            TotalProcessSlots,
            AvailableCacheLookupSlots,
            AvailableProcessSlots,
            AvailableMemoryMb,
            ResourcesAvailable,
            SemaphorePrecedence,
        }
    }
}

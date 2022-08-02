// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Ipc.Interfaces;

namespace BuildXL.Ipc.Common
{
    /// <summary>
    /// A moniker is meant to provide a unique handle to a connection that may be established at a later time.
    /// IPC providers will return the same connection for the same moniker
    /// </summary>
    /// <remarks>
    /// It basically a semantic wrap for a guid: it is important that all instances of a given type of <see cref="IIpcProvider"/>
    /// produce monikers with unique IDs, so that monikers can be created with different <see cref="IIpcProvider"/> instances,
    /// and then, when rendered by the same <see cref="IIpcProvider"/> instance different monikers still get 
    /// rendered to different connection strings.
    /// </remarks>
    public readonly struct IpcMoniker : IEquatable<IpcMoniker>
    {
        /// <summary>
        /// Gets a fixed moniker.
        /// </summary>
        public static IpcMoniker GetFixedMoniker() => new("BuildXL.Ipc");

        /// <summary>
        /// Creates and returns a new moniker.
        /// </summary>
        /// <remarks>
        /// Ensures that unique monikers are returned throughout one program execution.
        /// </remarks>
        public static IpcMoniker CreateNew() => new IpcMoniker(Guid.NewGuid().ToString());

        /// <summary>
        /// Creates a new moniker with a given id.
        /// </summary>
        public static IpcMoniker Create(string id) => new IpcMoniker(id);

        /// <inheritdoc />
        public string Id { get; }

        /// <nodoc />
        public IpcMoniker(string id)
        {
            Contract.Requires(!string.IsNullOrEmpty(id));

            Id = id;
        }

        /// <summary>Returns true if <paramref name="obj"/> is of the same type and has the same <see cref="Id"/>.</summary>
        public override bool Equals(object obj)
        {
            if (obj == null || GetType() != obj.GetType())
            {
                return false;
            }

            return Equals((IpcMoniker)obj);
        }

        /// <summary>Returns the hash code of the <see cref="Id"/> property.</summary>
        public override int GetHashCode()
        {
            return Id.GetHashCode();
        }

        /// <inheritdoc />
        public bool Equals(IpcMoniker other) => Id == other.Id;

        /// <nodoc />
        public static bool operator ==(IpcMoniker left, IpcMoniker right) => left.Equals(right);

        /// <nodoc />
        public static bool operator !=(IpcMoniker left, IpcMoniker right) => !(left == right);
    }
}

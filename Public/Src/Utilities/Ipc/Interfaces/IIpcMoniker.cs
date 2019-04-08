// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;

namespace BuildXL.Ipc.Interfaces
{
    /// <summary>
    /// An opaque type used by <see cref="IIpcProvider"/> to uniquely identify and track client-server interactions.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1040:ReplaceInterfaceWithAttribute", Justification = "Totally not suitable here")]
    public interface IIpcMoniker : IEquatable<IIpcMoniker>
    {
        /// <summary>
        /// A globally unique identifier for all monikers created by the same concrete type of <see cref="IIpcProvider"/>.
        /// </summary>
        /// <remarks>
        /// It is important that all instances of a given type of <see cref="IIpcProvider"/> produce monikers with
        /// unique IDs, so that monikers can be created with different <see cref="IIpcProvider"/> instances, and
        /// then, when rendered by the same <see cref="IIpcProvider"/> instance different monikers still get
        /// rendered to different connection strings.
        /// </remarks>
        string Id { get; }
    }
}

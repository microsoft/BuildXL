// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Threading;

namespace BuildXL.Cache.ContentStore.Utils
{
    /// <summary>
    /// Exposes a wrapped instance. Primarily used for testing purposes.
    /// </summary>
    public interface IComponentWrapper<out T>
    {
        /// <summary>
        /// The inner component
        /// </summary>
        T Inner { get; }
    }
}

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Sdk
{
    /// <summary>
    /// An object representing a promise to a value. Used by the cycle detector to detect a cycle.
    /// </summary>
    /// <remarks>
    /// If two IValuePromise are equal, the cycle detector assumes they will eventually yield to the same value.
    /// It is up to the user of the cycle detector to determine what constitutes a value promise.
    /// </remarks>
    [SuppressMessage("Microsoft.Design", "CA1040:Avoid empty interfaces",
            Justification = "Compile-time identification to make intent clearer")]
    public interface IValuePromise
    {
    }

    /// <summary>
    /// Cycle detector interface used for evaluation
    /// </summary>
    /// <remarks>
    /// Value promise chains can be explicitly added yielding a handle object, and removed by disposing that handle object.
    /// When a cycle is identified, a cycle announcer callback is invoked.
    /// </remarks>
    public interface ICycleDetector : IDisposable
    {
        /// <summary>
        /// Ensures that the cycle detector is actually processing chains in a background thread
        /// </summary>
        void EnsureStarted();

        /// <summary>
        /// Increase the priority of the cycle detector
        /// </summary>
        IDisposable IncreasePriority();

        /// <summary>
        /// Adds a request to detect a cycle (or deadlock) given an additional chain of value promises.
        /// </summary>
        /// <remarks>
        /// This method returns a disposable object that must be used to indicate later that the given chain is no longer valid
        /// (because evaluation progressed beyond what used to be the current value promise).
        /// If a cycle (or deadlock) is found, the cycle announcer action is invoked.
        /// A simple cycle is induced by a simple (cyclic) chain.
        /// A deadlock is detected if the sum of multiple active chains form a cycle in the induced directed graph.
        /// </remarks>
        [JetBrains.Annotations.NotNull]
        IDisposable AddValuePromiseChain([JetBrains.Annotations.NotNull]Func<IValuePromise[]> valuePromiseChainGetter, [JetBrains.Annotations.NotNull]Action cycleAnnouncer);
    }
}

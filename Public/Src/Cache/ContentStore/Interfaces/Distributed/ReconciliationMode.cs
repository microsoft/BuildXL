// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace BuildXL.Cache.ContentStore.Interfaces.Distributed
{
    /// <summary>
    /// Mode determining the method of reconciliation we perform
    /// </summary>
    public enum ReconciliationMode
    {
        /// <summary>
        /// Disabling reconciliation is an unsafe option that can cause builds to fail because the machine's state can be off compared to the LLS's state.
        /// Please do not set this property for long period of time. 
        /// </summary>
        None,

        /// <summary>
        /// Reconcile once during startup
        /// </summary>
        Once,

        /// <summary>
        /// Reconcile after every restore checkpoint
        /// </summary>
        Checkpoint,
    }
}

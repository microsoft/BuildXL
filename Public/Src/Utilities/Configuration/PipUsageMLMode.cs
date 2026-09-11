// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Controls which process pips use the Pip Usage ML model.
    /// </summary>
    public enum PipUsageMLMode
    {
        /// <summary>Do not use the model.</summary>
        Disabled = 0,

        /// <summary>Use the model only when historical performance data is unavailable.</summary>
        Cold = 1,

        /// <summary>Use the model for both cold and warm pips.</summary>
        ColdAndWarm = 2,
    }
}
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

        /// <summary>Use the model only for pips without historical performance data.</summary>
        Cold = 1,

        /// <summary>Use the model for both cold and warm pips.</summary>
        ColdAndWarm = 2,

        /// <summary>Use the model for all eligible pips only when the historical performance table is unavailable.</summary>
        HistoricDataUnavailable = 3,
    }
}
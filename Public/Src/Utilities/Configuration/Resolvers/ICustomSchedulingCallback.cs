// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// Callback specified by module name and scheduling function to customize how a JavaScript project is scheduled
    /// </summary>
    public interface ICustomSchedulingCallback
    {
        /// <summary>
        /// Module name.
        /// </summary>
        string Module { get; }
        
        /// <summary>
        /// The scheduling function to use
        /// </summary>
        /// <remarks>
        /// Can be a dotted identified to denote a function nested in namespaces.
        /// </remarks>
        string SchedulingFunction { get; }
    }
}

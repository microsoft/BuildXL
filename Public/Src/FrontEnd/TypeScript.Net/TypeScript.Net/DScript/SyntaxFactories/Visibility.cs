// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace TypeScript.Net.DScript
{
    /// <summary>
    /// Visibility of a type or a member.
    /// </summary>
    public enum Visibility
    {
        /// <summary>
        /// Default visibility.
        /// </summary>
        None,
        
        /// <summary>
        /// The member is exported.
        /// </summary>
        Export,

        /// <summary>
        /// The member is public.
        /// </summary>
        Public,
    }
}

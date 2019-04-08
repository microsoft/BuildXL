// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

namespace TypeScript.Net.Parsing
{
    /// <summary>
    /// Class allowing a boolean result to be returned by NodeWalker
    /// </summary>
    /// <remarks>
    /// This boxed version of boolean is needed because NodeWalker's generic methods restrict
    /// type T to be a class (as a way to restrict T to be a nullable type).
    /// </remarks>
    public sealed class Bool
    {
        /// <summary>
        /// Global instance for boxed <code>false</code> literal.
        /// </summary>
        public const Bool False = null;

        /// <summary>
        /// Global instance for boxed <code>true</code> literal.
        /// </summary>
        public static readonly Bool True = new Bool(true);

        private Bool(bool result)
        {
            Result = result;
        }

        /// <summary>
        /// Result of the operation.
        /// </summary>
        public bool Result { get; }

        /// <nodoc/>
        public static implicit operator Bool(bool b)
        {
            return b ? True : False;
        }

        /// <nodoc/>
        public static implicit operator bool(Bool b)
        {
            return b == True;
        }
    }
}

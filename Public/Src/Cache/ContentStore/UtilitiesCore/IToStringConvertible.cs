// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Text;

namespace BuildXL.Cache.ContentStore.UtilitiesCore
{
    /// <summary>
    /// Used to specify that a type can write itself into a <see cref="StringBuilder"/>
    /// </summary>
    public interface IToStringConvertible
    {
        /// <summary>
        /// Write type into the specified <see cref="StringBuilder"/>
        /// </summary>
        public void ToString(StringBuilder sb);
    }
}

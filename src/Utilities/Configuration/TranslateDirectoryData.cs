// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Utilities.Configuration
{
    /// <summary>
    /// A representation of the internal translate directory data.
    /// </summary>
    public sealed class TranslateDirectoryData
    {
        /// <summary>
        /// The to path
        /// </summary>
        public AbsolutePath ToPath { get; set; }

        /// <summary>
        /// The from path.
        /// </summary>
        public AbsolutePath FromPath { get; set; }

        /// <summary>
        /// The raw user specified option.
        /// </summary>
        public string RawUserOption { get; set; }

        /// <summary>
        /// Constructor.
        /// </summary>
        public TranslateDirectoryData()
        {
            FromPath = AbsolutePath.Invalid;
            ToPath = AbsolutePath.Invalid;
            RawUserOption = null;
        }

        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="userOption">The raw user option specified.</param>
        /// <param name="from">The from path for path translation.</param>
        /// <param name="to">The to path for path translation.</param>
        public TranslateDirectoryData(string userOption, AbsolutePath from, AbsolutePath to)
        {
            Contract.Requires(from.IsValid);
            Contract.Requires(to.IsValid);

            ToPath = to;
            FromPath = from;
            RawUserOption = userOption;
        }
    }
}

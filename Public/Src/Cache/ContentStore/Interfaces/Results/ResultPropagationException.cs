// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    /// Special <see cref="Exception"/> type that wraps non successful result.
    /// </summary>
    public class ResultPropagationException : Exception
    {
        /// <summary>
        /// A failure wrapped into the exception instance.
        /// </summary>
        public ResultBase Result { get; }

        /// <nodoc />
        public ResultPropagationException(ResultBase result)
            : base(result.ErrorMessage, result.Exception)
        {
            Contract.RequiresNotNull(result);
            Contract.Requires(!result.Succeeded);

            Result = result;
        }

        /// <inheritdoc />
        public override string ToString() => Result.ToString();
    }
}

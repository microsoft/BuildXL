// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics.ContractsLight;

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
            : base(GetErrorMessage(result), result.Exception)
        {
            Contract.RequiresNotNull(result);
            Contract.Requires(!result.Succeeded);

            Result = result;
        }

        /// <inheritdoc />
        public override string ToString() => Result.ToString();

        /// <nodoc />
        private static string GetErrorMessage(ResultBase error)
        {
            Contract.Requires(!error.Succeeded);
            Contract.Requires(error.Error != null);
            return error.Error.ErrorMessage;
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Diagnostics.ContractsLight;
using System.Threading;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Extensions for <see cref="CancellationToken"/>
    /// </summary>
    public static class CancellationTokenExtensions
    {
        /// <summary>
        /// Creates a <see cref="Failure"/> out of an instance of <see cref="CancellationToken"/>.
        /// </summary>
        public static Failure CreateFailure(this CancellationToken cancellationToken)
        {
            Contract.Requires(cancellationToken.IsCancellationRequested);
            return new CancellationFailure();
        }
    }

    /// <summary>
    /// Failure due to requested cancellation.
    /// </summary>
    public sealed class CancellationFailure : Failure
    {
        private const string Message = "Cancellation requested";

        /// <inheritdoc />
        public override BuildXLException CreateException() => new BuildXLException(Message);

        /// <inheritdoc />
        public override string Describe() => Message;

        /// <inheritdoc />
        public override BuildXLException Throw()
        {
            throw CreateException();
        }
    }
}

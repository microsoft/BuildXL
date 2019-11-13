// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Sessions;
#nullable enable

namespace BuildXL.Cache.ContentStore.Interfaces.Results
{
    /// <summary>
    ///     Result of the CreateSession call.
    /// </summary>
    public class CreateSessionResult<T> : BoolResult, IEquatable<CreateSessionResult<T>>
        where T : IName
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="CreateSessionResult{T}"/> class.
        /// </summary>
        public CreateSessionResult(T session)
        {
            Contract.Requires(session != null);
            Session = session;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="CreateSessionResult{T}"/> class.
        /// </summary>
        public CreateSessionResult(string errorMessage, string? diagnostics = null)
            : base(errorMessage, diagnostics)
        {
            Session = default;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="CreateSessionResult{T}"/> class.
        /// </summary>
        public CreateSessionResult(Exception exception, string? message = null)
            : base(exception, message)
        {
            Session = default;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="CreateSessionResult{T}"/> class.
        /// </summary>
        public CreateSessionResult(ResultBase other, string? message = null)
            : base(other, message)
        {
            Session = default;
        }

        /// <summary>
        /// Gets the created session.
        /// </summary>
        [AllowNull]
        [MaybeNull]
        public readonly T Session;

        /// <inheritdoc />
        public bool Equals(CreateSessionResult<T> other)
        {
            if (other is null || !base.Equals(other))
            {
                return false;
            }

            if (Session?.Name != other.Session?.Name)
            {
                return false;
            }

            return true;
        }

        /// <inheritdoc />
        public override bool Equals(object? obj)
        {
            return obj is CreateSessionResult<T> other && Equals(other);
        }

        /// <inheritdoc />
        public override int GetHashCode()
        {
            return base.GetHashCode() ^ (Session?.Name.GetHashCode() ?? 0);
        }

        /// <nodoc />
        public CreateSessionResult<B> Map<B>(Func<T, B> transform)
            where B : IName
        {
            if (Success)
            {
                return new CreateSessionResult<B>(transform(Session));
            }
            else
            {
                return new CreateSessionResult<B>(this);
            }
        }
    }
}

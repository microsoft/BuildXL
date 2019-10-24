// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ContentStore.Interfaces.Results;

namespace BuildXL.Cache.ContentStore.Interfaces.Distributed
{
    /// <summary>
    /// ConnectionStringResult wrapper class.
    /// </summary>
    public class ConnectionStringResult : BoolResult
    {
        /// <summary>
        /// The Connection String.
        /// </summary>
        public string ConnectionString { get; }

        /// <summary>
        /// Creates a successful result.
        /// </summary>
        public static ConnectionStringResult CreateSuccess(string connectionString)
        {
            Contract.Requires(connectionString != null);
            return new ConnectionStringResult(connectionString);
        }

        /// <summary>
        /// Creates a failure from another result.
        /// </summary>
        public static ConnectionStringResult CreateFailure(ResultBase other, string message = null)
        {
            Contract.Requires(other != null);
            return new ConnectionStringResult(other, message);
        }

        /// <summary>
        /// Creates a failure from an exception.
        /// </summary>
        public static ConnectionStringResult CreateFailure(Exception exception, string message = null)
        {
            Contract.Requires(exception != null);
            return new ConnectionStringResult(exception, message);
        }

        /// <summary>
        /// Creates a failed result.
        /// </summary>
        public static ConnectionStringResult CreateFailure(string errorMessage, string diagnostics = null)
        {
            Contract.Requires(errorMessage != null);
            return new ConnectionStringResult(errorMessage, diagnostics);
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionStringResult" /> class.
        /// </summary>
        private ConnectionStringResult(string connectionString)
        {
            ConnectionString = connectionString;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionStringResult" /> class.
        /// </summary>
        public ConnectionStringResult(ResultBase other, string message)
            : base(other, message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionStringResult" /> class.
        /// </summary>
        private ConnectionStringResult(Exception exception, string message)
            : base(exception, message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConnectionStringResult" /> class.
        /// </summary>
        private ConnectionStringResult(string errorMessage, string diagnostics)
            : base(errorMessage, diagnostics)
        {
        }
    }
}

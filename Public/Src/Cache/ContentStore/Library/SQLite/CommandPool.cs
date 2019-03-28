// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Data.SQLite;
using System.Diagnostics.ContractsLight;
using System.Threading.Tasks;
using BuildXL.Cache.ContentStore.Pooling;

namespace BuildXL.Cache.ContentStore.SQLite
{
    /// <summary>
    ///     Thread-safe pool of reusable SQLiteCommands.
    /// </summary>
    /// <typeparam name="T">The return value of the command's execution.</typeparam>
    public sealed class CommandPool<T> : IDisposable
    {
        private readonly ObjectPool<SQLiteCommand> _pool;
        private readonly Func<SQLiteCommand, Task<T>> _func;
        private bool _disposed;

        /// <summary>
        ///     Initializes a new instance of the <see cref="CommandPool{T}" /> class.
        /// </summary>
        /// <param name="connection">Database connection to use.</param>
        /// <param name="commandText">Text used to construct each SQLiteCommand.</param>
        /// <param name="func">Callback to process a command.</param>
        [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Security", "CA2100:Review SQL queries for security vulnerabilities")]
        public CommandPool(
            SQLiteConnection connection,
            string commandText,
            Func<SQLiteCommand, Task<T>> func)
        {
            Contract.Requires(connection != null);
            Contract.Requires(commandText != null);
            Contract.Requires(func != null);

            _pool = new ObjectPool<SQLiteCommand>(
                () => new SQLiteCommand(commandText, connection),
                sqliteCommand =>
                {
                    sqliteCommand.Parameters.Clear();
                    return sqliteCommand;
                });
            _func = func;
        }

        /// <summary>
        ///     Entry-point for invoking the command.
        /// </summary>
        /// <param name="parameters">Parameters added to the command before execution.</param>
        /// <returns>Return value of the command's execution.</returns>
        public async Task<T> RunAsync(params SQLiteParameter[] parameters)
        {
            using (var command = _pool.GetInstance())
            {
                command.Instance.Parameters.AddRange(parameters);
                return await _func(command.Instance);
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _pool?.Dispose();
            _disposed = true;
        }
    }
}

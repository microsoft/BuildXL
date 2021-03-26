// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Data to be included in <see cref="SandboxedProcessInfo"/> for remote execution.
    /// </summary>
    public class RemoteSandboxedProcessData
    {
        /// <summary>
        /// Temporary directories that need to be created prior to pip execution on remote worker/agent.
        /// </summary>
        /// <remarks>
        /// Pips often assume that temp directories exist prior to their executions. Since pips should not share
        /// temp directories, they should not be brought from the client. Thus, they have to be created before
        /// pips execute.
        /// 
        /// One should not include the output directories to be pre-created because that will prevent the ProjFs
        /// on agent to have a callback to the client. For example, a pip A can write an output D/f.txt which will be
        /// consumed by a pip B, and in turn, B produces D/g.txt. If D is pre-created on agent before B executes, then
        /// B will not find D/f.txt because ProjFs already see D as materialized.
        /// </remarks>
        public readonly IReadOnlyList<string> TempDirectories;

        /// <summary>
        /// Creates an instance of <see cref="RemoteSandboxedProcessData"/>.
        /// </summary>
        /// <param name="tempDirectories">List of temp directories.</param>
        public RemoteSandboxedProcessData(IReadOnlyList<string> tempDirectories)
        {
            Contract.Requires(tempDirectories != null);
            TempDirectories = tempDirectories;
        }

        /// <summary>
        /// Serializes this instance using an instance of <see cref="BuildXLWriter"/>.
        /// </summary>
        public void Serialize(BuildXLWriter writer) => writer.WriteReadOnlyList(TempDirectories, (w, s) => w.Write(s));

        /// <summary>
        /// Deserializes an instance of <see cref="RemoteSandboxedProcessData"/> using an instance of <see cref="BuildXLReader"/>.
        /// </summary>
        public static RemoteSandboxedProcessData Deserialize(BuildXLReader reader) =>
            new RemoteSandboxedProcessData(reader.ReadReadOnlyList(r => r.ReadString()));
    }
}

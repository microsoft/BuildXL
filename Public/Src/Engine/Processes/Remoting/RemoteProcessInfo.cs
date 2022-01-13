// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

#nullable enable

namespace BuildXL.Processes.Remoting
{
    /// <summary>
    /// Info for process to be executed remotely.
    /// </summary>
    /// <param name="Executable">Executable.</param>
    /// <param name="Args">Arguments.</param>
    /// <param name="WorkingDirectory">Working directory.</param>
    /// <param name="Environments">Environments.</param>
    public record RemoteProcessInfo(string Executable, string? Args, string WorkingDirectory, IReadOnlyDictionary<string, string> Environments);
}

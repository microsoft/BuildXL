// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.Serialization;

namespace BuildXL.Ide.LanguageServer
{
    /// <summary>
    /// Represents DScript-specific settings.
    /// </summary>
    /// <remarks>
    /// Not every setting is used by the server. Some of them, like 'turnOnModuleBrowser' are client-specific.
    /// </remarks>
    [DataContract]
    public class DScriptSettings
    {
        /// <nodoc />
        [DataMember(Name = "maxNumberOfProblems")]
        public bool MaxNumberOfProblems { get; set; }

        /// <nodoc />
        [DataMember(Name = "skipNuget")]
        public bool SkipNuget { get; set; }

        /// <nodoc />
        [DataMember(Name = "fastFailOnError")]
        public bool FailFastOnError { get; set; }

        /// <nodoc />
        [DataMember(Name = "debugOnStart")]
        public bool DebugOnStart { get; set; }

        /// <nodoc />
        [DataMember(Name = "logJsonRpcMessages")]
        public bool LogJsonRpcMessages { get; set; }

        /// <nodoc />
        [DataMember(Name = "turnOnModuleBrowser")]
        public bool TurnOnModuleBrowser { get; set; }

        /// <nodoc />
        [DataMember(Name = "turnOnSolutionExplorer")]
        public bool TurnOnSolutionExplorer { get; set; }
    }
}

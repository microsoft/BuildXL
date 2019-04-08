// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using Newtonsoft.Json;

namespace BuildXL.FrontEnd.Ninja.Serialization
{
    /// <summary>
    /// TODO
    /// </summary>
    /// <remarks>
    /// The main purpose of this class is to represent an Ninja node to be scheduled by BuildXL.
    /// This class is designed to be JSON serializable.
    /// </remarks>
    [JsonObject]
    public sealed class NinjaNode
    {
        /// <nodoc/>
        [JsonProperty(PropertyName = "rule")]
        public string Rule { get; private set; }

        /// <summary>
        /// The command that triggers the execution of this node
        /// The command is a full command line, with the name of the executable in the first place
        /// (or its path) and the arguments after a space character.
        /// </summary>
        [JsonProperty(PropertyName = "command")]
        public string Command { get; private set; }

        /// <summary>
        /// All the inputs that this node uses.
        /// These correspond with the 'inputs' specified in https://ninja-build.org/manual.html#_build_statements,
        /// except that if one of the inputs is the output of a phony rule 
        /// (https://ninja-build.org/manual.html#_the_literal_phony_literal_rule),
        /// then it is replaced by the input of that phony rule 
        /// (if those contain phonies as well, this process goes on until no inputs are the result of phony rules).
        /// </summary>
        [JsonProperty(PropertyName = "inputs")]
        public IReadOnlySet<AbsolutePath> Inputs { get; private set; }

        /// <summary>
        /// The outputs resulting of the execution of this node
        /// These correspond to the 'outputs' specified in https://ninja-build.org/manual.html#_build_statements
        /// </summary>
        [JsonProperty(PropertyName = "outputs")]
        public IReadOnlySet<AbsolutePath> Outputs { get; private set; }

        /// <summary>
        /// Direct dependencies of this node, that have to execute before it.
        /// </summary>
        [JsonProperty(PropertyName = "dependencies")]
        public IReadOnlyCollection<NinjaNode> Dependencies { get; private set; }

        /// <summary>
        /// A ninja rule can have an associated response file.
        /// This object encapsulates one of these response files with its name and contents.
        /// </summary>
        [JsonProperty(PropertyName = "responseFile")]
        public NinjaResponseFile? ResponseFile { get; private set; }

        /// <nodoc />
        public NinjaNode(string rule, string command, IReadOnlySet<AbsolutePath> inputs, IReadOnlySet<AbsolutePath> outputs, IReadOnlyCollection<NinjaNode> dependencies)
        {
            Rule = rule;
            Inputs = inputs;
            Command = command;
            Outputs = outputs;
            Dependencies = dependencies;
        }
    }

    /// <summary>
    /// The contents of a response file for a ninja pip and where it is supposed to be written
    /// </summary>
    [JsonObject(IsReference =  false)]
    public struct NinjaResponseFile
    {
        /// <summary>
        /// The path to where the response file should be written
        /// </summary>
        [JsonProperty(PropertyName = "name")]
        public AbsolutePath Path { get; private set; }

        /// <summary>
        /// The content of the response file
        /// </summary>
        [JsonProperty(PropertyName = "content")]
        public string Content { get; private set; }
    } 
}

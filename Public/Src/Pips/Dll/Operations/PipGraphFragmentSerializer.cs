// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using BuildXL.Utilities;

namespace BuildXL.Pips.Operations
{
    /// <summary>
    /// Class to handle serialization/deserialization of pip graph fragments
    /// </summary>
    public class PipGraphFragmentSerializer
    {
        /// <summary>
        /// Total pips in the fragment.  Until this is read in from the file, it is 0.
        /// </summary>
        public int TotalPips { get; private set; }

        /// <summary>
        /// Total pips deserialized so far.
        /// </summary>
        public int PipsDeserialized { get; private set; }

        /// <summary>
        /// Total pips serialized so far.
        /// </summary>
        public int PipsSerialized { get; private set; }

        /// <summary>
        /// Description of the fragment, for printing on the console
        /// </summary>
        public string FragmentDescription { get; private set; }

        /// <summary>
        /// Deserialize a pip graph fragment and call the given handleDeserializedPip function on each pip deserialized
        /// Returns true if successfully handled all pips.
        /// </summary>
        public bool Deserialize(string fragmentDescription, PipExecutionContext context, PipGraphFragmentContext pipFragmentContext, AbsolutePath filePath, Func<Pip, bool> handleDeserializedPip)
        {
            FragmentDescription = fragmentDescription;
            PipsDeserialized = 0;
            string fileName = filePath.ToString(context.PathTable);
            using (FileStream stream = new FileStream(fileName, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (RemapReader reader = new RemapReader(pipFragmentContext, stream, context))
            {
                while (reader.ReadBoolean())
                {
                    var pip = Pip.Deserialize(reader);
                    if (handleDeserializedPip != null)
                    {
                        if (!handleDeserializedPip.Invoke(pip))
                        {
                            return false;
                        }
                    }

                    PipsDeserialized++;
                }
            }

            return true;
        }

        /// <summary>
        /// Serialize list of pips to a file
        /// </summary>
        public void Serialize(string fragmentName, PipExecutionContext context, AbsolutePath filePath, IEnumerable<Pip> pipsToSerialize)
        {
            FragmentDescription = fragmentName;
            PipsSerialized = 0;
            string fileName = filePath.ToString(context.PathTable);
            Contract.Assert(!File.Exists(fileName), "Pip graph fragment file to write to already exists");
            using (FileStream stream = new FileStream(fileName, FileMode.Open, FileAccess.Write, FileShare.None))
            using (RemapWriter writer = new RemapWriter(stream, context))
            {
                foreach (var pip in pipsToSerialize)
                {
                    writer.Write(true);
                    pip.Serialize(writer);
                    PipsSerialized++;
                }

                writer.Write(false);
            }
        }
    }
}

// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;
using System.Runtime.Serialization;

namespace ContentStoreTest.Test
{
    [DataContract]
    public class LoggingConfiguration
    {
        [DataMember]
        public ICollection<string> Types;

        [DataMember]
        public string ConsoleSeverity;

        [DataMember]
        public string FileBaseName;

        [DataMember]
        public string FileSeverity;

        [DataMember]
        public bool FileAutoFlush;
    }
}

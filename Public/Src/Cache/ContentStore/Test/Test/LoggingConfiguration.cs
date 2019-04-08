// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

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

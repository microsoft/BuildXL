// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Runtime.Serialization;

namespace BuildXL.Ide.JsonRpc
{
    /// <summary>
    /// The parameters for the "dscript/sourceFileConfiguration" notification.
    /// </summary>
    [DataContract]
    public sealed class AddSourceFileConfiguration
    {
        /// <summary>
        /// The property name (such as "sources") to add the source file to.
        /// </summary>
        [DataMember(Name = "propertyName")]
        public string PropertyName { get; set; }

        /// <summary>
        /// The function name whose argument interface contains the property.
        /// referenced by <see cref="PropertyName"/>
        /// </summary>
        [DataMember(Name = "functionName")]
        public string FunctionName { get; set; }

        /// <summary>
        /// The position of the argument in the function call that is of the
        /// type referenced by <see cref="ArgumentTypeName"/>
        /// </summary>
        [DataMember(Name = "argumentPosition")]
        public int ArgumentPosition { get; set; }

        /// <summary>
        /// The type of the argument specified to the function (such as "Arguments") 
        /// </summary>
        [DataMember(Name = "argumentTypeName")]
        public string ArgumentTypeName { get; set; }

        /// <summary>
        /// The module for which the property specified in <see cref="ArgumentTypeName"/> belongs to (such as "Build.Wdg.Native.Tools.StaticLibrary")
        /// </summary>
        [DataMember(Name = "argumentTypeModuleName")]
        public string ArgumentTypeModuleName { get; set; }
    }
}

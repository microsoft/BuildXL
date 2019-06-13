// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Diagnostics.ContractsLight;
using BuildXL.FrontEnd.Script.Types;
using BuildXL.Utilities;

namespace BuildXL.FrontEnd.Script.Values
{
    /// <summary>
    /// Class that holds information about all well-known primitive types.
    /// </summary>
    /// <remarks>
    /// Unlike TypeScript, DScript has some additional primitive types like Path, Set, Map and others.
    /// </remarks>
    public sealed class PrimitiveTypes
    {
        internal StringTable StringTable { get; }

        /// <summary>
        /// Path type.
        /// </summary>
        public Type PathType { get; }

        /// <summary>
        /// PathAtom type.
        /// </summary>
        public Type PathAtomType { get; }

        /// <summary>
        /// RelativePath type.
        /// </summary>
        public Type RelativePathType { get; }

        /// <summary>
        /// File type.
        /// </summary>
        public Type FileType { get; }

        /// <summary>
        /// Directory type.
        /// </summary>
        public Type DirectoryType { get; }

        /// <summary>
        /// DerivedFile type.
        /// </summary>
        public Type DerivedFileType { get; }

        /// <summary>
        /// Static directory type.
        /// </summary>
        public Type StaticDirectoryType { get; }

        /// <summary>
        /// Execute arguments type.
        /// </summary>
        public Type ExecuteArgumentsType { get; }

        /// <summary>
        /// Execute result type.
        /// </summary>
        public Type ExecuteResultType { get; }

        /// <summary>
        /// IpcSendArguments type.
        /// </summary>
        public Type IpcSendArgumentsType { get; }

        /// <summary>
        /// IpcSendResult type.
        /// </summary>
        public Type IpcSendResultType { get; }

        /// <summary>
        /// CreateService arguments type.
        /// </summary>
        public Type CreateServiceArgumentsType { get; }

        /// <summary>
        /// CreateService result type.
        /// </summary>
        public Type CreateServiceResultType { get; }

        /// <summary>
        /// Object type.
        /// </summary>
        public Type ObjectType { get; }

        /// <summary>
        /// Array type.
        /// </summary>
        public Type ArrayType { get; }

        /// <summary>
        /// Module type.
        /// </summary>
        public Type ModuleType { get; }

        /// <summary>
        /// Enum type.
        /// </summary>
        public Type EnumType { get; }

        /// <summary>
        /// Closure type.
        /// </summary>
        public Type ClosureType { get; }

        /// <summary>
        /// Ambient type.
        /// </summary>
        public Type AmbientType { get; }

        /// <summary>
        /// Unit type.
        /// </summary>
        public Type UnitType { get; }

        /// <summary>
        /// Boolean type.
        /// </summary>
        public Type BooleanType { get; }

        /// <summary>
        /// String type.
        /// </summary>
        public Type StringType { get; }
        
        /// <summary>
        /// StringBuilder type.
        /// </summary>
        public Type StringBuilderType { get; }

        /// <summary>
        /// Number type.
        /// </summary>
        public Type NumberType { get; }

        /// <summary>
        /// Map type.
        /// </summary>
        public Type MapType { get; }

        /// <summary>
        /// Set type.
        /// </summary>
        public Type SetType { get; }

        /// <summary>
        /// Data type.
        /// </summary>
        public Type DataType { get; }

        /// <summary>
        /// IpcMoniker type.
        /// </summary>
        public Type IpcMonikerType { get; }

        /// <nodoc />
        public PrimitiveTypes(StringTable stringTable)
        {
            Contract.Requires(stringTable != null);

            StringTable = stringTable;

            PathType = CreateNamedTypeReference("Path");
            PathAtomType = CreateNamedTypeReference("PathAtom");
            RelativePathType = CreateNamedTypeReference("RelativePath");
            FileType = CreateNamedTypeReference("File");
            DirectoryType = CreateNamedTypeReference("Directory");
            DerivedFileType = CreateNamedTypeReference("DerivedFile");
            StaticDirectoryType = CreateNamedTypeReference("StaticDirectory");
            ExecuteArgumentsType = CreateNamedTypeReference("ExecuteArguments");
            ExecuteResultType = CreateNamedTypeReference("ExecuteResult");
            IpcSendArgumentsType = CreateNamedTypeReference("IpcSendArguments");
            IpcSendResultType = CreateNamedTypeReference("IpcSendResult");
            CreateServiceArgumentsType = CreateNamedTypeReference("CreateServiceArguments");
            CreateServiceResultType = CreateNamedTypeReference("CreateServiceResult");
            ObjectType = CreateNamedTypeReference("Object");
            ArrayType = CreateNamedTypeReference("Array");
            ModuleType = CreateNamedTypeReference("Module");
            EnumType = CreateNamedTypeReference("Enum");
            ClosureType = CreateNamedTypeReference("Closure");
            AmbientType = CreateNamedTypeReference("Ambient");
            UnitType = PrimitiveType.UnitType;
            BooleanType = PrimitiveType.BooleanType;
            StringType = PrimitiveType.StringType;
            NumberType = PrimitiveType.NumberType;
            StringBuilderType = CreateNamedTypeReference("StringBuilder");
            MapType = CreateNamedTypeReference("Map");
            SetType = CreateNamedTypeReference("Set");
            DataType = CreateNamedTypeReference("Data");
            IpcMonikerType = CreateNamedTypeReference("IpcMoniker");
        }

        /// <summary>
        /// Gets named type reference from a string.
        /// </summary>
        /// <remarks>This method should only be used temporarily to handle unexpected types.</remarks>
        public Type CreateNamedTypeReference(string name)
        {
            Contract.Requires(!string.IsNullOrWhiteSpace(name));
            return new NamedTypeReference(SymbolAtom.Create(StringTable, name));
        }
    }
}

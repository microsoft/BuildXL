// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Diagnostics.ContractsLight;
using System.Globalization;
using System.Linq;
using BuildXL.Ipc.Interfaces;
using BuildXL.Utilities;
using BuildXL.Utilities.Collections;
using TypeScript.Net.Extensions;
using static BuildXL.Utilities.FormattableStringEx;

#pragma warning disable SA1649 // File name must match first type name

namespace BuildXL.FrontEnd.Script.Ambients.Transformers
{
    // Please note, that every type in this file should be in sync with Prelude.ds file.

    /// <summary>
    /// Artifact kind
    /// </summary>
    /// <remarks>
    /// IMPORTANT: this enum (its name and the name of its literals) must be in sync with the enum defined in Prelude.Transformer.Arguments.dsc.
    /// 
    /// Note that since 'ArtifactKind' is defined as part of the prelude, evaluation of these enum constants will go through our ambient namespace 
    /// (<see cref="AmbientArtifactKind"/>), hence the ordinal values of these enum constants need not match the ordinal values of the corresponding
    /// DScript enum constants in Prelude.Transformer.Arguments.dsc (but there is no reason they shouldn't).
    /// </remarks>
    public enum ArtifactKind
    {
        /// <nodoc/>
        Undefined = 0,

        /// <nodoc/>
        Input = 1,

        /// <nodoc/>
        Output,

        /// <nodoc/>
        Rewritten,

        /// <nodoc/>
        None,

        /// <nodoc/>
        VsoHash,

        /// <nodoc/>
        FileId,

        /// <nodoc/>
        SharedOpaque,

        /// <nodoc/>
        DirectoryId
    }

    /// <summary>
    /// Types of the union type used by <see cref="Artifact"/> struct.
    /// </summary>
    public enum ArtifactValueType
    {
        /// <nodoc/>
        Undefined = 0,

        /// <nodoc/>
        File,

        /// <nodoc/>
        Directory,

        /// <nodoc/>
        AbsolutePath,
    }

    /// <summary>
    /// Represents an artifact in the build graph.
    /// This struct represents following union type: File | Path | Directory;
    /// The file and path are the same right now, that's why there is only two cases.
    /// </summary>
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct Artifact : IEquatable<Artifact>
    {
        // TODO: to save space we can potentially use C-style union types that will use the same space
        // But that's only possible if FileArtifact and DirectoryArtifact have the same size!
        private readonly FileArtifact m_fileArtifact;
        private readonly DirectoryArtifact m_directoryArtifact;
        private readonly AbsolutePath m_absolutePath;

        /// <nodoc/>
        public Artifact(ArtifactKind kind, FileArtifact file)
            : this()
        {
            Kind = kind;
            m_fileArtifact = file;
            Type = ArtifactValueType.File;
        }

        /// <nodoc/>
        public Artifact(ArtifactKind kind, AbsolutePath path, FileArtifact original = default(FileArtifact))
            : this()
        {
            Contract.Requires(kind != ArtifactKind.Rewritten || original.IsValid); // i.e., kind == ArtifactKind.Rewritten ==> original.IsValid

            Kind = kind;
            m_absolutePath = path;
            Type = ArtifactValueType.AbsolutePath;
            Original = original;
        }

        /// <nodoc/>
        public Artifact(ArtifactKind kind, DirectoryArtifact directory)
            : this()
        {
            // Rewrite only works for files.
            Contract.Requires(kind != ArtifactKind.Rewritten);

            Kind = kind;
            m_directoryArtifact = directory;
            Type = ArtifactValueType.Directory;
        }

        /// <nodoc/>
        public ArtifactKind Kind { get; }

        /// <summary>
        /// Relevant and required only when Kind == ArtifactKind.Rewritten: specifies the original input file to be copied and then rewritten
        /// </summary>
        public FileArtifact Original { get; }

        /// <nodoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
        public ArtifactValueType Type { get; }

        /// <nodoc/>
        public bool IsDefined => Type != ArtifactValueType.Undefined;

        /// <nodoc/>
        public FileArtifact File
        {
            get
            {
                Contract.Requires(Type == ArtifactValueType.File);
                return m_fileArtifact;
            }
        }

        /// <nodoc/>
        public DirectoryArtifact Directory
        {
            get
            {
                Contract.Requires(Type == ArtifactValueType.Directory);
                return m_directoryArtifact;
            }
        }

        /// <nodoc/>
        public AbsolutePath Path
        {
            get
            {
                Contract.Requires(Type == ArtifactValueType.AbsolutePath);
                return m_absolutePath;
            }
        }

        /// <inheridoc/>
        public bool Equals(Artifact other)
        {
            return m_fileArtifact.Equals(other.m_fileArtifact) &&
                   m_absolutePath.Equals(other.m_absolutePath) &&
                   m_directoryArtifact.Equals(other.m_directoryArtifact) &&
                   Kind == other.Kind &&
                   Type == other.Type;
        }

        /// <inheridoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is Artifact && Equals((Artifact)obj);
        }

        /// <inheridoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(
                m_fileArtifact.GetHashCode(),
                m_directoryArtifact.GetHashCode(),
                m_absolutePath.GetHashCode(),
                (int)Kind,
                (int)Type);
        }

        /// <nodoc />
        public static bool operator ==(Artifact left, Artifact right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(Artifact left, Artifact right)
        {
            return !left.Equals(right);
        }

        /// <inheridoc/>
        public override string ToString()
        {
            string valueAsString = null;
            switch (Type)
            {
                case ArtifactValueType.File:
                    valueAsString = File.Path.ToString();
                    break;
                case ArtifactValueType.Directory:
                    valueAsString = Directory.Path.ToString();
                    break;
                case ArtifactValueType.AbsolutePath:
                    valueAsString = Path.ToString();
                    break;
                case ArtifactValueType.Undefined:
                    valueAsString = "undefined";
                    break;
                default:
                    throw Contract.AssertFailure($"Unknown Type '{Type}'");
            }

            return I($"kind: {Kind}, type: {Type}, value: {valueAsString}");
        }
    }

    // TODO: think about generialized way of dealing with union types!

    /// <summary>
    /// Set of cases for <see cref="PrimitiveValue"/> union type.
    /// </summary>
    public enum PrimitiveValueType
    {
        /// <nodoc/>
        Undefined,

        /// <nodoc/>
        String,

        /// <nodoc/>
        Number,

        /// <nodoc/>
        Path,

        /// <nodoc/>
        RelativePath,

        /// <nodoc/>
        PathAtom,

        /// <nodoc/>
        IpcMoniker,
    }

    /// <nodoc/>
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct PrimitiveValue : IEquatable<PrimitiveValue>
    {
        private readonly string m_string;
        private readonly int m_number;
        private readonly AbsolutePath m_path;
        private readonly RelativePath m_relativePath;
        private readonly PathAtom m_pathAtom;
        private readonly IIpcMoniker m_moniker;

        /// <nodoc/>
        public PrimitiveValue(string value)
            : this()
        {
            Type = PrimitiveValueType.String;
            m_string = value;
        }

        /// <nodoc/>
        public PrimitiveValue(int number)
            : this()
        {
            m_number = number;
            Type = PrimitiveValueType.Number;
        }

        /// <nodoc/>
        public PrimitiveValue(AbsolutePath path)
            : this()
        {
            m_path = path;
            Type = PrimitiveValueType.Path;
        }

        /// <nodoc/>
        public PrimitiveValue(RelativePath relativePath)
            : this()
        {
            m_relativePath = relativePath;
            Type = PrimitiveValueType.RelativePath;
        }

        /// <nodoc/>
        public PrimitiveValue(PathAtom pathAtom)
            : this()
        {
            m_pathAtom = pathAtom;
            Type = PrimitiveValueType.PathAtom;
        }

        /// <nodoc/>
        public PrimitiveValue(IIpcMoniker moniker)
            : this()
        {
            m_moniker = moniker;
            Type = PrimitiveValueType.IpcMoniker;
        }

        /// <nodoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
        public PrimitiveValueType Type { get; }

        /// <nodoc/>
        public string String
        {
            get
            {
                Contract.Requires(Type == PrimitiveValueType.String);
                return m_string;
            }
        }

        /// <nodoc/>
        public int Number
        {
            get
            {
                Contract.Requires(Type == PrimitiveValueType.Number);
                return m_number;
            }
        }

        /// <nodoc/>
        public AbsolutePath Path
        {
            get
            {
                Contract.Requires(Type == PrimitiveValueType.Path);
                return m_path;
            }
        }

        /// <nodoc/>
        public RelativePath RelativePath
        {
            get
            {
                Contract.Requires(Type == PrimitiveValueType.RelativePath);
                return m_relativePath;
            }
        }

        /// <nodoc/>
        public PathAtom PathAtom
        {
            get
            {
                Contract.Requires(Type == PrimitiveValueType.PathAtom);
                return m_pathAtom;
            }
        }

        /// <nodoc/>
        public IIpcMoniker IpcMoniker
        {
            get
            {
                Contract.Requires(Type == PrimitiveValueType.IpcMoniker);
                return m_moniker;
            }
        }

        /// <summary>
        /// Returns string representation of the value. null if the value is 'undefined'.
        /// </summary>
        public string AsString()
        {
            switch (Type)
            {
                case PrimitiveValueType.Number:
                    return Number.ToString(CultureInfo.InvariantCulture);
                case PrimitiveValueType.String:
                    return String;
                default:
                    return null;
            }
        }

        /// <nodoc/>
        public bool IsDefined => Type != PrimitiveValueType.Undefined;

        /// <inheridoc/>
        public bool Equals(PrimitiveValue other)
        {
            return string.Equals(m_string, other.m_string)
                   && m_number == other.m_number
                   && m_path == other.m_path
                   && m_relativePath == other.m_relativePath
                   && m_pathAtom == other.m_pathAtom
                   && Type == other.Type;
        }

        /// <inheridoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is PrimitiveValue && Equals((PrimitiveValue)obj);
        }

        /// <inheridoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(
                m_string != null ? m_string.GetHashCode() : 0,
                m_number,
                m_path.GetHashCode(),
                m_relativePath.GetHashCode(),
                m_pathAtom.GetHashCode(),
                (int)Type);
        }

        /// <nodoc />
        public static bool operator ==(PrimitiveValue left, PrimitiveValue right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(PrimitiveValue left, PrimitiveValue right)
        {
            return !left.Equals(right);
        }

        /// <inheridoc/>
        public override string ToString()
        {
            if (!IsDefined)
            {
                return "undefined";
            }

            switch (Type)
            {
                case PrimitiveValueType.String:
                    return I($"\"{m_string}\"");
                case PrimitiveValueType.Number:
                    return m_number.ToString(CultureInfo.InvariantCulture);
                case PrimitiveValueType.Path:
                    return "<" + nameof(AbsolutePath) + ":" + m_path.Value.Value + ">";
                case PrimitiveValueType.RelativePath:
                    return "<" + nameof(RelativePath) + ":" + string.Join(",", m_relativePath.GetAtoms().Select(a => a.StringId.Value)) + ">";
                case PrimitiveValueType.PathAtom:
                    return "<" + nameof(PathAtom) + ":" + m_pathAtom.StringId.Value + ">";
            }

            return string.Empty;
        }
    }

    /// <summary>
    /// Type of command line argument.
    /// For more information, see comments in the Prelude.ds file.
    /// </summary>
    public enum ArgumentKind
    {
        /// <summary>
        /// Undefined, when default constructor of the <see cref="PrimitiveArgument"/> was used.
        /// </summary>
        Undefined = 0,

        /// <nodoc/>
        RawText = 1,

        /// <nodoc/>
        Regular,

        /// <nodoc/>
        Flag,

        /// <nodoc/>
        StartUsingResponseFile,
    }

    /// <summary>
    /// Primitive argument value wrapped with additional type information.
    /// </summary>
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct PrimitiveArgument : IEquatable<PrimitiveArgument>
    {
        /// <nodoc/>
        public PrimitiveArgument(ArgumentKind kind, PrimitiveValue value)
            : this()
        {
            Kind = kind;
            Value = value;
        }

        /// <nodoc/>
        public ArgumentKind Kind { get; }

        /// <nodoc/>
        public PrimitiveValue Value { get; }

        /// <inheridoc/>
        public bool Equals(PrimitiveArgument other)
        {
            return Kind == other.Kind && Value.Equals(other.Value);
        }

        /// <inheridoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is PrimitiveArgument && Equals((PrimitiveArgument)obj);
        }

        /// <inheridoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine((int)Kind, Value.GetHashCode());
        }

        /// <nodoc />
        public static bool operator ==(PrimitiveArgument left, PrimitiveArgument right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(PrimitiveArgument left, PrimitiveArgument right)
        {
            return !left.Equals(right);
        }

        /// <nodoc/>
        public bool IsDefined => Kind != ArgumentKind.Undefined;

        /// <inheridoc/>
        public override string ToString()
        {
            if (!IsDefined)
            {
                return "undefined";
            }

            return I($"{{ kind: {Kind}, value: {Value}}}");
        }
    }

    /// <summary>
    /// Set of cases for <see cref="ArgumentValue"/> union type.
    /// </summary>
    public enum ArgumentValueKind
    {
        /// <summary>
        /// Used when default constructor was used.
        /// </summary>
        Undefined = 0,

        /// <nodoc/>
        PrimitiveValue,

        /// <nodoc/>
        Artifact,

        /// <nodoc/>
        PrimitiveArgument,

        /// <nodoc/>
        CompoundValue,
    }

    /// <summary>
    /// Union type that can be used as an input value in tool command line arguments.
    /// </summary>
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct ArgumentValue : IEquatable<ArgumentValue>
    {
        private readonly PrimitiveValue m_primitiveValue;
        private readonly PrimitiveArgument m_primitiveArgument;
        private readonly Artifact m_artifact;
        private readonly CompoundArgumentValue m_compoundValue;

        /// <nodoc/>
        public ArgumentValue(PrimitiveValue primitiveValue)
            : this()
        {
            Contract.Requires(primitiveValue.IsDefined);

            m_primitiveValue = primitiveValue;
            Type = ArgumentValueKind.PrimitiveValue;
        }

        /// <nodoc/>
        public ArgumentValue(PrimitiveArgument primitiveArgument)
            : this()
        {
            Contract.Requires(primitiveArgument.IsDefined);

            m_primitiveArgument = primitiveArgument;
            Type = ArgumentValueKind.PrimitiveArgument;
        }

        /// <nodoc/>
        public ArgumentValue(Artifact artifact)
            : this()
        {
            Contract.Requires(artifact.IsDefined);

            m_artifact = artifact;
            Type = ArgumentValueKind.Artifact;
        }

        /// <nodoc/>
        public ArgumentValue(CompoundArgumentValue compoundValue)
            : this()
        {
            Contract.Requires(compoundValue.IsDefined);

            m_compoundValue = compoundValue;
            Type = ArgumentValueKind.CompoundValue;
        }

        /// <nodoc/>
        public static ArgumentValue FromNumber(int number)
        {
            return new ArgumentValue(new PrimitiveValue(number));
        }

        /// <nodoc/>
        public static ArgumentValue FromString(string value)
        {
            return new ArgumentValue(new PrimitiveValue(value));
        }

        /// <nodoc/>
        public static ArgumentValue FromAbsolutePath(AbsolutePath path)
        {
            return new ArgumentValue(new PrimitiveValue(path));
        }

        /// <nodoc/>
        public static ArgumentValue FromPathAtom(PathAtom pathAtom)
        {
            return new ArgumentValue(new PrimitiveValue(pathAtom));
        }

        /// <nodoc/>
        public static ArgumentValue FromRelativePath(RelativePath relativePath)
        {
            return new ArgumentValue(new PrimitiveValue(relativePath));
        }

        /// <nodoc/>
        public static ArgumentValue FromArtifact(ArtifactKind kind, FileArtifact file)
        {
            return new ArgumentValue(new Artifact(kind, file));
        }

        /// <nodoc/>
        public static ArgumentValue FromAbsolutePath(ArtifactKind kind, AbsolutePath file, FileArtifact original = default(FileArtifact))
        {
            return new ArgumentValue(new Artifact(kind, file, original));
        }

        /// <nodoc/>
        public static ArgumentValue FromArtifact(ArtifactKind kind, DirectoryArtifact directory)
        {
            return new ArgumentValue(new Artifact(kind, directory));
        }

        /// <nodoc/>
        public static ArgumentValue FromPrimitive(ArgumentKind kind, PrimitiveValue value)
        {
            return new ArgumentValue(new PrimitiveArgument(kind, value));
        }

        /// <nodoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
        public ArgumentValueKind Type { get; }

        /// <nodoc/>
        public bool IsDefined => Type != ArgumentValueKind.Undefined;

        /// <nodoc/>
        public PrimitiveValue PrimitiveValue
        {
            get
            {
                Contract.Requires(Type == ArgumentValueKind.PrimitiveValue);
                return m_primitiveValue;
            }
        }

        /// <nodoc/>
        public PrimitiveArgument PrimitiveArgument
        {
            get
            {
                Contract.Requires(Type == ArgumentValueKind.PrimitiveArgument);
                return m_primitiveArgument;
            }
        }

        /// <nodoc/>
        public Artifact Artifact
        {
            get
            {
                Contract.Requires(Type == ArgumentValueKind.Artifact);
                return m_artifact;
            }
        }

        /// <nodoc/>
        public CompoundArgumentValue CompoundValue
        {
            get
            {
                Contract.Requires(Type == ArgumentValueKind.CompoundValue);
                return m_compoundValue;
            }
        }

        /// <inheridoc/>
        public bool Equals(ArgumentValue other)
        {
            return m_primitiveValue.Equals(other.m_primitiveValue) &&
                m_primitiveArgument.Equals(other.m_primitiveArgument) &&
                m_artifact.Equals(other.m_artifact) &&
                m_compoundValue.Equals(other.m_compoundValue) &&
                Type == other.Type;
        }

        /// <inheridoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is ArgumentValue && Equals((ArgumentValue)obj);
        }

        /// <inheridoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(
                m_primitiveArgument.GetHashCode(),
                m_primitiveArgument.GetHashCode(),
                m_artifact.GetHashCode(),
                m_compoundValue.GetHashCode(),
                (int)Type);
        }

        /// <nodoc />
        public static bool operator ==(ArgumentValue left, ArgumentValue right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(ArgumentValue left, ArgumentValue right)
        {
            return !left.Equals(right);
        }

        /// <inheridoc/>
        public override string ToString()
        {
            switch (Type)
            {
                case ArgumentValueKind.PrimitiveValue:
                    return PrimitiveValue.ToString();
                case ArgumentValueKind.Artifact:
                    return Artifact.ToString();
                case ArgumentValueKind.PrimitiveArgument:
                    return PrimitiveArgument.ToString();
                case ArgumentValueKind.CompoundValue:
                    return CompoundValue.ToString();
                case ArgumentValueKind.Undefined:
                    return "undefined";
                default:
                    throw new ArgumentOutOfRangeException($"Unknown Type '{Type}'");
            }
        }
    }

    /// <summary>
    /// Special complex value that consists of multiple values separated using specified separator.
    /// </summary>
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct CompoundArgumentValue : IEquatable<CompoundArgumentValue>
    {
        /// <nodoc/>
        public CompoundArgumentValue(string separator, IReadOnlyList<ArgumentValue> values)
            : this()
        {
            Contract.Requires(values != null);

            Values = values;
            Separator = separator;
        }

        /// <nodoc/>
        public IReadOnlyList<ArgumentValue> Values { get; }

        /// <nodoc/>
        public string Separator { get; }

        /// <nodoc/>
        public bool IsDefined => Values != null;

        /// <inheridoc/>
        public bool Equals(CompoundArgumentValue other)
        {
            return ScalarListAreEquals(Values, other.Values) && string.Equals(Separator, other.Separator);
        }

        /// <inheridoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is CompoundArgumentValue && Equals((CompoundArgumentValue)obj);
        }

        /// <inheridoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(
                GetListHashCode(Values),
                Separator != null ? Separator.GetHashCode() : 0);
        }

        /// <nodoc />
        public static bool operator ==(CompoundArgumentValue left, CompoundArgumentValue right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(CompoundArgumentValue left, CompoundArgumentValue right)
        {
            return !left.Equals(right);
        }

        private static int GetListHashCode(IReadOnlyList<ArgumentValue> values)
        {
            return values?.Aggregate(0, (current, scalar) => HashCodeHelper.Combine(current, scalar.GetHashCode())) ?? 0;
        }

        private static bool ScalarListAreEquals(IReadOnlyList<ArgumentValue> left, IReadOnlyList<ArgumentValue> right)
        {
            if (left == null && right == null)
            {
                return true;
            }

            if (left == null || right == null)
            {
                return false;
            }

            if (left.Count != right.Count)
            {
                return false;
            }

            for (int i = 0; i < left.Count; i++)
            {
                if (!left[i].Equals(right[i]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <inheridoc/>
        public override string ToString()
        {
            var valuesAsString = string.Join(", ", (Values ?? Enumerable.Empty<ArgumentValue>()).Select(x => x.ToString()));
            return I($"{{separator: '{Separator}', values: [{valuesAsString}]");
        }

        internal void TraverseAllArtifacts<TState>(TState state, Action<TState, Artifact> artifactAction)
        {
            Contract.Requires(artifactAction != null);

            // calls given action for each artifact transitively found from this compound value
            foreach (var val in Values.AsStructEnumerable())
            {
                switch (val.Type)
                {
                    case ArgumentValueKind.Artifact:
                        artifactAction(state, val.Artifact);
                        break;
                    case ArgumentValueKind.CompoundValue:
                        val.CompoundValue.TraverseAllArtifacts(state, artifactAction);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Set of cases for <see cref="CommandLineValue"/> union type.
    /// </summary>
    public enum CommandLineValueType
    {
        /// <summary>
        /// Used when default constructor was used.
        /// </summary>
        Undefined = 0,

        /// <nodoc/>
        ScalarArgument,

        /// <nodoc/>
        ScalarArgumentArray,
    }

    /// <summary>
    /// Union type that represents value of the command line argument.
    /// </summary>
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct CommandLineValue : IEquatable<CommandLineValue>
    {
        private readonly ArgumentValue m_scalarArgument;
        private readonly ArgumentValue[] m_scalarArguments;

        /// <nodoc/>
        public CommandLineValue(ArgumentValue[] scalarArguments)
            : this()
        {
            Contract.Requires(scalarArguments != null);

            m_scalarArguments = scalarArguments;
            Type = CommandLineValueType.ScalarArgumentArray;
        }

        /// <nodoc/>
        public CommandLineValue(ArgumentValue scalarArgument)
            : this()
        {
            m_scalarArgument = scalarArgument;
            Type = CommandLineValueType.ScalarArgument;
        }

        /// <nodoc/>
        [SuppressMessage("Microsoft.Naming", "CA1721:PropertyNamesShouldNotMatchGetMethods")]
        public CommandLineValueType Type { get; }

        /// <nodoc/>
        public bool IsDefined => Type != CommandLineValueType.Undefined;

        /// <nodoc/>
        public ArgumentValue ScalarArgument
        {
            get
            {
                Contract.Requires(Type == CommandLineValueType.ScalarArgument);
                return m_scalarArgument;
            }
        }

        /// <nodoc/>
        [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
        public ArgumentValue[] ScalarArguments
        {
            get
            {
                Contract.Requires(Type == CommandLineValueType.ScalarArgumentArray);
                return m_scalarArguments;
            }
        }
        
        /// <inheridoc/>
        public bool Equals(CommandLineValue other)
        {
            return Type == other.Type &&
                (Type != CommandLineValueType.ScalarArgument || m_scalarArgument.Equals(other.m_scalarArgument)) &&
                (Type != CommandLineValueType.ScalarArgumentArray || m_scalarArguments.SequenceEqual(other.m_scalarArguments));
        }

        /// <inheridoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is CommandLineValue && Equals((CommandLineValue)obj);
        }

        /// <inheridoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(m_scalarArgument.GetHashCode(), m_scalarArguments.GetHashCode(), (int)Type);
        }

        /// <nodoc />
        public static bool operator ==(CommandLineValue left, CommandLineValue right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(CommandLineValue left, CommandLineValue right)
        {
            return !left.Equals(right);
        }

        /// <inheridoc/>
        public override string ToString()
        {
            switch (Type)
            {
                case CommandLineValueType.ScalarArgument:
                    return ScalarArgument.ToString();
                case CommandLineValueType.ScalarArgumentArray:
                    return I($"[{string.Join(", ", ScalarArguments.Select(x => x.ToString()))}]");
                default:
                    return "undefined";
            }
        }
    }

    /// <summary>
    /// Represents command line argument for the tool.
    /// </summary>
    [DebuggerDisplay("{ToString(),nq}")]
    public readonly struct Argument : IEquatable<Argument>
    {
        /// <nodoc/>
        public Argument(string name, CommandLineValue value)
            : this()
        {
            Value = value;
            Name = name;
        }

        /// <nodoc/>
        public string Name { get; }

        /// <nodoc/>
        public CommandLineValue Value { get; }

        /// <nodoc/>
        public bool IsDefined => !string.IsNullOrEmpty(Name) || Value.IsDefined;

        /// <inheridoc/>
        public bool Equals(Argument other)
        {
            return string.Equals(Name, other.Name) && Value.Equals(other.Value);
        }

        /// <inheridoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            return obj is Argument && Equals((Argument)obj);
        }

        /// <inheridoc/>
        public override int GetHashCode()
        {
            return HashCodeHelper.Combine(Name != null ? Name.GetHashCode() : 0, Value.GetHashCode());
        }

        /// <nodoc />
        public static bool operator ==(Argument left, Argument right)
        {
            return left.Equals(right);
        }

        /// <nodoc />
        public static bool operator !=(Argument left, Argument right)
        {
            return !left.Equals(right);
        }

        /// <inheridoc/>
        public override string ToString()
        {
            return I($"{{name: {Name ?? ")undefined"}, value: {Value}}}");
        }
    }

    internal static class Helpers
    {
        internal static Func<T, object> WrapActionToFunc<T>(Action<T> action)
        {
            return new Func<T, object>(t =>
            {
                action(t);
                return null;
            });
        }

        internal static Func<object> WrapActionToFunc(Action action)
        {
            return new Func<object>(() =>
            {
                action();
                return null;
            });
        }
    }
}

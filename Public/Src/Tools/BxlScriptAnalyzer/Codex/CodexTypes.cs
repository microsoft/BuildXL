// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;

#pragma warning disable 1591 // disabling warning about missing API documentation; TODO: Remove this line and write documentation!

namespace Codex.Analysis.External
{
    public class CodexSemanticStore
    {
        public readonly ListMap<CodexProject> Projects = new ListMap<CodexProject>();
        public readonly ListMap<CodexFile> Files = new ListMap<CodexFile>();
        public readonly ListMap<CodexSymbol> Symbols = new ListMap<CodexSymbol>();
        public readonly ListMap<CodexClassification> Classifications = new ListMap<CodexClassification>();
        public readonly ListMap<CodexRefKind> ReferenceKinds = new ListMap<CodexRefKind>();

        private readonly CodexId<CodexRefKind>[] m_wellKnownReferenceKinds;

        private readonly string m_directory;
        private readonly string m_filesDirectory;

        public CodexSemanticStore(string directory)
        {
            m_directory = directory;
            m_filesDirectory = Path.Combine(m_directory, "files");
            m_wellKnownReferenceKinds = Enum.GetValues(typeof(WellKnownReferenceKinds)).OfType<WellKnownReferenceKinds>()
                .Select(kind => ReferenceKinds.Add(new CodexRefKind() { Name = kind.ToString() }))
                .ToArray();
        }

        public void Save()
        {
            using (var writer = new StreamWriter(Path.Combine(m_directory, "classifications.txt")))
            {
                foreach (var classification in Classifications.List)
                {
                    classification.Write(writer);
                }
            }

            using (var writer = new StreamWriter(Path.Combine(m_directory, "projects.txt")))
            {
                writer.WriteLine(CodexProject.Columns);
                foreach (var project in Projects.List)
                {
                    project.WriteEntry(writer);
                }
            }

            using (var writer = new StreamWriter(Path.Combine(m_directory, "files.txt")))
            {
                writer.WriteLine(CodexFile.Columns);
                foreach (var file in Files.List)
                {
                    file.WriteEntry(writer);
                }
            }

            using (var writer = new StreamWriter(Path.Combine(m_directory, "symbols.txt")))
            {
                writer.WriteLine(CodexSymbol.Columns);
                foreach (var symbol in Symbols.List)
                {
                    symbol.Write(writer);
                }
            }

            using (var writer = new StreamWriter(Path.Combine(m_directory, "referencekinds.txt")))
            {
                foreach (var referenceKind in ReferenceKinds.List)
                {
                    referenceKind.Write(writer);
                }
            }

            //foreach (var file in Files.List)
            //{
            //    SaveFile(file);
            //}
        }

        public void Load()
        {
            using (var reader = new StreamReader(Path.Combine(m_directory, "classifications.txt")))
            {
                CodexClassification classification;
                while ((classification = CodexClassification.Read(reader)) != null)
                {
                    Classifications.Add(classification);
                }
            }

            using (var reader = new StreamReader(Path.Combine(m_directory, "referencekinds.txt")))
            {
                CodexRefKind refKind;
                while ((refKind = CodexRefKind.Read(reader)) != null)
                {
                    ReferenceKinds.Add(refKind);
                }
            }

            ReadLinesFromFile("symbols.txt", line =>
            {
                var symbol = CodexSymbol.Read(line);
                Symbols.Add(symbol);
            });

            ReadLinesFromFile("projects.txt", line =>
            {
                var project = CodexProject.ReadEntry(line);
                project.Store = this;
                Projects.Add(project);
            });

            ReadLinesFromFile("files.txt", line =>
            {
                var file = CodexFile.ReadEntry(line);
                file.Store = this;
                Files.Add(file);
            });
        }


        private void ReadLinesFromFile(string filename, Action<string> processLine)
        {
            using (var reader = new StreamReader(Path.Combine(m_directory, filename)))
            {
                string line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.StartsWith("#"))
                    {
                        continue;
                    }

                    processLine(line);
                }
            }
        }

        public CodexFile LoadFile(string path)
        {
            int id;
            if (!Files.Map.TryGetValue(path, out id))
            {
                return null;
            }

            var file = Files.List[id];

            Load(file);

            return file;
        }

        public void Load(CodexFile file)
        {
            using (var reader = new StreamReader(Path.Combine(m_filesDirectory,
                    file.AnnotationFileName)))
            {
                file.Read(reader);
            }
        }

        public void SaveFile(CodexFile file)
        {
            file.Spans.Sort((cs1, cs2) => cs1.Start.CompareTo(cs2.Start));
            file.AnnotationFileName = Path.GetFileName(file.Path) + file.Path.ToLowerInvariant().GetHashCode() + ".txt";
            Directory.CreateDirectory(m_filesDirectory);

            using (var writer = new StreamWriter(Path.Combine(m_filesDirectory, file.AnnotationFileName)))
            {
                file.Write(writer);
            }
        }

        public CodexId<CodexRefKind> GetWellKnownId(WellKnownReferenceKinds kind)
        {
            return m_wellKnownReferenceKinds[(int)kind];
        }
    }

    public class ListMap<TValue> where TValue : IIdentifiable
    {
        public List<TValue> List = new List<TValue>();
        public Dictionary<string, int> Map = new Dictionary<string, int>();

        public CodexId<TValue> Add(TValue value)
        {
            lock (Map)
            {
                var key = value.Identity;
                int id;
                if (!Map.TryGetValue(key, out id))
                {
                    id = List.Count;
                    Map.Add(key, id);
                    List.Add(value);
                }

                return new CodexId<TValue>() { Id = id };
            }
        }

        public TValue this[CodexId<TValue> id] => Get(id);

        public TValue Get(CodexId<TValue> id)
        {
            return Get(id.Id);
        }

        public TValue Get(int id)
        {
            return List[id];
        }
    }

    public interface IIdentifiable
    {
        string Identity { get; }
    }

    public class CodexProject : IIdentifiable
    {
        public CodexSemanticStore Store;
        public string Name;
        public string Directory;
        public string PrimaryFile;

        string IIdentifiable.Identity => Name;

        public static string Columns => "#Columns={Name}|{Directory}|{PrimaryFile}";

        public void WriteEntry(TextWriter writer)
        {
            writer.WriteLine($"{Name}|{Directory}|{PrimaryFile}");
        }

        public static CodexProject ReadEntry(string line)
        {
            string[] parts = line.Split('|');

            return new CodexProject()
            {
                Name = parts[0],
                Directory = parts[1],
                PrimaryFile = parts[2],
            };
        }
    }

    public class CodexFile : IIdentifiable
    {
        public CodexSemanticStore Store;
        public string Path;
        public int Length;
        public string Hash;
        public string AnnotationFileName;
        public List<CodexSpan> Spans = new List<CodexSpan>();
        public CodexId<CodexProject> Project;

        string IIdentifiable.Identity => Path.ToLowerInvariant();

        public static string Columns = "#{Path}|{Length}|{AnnotationFileName}|{Hash}|{Project}";

        public void WriteEntry(TextWriter writer)
        {
            writer.WriteLine($"{Path}|{Length}|{AnnotationFileName}|{Hash}|{Project}");
        }

        public void Write(TextWriter writer)
        {
            writer.WriteLine($"#Path={Path}");
            writer.WriteLine($"#Length={Length}");
            writer.WriteLine($"#Hash={Hash}");
            writer.WriteLine(CodexSpan.Columns);
            foreach (var span in Spans)
            {
                // Check if span fits in file;
                Contract.Assert(span.Start + span.Length < Length, "Span would fall outside of the file.");
                Contract.Assert(span.Length > 0, "Can't have empty spans...");

                span.Write(writer);
            }
        }

        public void Load()
        {
            Store.Load(this);
        }

        public static CodexFile ReadEntry(string line)
        {
            string[] parts = line.Split('|');

            var file = new CodexFile()
            {
                Path = parts[0],
                Length = int.Parse(parts[1]),
                AnnotationFileName = parts[2],
                Hash = parts[3]
            };

            file.Project.Read(parts[4]);
            return file;
        }

        public void Read(StreamReader reader)
        {
            string line = null;
            while ((line = reader.ReadLine()) != null)
            {
                if (line.StartsWith("#"))
                {
                    continue;
                }

                var span = CodexSpan.Read(line);
                Debug.Assert(span.Length > 0, "Must have some length..");
                Debug.Assert(span.Start + span.Length < Length, "Span must fit in file...");

                Spans.Add(span);
            }
        }
    }

    public class CodexSymbol : IIdentifiable
    {
        public string UniqueId;
        public string ShortName;
        public string ContainerQualifiedName;
        public string DisplayName;
        public string Kind;
        public string Attributes;
        public int SymbolDepth;
        public CodexId<CodexProject> Project;

        public object ExtensionData;

        string IIdentifiable.Identity => UniqueId;

        public static string Columns => "#Columns={UniqueId},{ShortName},{ContainerQualifiedName},{DisplayName},{Kind},{Attributes},{SymbolDepth},{Project}";

        public void Write(TextWriter writer)
        {
            writer.WriteLine($"{UniqueId},{ShortName},{ContainerQualifiedName},{DisplayName},{Kind},{Attributes},{SymbolDepth},{Project}");
        }

        public static CodexSymbol Read(string line)
        {
            string[] parts = line.Split(',');

            var symbol = new CodexSymbol()
            {
                UniqueId = parts[0],
                ShortName = parts[1],
                ContainerQualifiedName = parts[2],
                DisplayName = parts[3],
                Kind = parts[4],
                Attributes = parts[5],
                SymbolDepth = int.Parse(parts[6]),
            };

            symbol.Project.Read(parts[7]);

            return symbol;
        }
    }

    public abstract class KindBase : IIdentifiable
    {
        public string Name;

        string IIdentifiable.Identity => Name;

        public void Write(TextWriter writer)
        {
            writer.WriteLine(Name);
        }

        public static T Read<T>(TextReader reader, T instance)
            where T : KindBase
        {
            var name = reader.ReadLine();
            if (string.IsNullOrEmpty(name))
            {
                return null;
            }

            instance.Name = name;
            return instance;
        }
    }


    public class CodexClassification : KindBase
    {
        public static CodexClassification Read(TextReader reader)
        {
            return KindBase.Read(reader, new CodexClassification());
        }
    }

    public class CodexRefKind : KindBase
    {
        public static CodexRefKind Read(TextReader reader)
        {
            return KindBase.Read(reader, new CodexRefKind());
        }
    }

    public struct CodexId<T>
    {
        public static readonly CodexId<T> Invalid = new CodexId<T>() { Id = -1 };

        private int m_id;
        public int Id
        {
            get
            {
                return m_id - 1;
            }
            set
            {
                m_id = value + 1;
            }
        }

        public bool IsValid => m_id > 0;

        public override string ToString()
        {
            if (!IsValid)
            {
                return string.Empty;
            }

            return Id.ToString();
        }

        public void Read(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                Id = -1;
            }
            else
            {
                Id = int.Parse(value);
            }
        }

        public static implicit operator CodexId<T>(int? id)
        {
            if (id == null)
            {
                return Invalid;
            }

            return new CodexId<T>() { Id = id.Value };
        }
    }

    public class CodexSpan
    {
        public int Start;
        public int Length;

        public CodexId<object> LocalId;
        public CodexId<CodexClassification> Classification;
        public CodexId<CodexSymbol> Symbol;
        public CodexId<CodexRefKind> ReferenceKind;

        public static string Columns = "#{Start},{Length},{Classification},{Symbol},{ReferenceKind},{LocalId}";

        public void Write(TextWriter writer)
        {
            writer.WriteLine($"{Start},{Length},{Classification},{Symbol},{ReferenceKind},{LocalId}");
        }

        public static CodexSpan Read(string line)
        {
            string[] parts = line.Split(',');

            var span = new CodexSpan()
            {
                Start = int.Parse(parts[0]),
                Length = int.Parse(parts[1]),
            };

            span.Classification.Read(parts[2]);
            span.Symbol.Read(parts[3]);
            span.ReferenceKind.Read(parts[4]);
            span.LocalId.Read(parts[5]);

            return span;
        }
    }

    /// <summary>
    /// Defines standard set of reference kinds
    /// </summary>
    public enum WellKnownReferenceKinds
    {
        Definition,

        /// <summary>
        /// This represents a constructor declaration for the given type. This is different than
        /// instantiation which actually represents a call to the constructor
        /// </summary>
        Constructor,

        /// <summary>
        /// A call to the constructor of the type referenced by the symbol. This is different than
        /// constructor which is the actual declaration for a constructor for the type symbol.
        /// </summary>
        Instantiation,

        DerivedType,
        InterfaceInheritance,
        InterfaceImplementation,
        Override,
        InterfaceMemberImplementation,

        Write,
        Read,
        GuidUsage,

        ProjectLevelReference,

        /// <summary>
        /// Catch-all reference comes after more specific reference kinds
        /// </summary>
        Reference,
    }
}

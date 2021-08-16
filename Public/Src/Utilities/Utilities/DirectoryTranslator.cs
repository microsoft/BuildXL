// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using static BuildXL.Utilities.FormattableStringEx;

namespace BuildXL.Utilities
{
    /// <summary>
    /// Class for directory translations.
    /// </summary>
    public sealed class DirectoryTranslator
    {
        private const string Prefix = "\\??\\";
        private const string NtPrefix = "\\\\?\\";
        private readonly List<Translation> m_translations = new List<Translation>();
        private static readonly char s_directorySeparatorChar = PathFormatter.GetPathSeparator(PathFormat.HostOs);

        /// <summary>
        /// The name of the environment variable holding the directory translation values passed to BuildXL from the command line.
        /// This is primarily used to inject directory translation tuples into our test processes in the CloudBuild CI scenario.
        /// </summary>
        internal const string TranslatedDirectoriesEnvironmentVariable = "BUILDXL_TRANSLATED_DIRECTORIES";

        /// <summary>
        /// Translations.
        /// </summary>
        public IEnumerable<Translation> Translations => m_translations;

        /// <summary>
        /// Number of translations.
        /// </summary>
        public int Count => m_translations.Count;

        /// <summary>
        /// Whether this translator is sealed
        /// </summary>
        public bool Sealed { get; private set; }

        /// <summary>
        /// Seals the translator.
        /// </summary>
        public void Seal()
        {
            // Sort so that longest paths appear first.
            // This is needed so that more specific mappings take precedence.
            m_translations.Sort((t1, t2) => -t1.SourcePath.Length.CompareTo(t2.SourcePath.Length));
            Sealed = true;
        }

        /// <summary>
        /// Adds a root translation
        /// </summary>
        public void AddTranslation(string sourcePath, string targetPath)
        {
            Contract.RequiresNotNullOrWhiteSpace(sourcePath);
            Contract.RequiresNotNullOrWhiteSpace(targetPath);
            Contract.Assert(!Sealed);

            m_translations.Add(new Translation(
                sourcePath: EnsureDirectoryPath(sourcePath),
                targetPath: EnsureDirectoryPath(targetPath)));
        }

        /// <summary>
        /// Adds a sequence of directory translations.
        /// </summary>
        public void AddTranslations(IEnumerable<(string sourcePath, string targetPath)> translations)
        {
            foreach (var translation in translations)
            {
                AddTranslation(translation.sourcePath, translation.targetPath);
            }
        }

        /// <summary>
        /// Adds a directory translation
        /// </summary>
        public void AddTranslation(AbsolutePath sourcePath, AbsolutePath targetPath, PathTable pathTable)
        {
            Contract.Requires(sourcePath.IsValid);
            Contract.Requires(targetPath.IsValid);
            Contract.RequiresNotNull(pathTable);

            AddTranslation(sourcePath.ToString(pathTable), targetPath.ToString(pathTable));
        }

        /// <summary>
        /// Adds a sequence of directory translations.
        /// </summary>
        public void AddTranslations(IEnumerable<(AbsolutePath sourcePath, AbsolutePath targetPath)> translations, PathTable pathTable)
        {
            // ReSharper disable once PossibleMultipleEnumeration
            Contract.RequiresForAll(translations, t => t.sourcePath.IsValid && t.targetPath.IsValid);
            Contract.RequiresNotNull(pathTable);

            // ReSharper disable once PossibleMultipleEnumeration
            foreach (var translation in translations)
            {
                AddTranslation(translation.sourcePath, translation.targetPath, pathTable);
            }
        }

        /// <summary>
        /// Adds a sequence of directory translations.
        /// </summary>
        public void AddTranslations(RawInputTranslation inputTranslation, PathTable pathTable)
        {
            AddTranslation(inputTranslation.SourcePath, inputTranslation.TargetPath, pathTable);
        }

        /// <summary>
        /// Adds a sequence of directory translations.
        /// </summary>
        public void AddTranslations(IEnumerable<RawInputTranslation> translations, PathTable pathTable)
        {
            // ReSharper disable once PossibleMultipleEnumeration
            Contract.RequiresForAll(translations, t => t.SourcePath.IsValid && t.TargetPath.IsValid);
            Contract.RequiresNotNull(pathTable);

            // ReSharper disable once PossibleMultipleEnumeration
            foreach (var translation in translations)
            {
                AddTranslation(translation.SourcePath, translation.TargetPath, pathTable);
            }
        }

        /// <summary>
        /// Translates the path based on the added translations.
        /// </summary>
        public string Translate(string path)
        {
            Contract.Assert(Sealed);
            string originalPath = path;

            if (m_translations.Count == 0)
            {
                return path;
            }

            if (path.Length == 0)
            {
                return path;
            }

            bool hasPrefix = false;
            bool hasNtPrefix = false;

            if (path.StartsWith(Prefix, OperatingSystemHelper.PathComparison))
            {
                hasPrefix = true;
                path = path.Substring(Prefix.Length);
            }

            if (path.StartsWith(NtPrefix, OperatingSystemHelper.PathComparison))
            {
                hasNtPrefix = true;
                path = path.Substring(NtPrefix.Length);
            }

            if (path.Length == 0)
            {
                return originalPath;
            }

            string priorPath = null;

            while (path != priorPath)
            {
                priorPath = path;
                foreach (var translation in m_translations)
                {
                    if (path.StartsWith(translation.SourcePath, OperatingSystemHelper.PathComparison))
                    {
                        path = translation.TargetPath + path.Substring(translation.SourcePath.Length);
                        break;
                    }

                    if (path[path.Length - 1] != s_directorySeparatorChar &&
                        string.Equals(path + s_directorySeparatorChar, translation.SourcePath, OperatingSystemHelper.PathComparison))
                    {
                        // Path can be a directory path without trailing '\'.
                        path = translation.TargetPath;
                        break;
                    }
                }
            }

            if (hasPrefix)
            {
                path = Prefix + path;
            }

            if (hasNtPrefix)
            {
                path = NtPrefix + path;
            }

            return path;
        }

        /// <summary>
        /// Gets reverse translator.
        /// </summary>
        public DirectoryTranslator GetReverseTranslator()
        {
            var result = new DirectoryTranslator();

            foreach (var translation in m_translations)
            {
                result.AddTranslation(translation.TargetPath, translation.SourcePath);
            }

            result.Seal();
            return result;
        }

        /// <summary>
        /// Gets an unsealed clone of this translator.
        /// </summary>
        public DirectoryTranslator GetUnsealedClone()
        {
            var result = new DirectoryTranslator();

            foreach (var translation in m_translations)
            {
                result.AddTranslation(translation.SourcePath, translation.TargetPath);
            }

            return result;
        }

        /// <summary>
        /// Returns a tuple containing the environment variable name and a properly formatted value which can be used to
        /// augment an execution environment with directory translations, also see <see cref="TranslatedDirectoriesEnvironmentVariable"/>
        /// </summary>
        public static (string variable, string value) GetEnvironmentVaribleRepresentationForTranslations(IReadOnlyList<Translation> translations)
        {
            return (TranslatedDirectoriesEnvironmentVariable, $"{string.Join(";", translations.Select(t => t.SourcePath + "|" + t.TargetPath))}");
        }

        /// <summary>
        /// Add all directory translations found in the environment <see cref="TranslatedDirectoriesEnvironmentVariable"/> to the current set of translations
        /// </summary>
        public void AddDirectoryTranslationFromEnvironment(string injectedEnvironment = null)
        {
            var translatedDirectoriesFromEnvironment = injectedEnvironment ?? Environment.GetEnvironmentVariable(TranslatedDirectoriesEnvironmentVariable);
            if (!Sealed && !string.IsNullOrEmpty(translatedDirectoriesFromEnvironment))
            {
                var directories = translatedDirectoriesFromEnvironment.Split(new string[] { ";" }, StringSplitOptions.None);
                foreach (var entry in directories)
                {
                    var values = entry.Split(new string[] { "|" }, StringSplitOptions.None);
                    if (values.Length == 2)
                    {
                        AddTranslation(values[0], values[1]);
                    }
                }
            }
        }

        /// <summary>
        /// Translates the path based on the added translations.
        /// </summary>
        public AbsolutePath Translate(AbsolutePath path, PathTable pathTable)
        {
            Contract.Requires(path.IsValid);
            Contract.RequiresNotNull(pathTable);
            Contract.Assert(Sealed);

            return AbsolutePath.Create(pathTable, Translate(path.ToString(pathTable)));
        }

        /// <summary>
        /// Validates directory translations.
        /// </summary>
        public static bool ValidateDirectoryTranslation(
            PathTable pathTable,
            IEnumerable<RawInputTranslation> translations,
            out string error)
        {
            // ReSharper disable once PossibleMultipleEnumeration
            Contract.RequiresForAll(translations, t => t != null);
            Contract.RequiresNotNull(pathTable);

            error = null;
            var translationMapping = new Dictionary<AbsolutePath, AbsolutePath>();

            // ReSharper disable once PossibleMultipleEnumeration
            foreach (var translation in translations)
            {
                if (!translation.SourcePath.IsValid || !translation.TargetPath.IsValid)
                {
                    error = I($"invalid path on translation {translation.OptionalInfo ?? "<UNKNOWN>"}");
                    return false;
                }

                if (!translationMapping.ContainsKey(translation.SourcePath))
                {
                    // First translation wins.
                    translationMapping.Add(translation.SourcePath, translation.TargetPath);
                }
            }

            var seenPaths = new HashSet<AbsolutePath>();
            var trace = new Stack<AbsolutePath>();

            foreach (var mapping in translationMapping)
            {
                trace.Clear();
                seenPaths.Clear();

                if (!ValidateNoCycles(pathTable, mapping.Key, translationMapping, seenPaths, trace, out error))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateNoCycles(
            PathTable pathTable,
            AbsolutePath source,
            Dictionary<AbsolutePath, AbsolutePath> translationMapping,
            HashSet<AbsolutePath> seenPaths,
            Stack<AbsolutePath> trace,
            out string error)
        {
            error = null;

            if (seenPaths.Contains(source))
            {
                // We have seen the source. There is a cycle.
                error = I($"cycle in directory translations {string.Join(" < ", GetCycle(trace, source).Select(p => "'" + p.ToString(pathTable) + "'"))}");
                return false;
            }

            seenPaths.Add(source);
            trace.Push(source);

            AbsolutePath target;
            return !translationMapping.TryGetValue(source, out target) || ValidateNoCycles(pathTable, target, translationMapping, seenPaths, trace, out error);
        }

        /// <summary>
        /// Tests raw input translation for junctions.
        /// </summary>
        /// <param name="pathTable">Path table used for testing.</param>
        /// <param name="translations">Raw input translations.</param>
        /// <param name="error">Error message; null if the method returns true.</param>
        /// <returns>True if the junction condition is satisfied.</returns>
        /// <remarks>
        /// This method returns true if the junction condition is satisfied. The junction condition of directory
        /// translation is satisfied if, for any file in the source directory, that file is reachable through the corresponding
        /// target directory. This check is done by writing a file into the source directory, and verifying that the file
        /// can be read from the target directory, and the contents match. Moreover, the file name and the content are randomized using GUID.
        /// </remarks>
        public static bool TestForJunctions(PathTable pathTable, IEnumerable<RawInputTranslation> translations, out string error)
        {
            Contract.RequiresForAll(translations, t => t != null);
            Contract.RequiresNotNull(pathTable);

            error = null;
            var errors = new List<string>();

            foreach (var translation in translations)
            {
                string translationError = null;

                var source = translation.SourcePath.ToString(pathTable);
                var target = translation.TargetPath.ToString(pathTable);

                if (!Directory.Exists(source))
                {
                    translationError = I($"Source directory '{source}' does not exist");
                }
                else if (!Directory.Exists(target))
                {
                    translationError = I($"Target directory '{target}' does not exist");
                }
                else
                {
                    var guidFileName = Guid.NewGuid().ToString();

                    string sourceFile = Path.Combine(source, guidFileName);
                    string targetFile = Path.Combine(target, guidFileName);

                    try
                    {
                        if (File.Exists(sourceFile))
                        {
                            File.Delete(sourceFile);
                        }

                        using (File.Create(sourceFile, 1024, FileOptions.DeleteOnClose))
                        {
                            if (!File.Exists(targetFile))
                            {
                                translationError = I($"Expect target file '{targetFile}' to exist because '{sourceFile}' was created");
                            }
                        }
                    }
                    catch (IOException ioException)
                    {
                        translationError = ioException.Message;
                    }
                }

                if (translationError != null)
                {
                    translationError = $"Translation from '{source}' to '{target}': {translationError}";
                    errors.Add(translationError);
                }
            }

            if (errors.Count > 0)
            {
                error = string.Join(Environment.NewLine, errors);
            }

            return errors.Count == 0;
        }

        private static IEnumerable<AbsolutePath> GetCycle(IEnumerable<AbsolutePath> trace, AbsolutePath path)
        {
            var cycle = new Stack<AbsolutePath>();
            cycle.Push(path);
            foreach (var t in trace)
            {
                cycle.Push(t);
                if (t == path)
                {
                    break;
                }
            }

            return cycle;
        }

        private static string EnsureDirectoryPath(string path)
        {
            if (!path.EndsWith(s_directorySeparatorChar.ToString(), OperatingSystemHelper.PathComparison))
            {
                path += s_directorySeparatorChar.ToString();
            }

            return path;
        }

        /// <summary>
        /// Translation structure.
        /// </summary>
        public sealed class Translation
        {
            /// <summary>
            /// Constructor.
            /// </summary>
            public Translation(string sourcePath, string targetPath)
            {
                Contract.RequiresNotNullOrWhiteSpace(sourcePath);
                Contract.RequiresNotNullOrWhiteSpace(targetPath);

                SourcePath = sourcePath;
                TargetPath = targetPath;
            }

            /// <summary>
            /// Source path.
            /// </summary>
            public string SourcePath { get; }

            /// <summary>
            /// Target path.
            /// </summary>
            public string TargetPath { get; }

            /// <nodoc/>
            public override bool Equals(object obj)
            {
                return (obj is Translation)
                    && ((Translation)obj).SourcePath.Equals(SourcePath)
                    && ((Translation)obj).TargetPath.Equals(TargetPath);
            }

            /// <nodoc/>
            public override int GetHashCode()
            {
                return SourcePath.GetHashCode() ^ TargetPath.GetHashCode();
            }
        }

        /// <summary>
        /// Raw input translation.
        /// </summary>
        public sealed class RawInputTranslation
        {
            /// <summary>
            /// Constructor.
            /// </summary>
            private RawInputTranslation(AbsolutePath sourcePath, AbsolutePath targetPath, string optionalInfo)
            {
                SourcePath = sourcePath;
                TargetPath = targetPath;
                OptionalInfo = optionalInfo;
            }

            /// <summary>
            /// Creates a raw input translation.
            /// </summary>
            public static RawInputTranslation Create(AbsolutePath sourcePath, AbsolutePath targetPath, string optionalInfo = null)
            {
                return new RawInputTranslation(sourcePath, targetPath, optionalInfo);
            }

            /// <summary>
            /// Source path.
            /// </summary>
            public AbsolutePath SourcePath { get; }

            /// <summary>
            /// Target path.
            /// </summary>
            public AbsolutePath TargetPath { get; }

            /// <summary>
            /// Optional info.
            /// </summary>
            public string OptionalInfo { get; }
        }
    }
}

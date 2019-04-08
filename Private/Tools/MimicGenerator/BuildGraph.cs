// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using BuildXL.Utilities.Collections;

namespace Tool.MimicGenerator
{
    /// <summary>
    /// A build graph read in from a json file
    /// </summary>
    public sealed class BuildGraph
    {
        /// <summary>
        /// Directory id to directory
        /// </summary>
        public readonly ConcurrentBigMap<int, Dir> Directories = new ConcurrentBigMap<int, Dir>();

        /// <summary>
        /// File id to file. Do not add directly add files to this collection. Instead, use the <see cref="TryAddFile(int, File)"/> method.
        /// </summary>
        public readonly ConcurrentBigMap<int, File> Files = new ConcurrentBigMap<int, File>();

        /// <summary>
        /// string to file id. This doesn't take rewrites into account, so the FileId returned is arbitrary.
        /// That approximation is generally ok for the sake of duplicating dependencies
        /// </summary>
        public readonly ConcurrentBigMap<string, int> FilesByPath = new ConcurrentBigMap<string, int>();

        /// <summary>
        /// PipId to pip
        /// </summary>
        public readonly ConcurrentBigMap<int, Pip> Pips = new ConcurrentBigMap<int, Pip>();

        /// <summary>
        /// SemiStableHash to PipId
        /// </summary>
        public readonly ConcurrentBigMap<string, Pip> SemiStableHashes = new ConcurrentBigMap<string, Pip>();

        /// <summary>
        /// FileId to the producing pip
        /// </summary>
        public readonly ConcurrentBigMap<int, int> OutputArtifactToProducingPip = new ConcurrentBigMap<int, int>();

        /// <summary>
        /// Observed accesses for process pips
        /// </summary>
        public readonly ConcurrentBigMap<int, ObservedAccess[]> ObservedAccesses = new ConcurrentBigMap<int, ObservedAccess[]>();

        /// <summary>
        /// The statistics for the output files
        /// </summary>
        public readonly Stats OutputFileStats = new Stats("OutputFileSize");

        /// <summary>
        /// The statistics for the output files
        /// </summary>
        public readonly Stats SourceFileStats = new Stats("SourceFileSize");

        /// <summary>
        /// The statistics for the output files
        /// </summary>
        public readonly Stats PipDurationStats = new Stats("PipDuration");

        /// <summary>
        /// The statistics for process pips
        /// </summary>
        public readonly Stats ProcessPipStats = new Stats("ProcessPips");

        /// <summary>
        /// The total time of the build
        /// </summary>
        public Interval BuildInterval = new Interval("BuildInterval");

        /// <summary>
        /// Registers an output file and its producing pip
        /// </summary>
        public void AddOutputArtifact(int outputId, int producerId, bool isDirectory = false)
        {
            if (!OutputArtifactToProducingPip.TryAdd(outputId, producerId))
            {
                throw new MimicGeneratorException("Encountered duplicate output artifact. OutputId:{0}. ProducerId:{1}", outputId, producerId);
            }

            if (isDirectory)
            {
                Dir dir;
                if (Directories.TryGetValue(outputId, out dir))
                {
                    dir.ProducerId = producerId;
                }
            }
            else
            {
                File file;
                if (Files.TryGetValue(outputId, out file))
                {
                    file.IsOutputFile = true;
                }
            }
        }

        public bool TryAddPip(Pip p)
        {
            bool added = Pips.TryAdd(p.PipId, p);
            if (added)
            {
                SemiStableHashes.TryAdd(p.StableId, p);
            }

            m_maxPipId = Math.Max(m_maxPipId, p.PipId);

            return added;
        }

        public int AddWithNewPipId(Pip p, int originalPip)
        {
            p.OriginalPipId = originalPip;
            p.PipId = ++m_maxPipId;
            if (!Pips.TryAdd(p.PipId, p))
            {
                throw new MimicGeneratorException("Failed to add new pip");
            }

            return p.PipId;
        }

        private int m_maxPipId;

        public bool TryAddFile(int id, File file)
        {
            m_maxFile = Math.Max(m_maxFile, id);
            bool result = Files.TryAdd(id, file);
            if (result)
            {
                FilesByPath[file.Location] = id;
            }

            return result;
        }

        public int DuplicateFile(File file, string newRoot)
        {
            string newLocation = DuplicatePath(file.Location, newRoot);
            int existing;
            if (FilesByPath.TryGetValue(newLocation, out existing))
            {
                return existing;
            }
            else
            {
                File newFile = new File(newLocation) { Hash = Guid.NewGuid().ToString(), IsOutputFile = file.IsOutputFile };
                int newFileId = ++m_maxFile;
                Files[newFileId] = newFile;
                FilesByPath.Add(newLocation, newFileId);
                newFile.SetUnscaledLength(file.GetScaledLengthInBytes(1.0));
                return newFileId;
            }
        }

        /// <summary>
        /// Duplicates a path by injecting a new root within the path.
        ///
        /// path: d:\foo\bar.txt
        /// newRoot: 13
        /// result: d:\mim13\foo\bar.txt
        /// </summary>
        public static string DuplicatePath(string path, string newRoot)
        {
            int drive = path.IndexOf(Path.DirectorySeparatorChar);
            int split = drive;
            if (path.StartsWith("mim", StringComparison.OrdinalIgnoreCase))
            {
                split = path.IndexOf(Path.DirectorySeparatorChar, split);
            }

            string newLocation = string.Format(CultureInfo.InvariantCulture, @"{0}\mim{1}{2}",
                path.Substring(0, drive),
                newRoot,
                path.Substring(split, path.Length - split));
            return newLocation;
        }

        private int m_maxFile;
    }

    /// <summary>
    /// File in the build graph
    /// </summary>
    public sealed class File
    {
        private const int DefaultFileLength = 14000;
        private const int UnsetFileLength = -1;

        /// <summary>
        /// Location of the file
        /// </summary>
        public readonly string Location;

        /// <summary>
        /// Indicates if the file is an output file
        /// </summary>
        public bool IsOutputFile;

        public void SetUnscaledLength(int length)
        {
            // Size of -1 is in the json graph when the size could not be computed. Fall back on the default
            // in that case.
            if (length > -1)
            {
                m_lengthInBytes = length;
            }
        }

        /// <summary>
        /// Returns true if the
        /// </summary>
        public bool WasLengthSet
        {
            get
            {
                return m_lengthInBytes != UnsetFileLength;
            }
        }

        /// <summary>
        /// Get the scaled length
        /// </summary>
        public int GetScaledLengthInBytes(double scaleFactor)
        {
            if (WasLengthSet)
            {
                return (int)(m_lengthInBytes * scaleFactor);
            }
            else
            {
                return (int)(DefaultFileLength * scaleFactor);
            }
        }

        private int m_lengthInBytes = UnsetFileLength;

        /// <summary>
        /// Hash of file
        /// </summary>
        public string Hash
        {
            get
            {
                if (m_hash == null)
                {
                    // Hash the filename if no hash was provided
#pragma warning disable CA5351 // Do not use insecure cryptographic algorithm MD5.
                    using (var md5 = MD5.Create())
#pragma warning restore CA5351 // Do not use insecure cryptographic algorithm MD5.
                    {
                        m_hash = string.Join(string.Empty, md5.ComputeHash(Encoding.UTF8.GetBytes(Location)).Select(b => b.ToString("X2", CultureInfo.InvariantCulture)));
                    }
                }

                return m_hash;
            }

            set
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    m_hash = value;
                }
            }
        }

        private string m_hash;

        public File(string path)
        {
            Location = path;
        }
    }

    /// <summary>
    /// Statistics for a set of values
    /// </summary>
    public sealed class Interval
    {
        private long m_min = long.MaxValue;
        private long m_max;
        private string m_displayPrefix;

        /// <summary>
        /// The min of the values
        /// </summary>
        public long Min
        {
            get { return m_min; }
        }

        /// <summary>
        /// The max of the values
        /// </summary>
        public long Max
        {
            get { return m_max; }
        }

        /// <summary>
        /// The total sum of all added values
        /// </summary>
        public long Total
        {
            get { return m_max - m_min; }
        }

        public Interval(string displayPrefix)
        {
            m_displayPrefix = displayPrefix;
        }

        /// <summary>
        /// Adds a value to the interval
        /// </summary>
        public void Add(long value)
        {
            m_min = Math.Min(m_min, value);
            m_max = Math.Max(m_max, value);
        }

        /// <summary>
        /// Writes the stats
        /// </summary>
        public void Write(TextWriter writer)
        {
            writer.WriteLine("{0}.Min={1}", m_displayPrefix, Min);
            writer.WriteLine("{0}.Max={1}", m_displayPrefix, Max);
            writer.WriteLine("{0}.Total={1}", m_displayPrefix, Total);
        }
    }

    /// <summary>
    /// Statistics for a set of values
    /// </summary>
    public sealed class Stats
    {
        private long m_min = long.MaxValue;
        private long m_total;
        private long m_max;
        private long m_count;
        private string m_displayPrefix;

        /// <summary>
        /// The min of the values
        /// </summary>
        public long Min
        {
            get { return m_min; }
        }

        /// <summary>
        /// The max of the values
        /// </summary>
        public long Max
        {
            get { return m_max; }
        }

        /// <summary>
        /// The count of values added
        /// </summary>
        public long Count
        {
            get { return m_count; }
        }

        /// <summary>
        /// The total sum of all added values
        /// </summary>
        public long Total
        {
            get { return m_total; }
        }

        /// <summary>
        /// The total sum of all added values
        /// </summary>
        public long Average
        {
            get
            {
                if (m_count == 0)
                {
                    return 0;
                }

                return m_total / m_count;
            }
        }

        public Stats(string displayPrefix)
        {
            m_displayPrefix = displayPrefix;
        }

        /// <summary>
        /// Adds a value to the stats
        /// </summary>
        public void Add(long value)
        {
            m_min = Math.Min(m_min, value);
            m_max = Math.Max(m_max, value);
            m_count++;
            m_total += value;
        }

        /// <summary>
        /// Writes the stats
        /// </summary>
        public void Write(TextWriter writer)
        {
            writer.WriteLine("{0}.Min={1}", m_displayPrefix, Min);
            writer.WriteLine("{0}.Max={1}", m_displayPrefix, Max);
            writer.WriteLine("{0}.Count={1}", m_displayPrefix, Count);
            writer.WriteLine("{0}.Total={1}", m_displayPrefix, Total);
            writer.WriteLine("{0}.Average={1}", m_displayPrefix, Average);
        }
    }

    /// <summary>
    /// Directory in the build graph
    /// </summary>
    public sealed class Dir
    {
        /// <summary>
        /// Location of the directory
        /// </summary>
        public readonly string Location;

        /// <summary>
        /// Files in the directory
        /// </summary>
        public readonly List<int> Contents;

        /// <summary>
        /// The id of corresponding output pip: 0 if there is none
        /// </summary>
        public int ProducerId;

        public Dir(string path, List<int> contents)
        {
            Location = path;
            Contents = contents;
        }
    }
}

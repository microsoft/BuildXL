// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using BuildXL.Scheduler.Graph;
using BuildXL.Utilities;

namespace BuildXL.Execution.Analyzer
{
    public class SimulateChangesOverTime
    {
        public sealed class SimulatedBuildParams
        {
            public int NumberOfChangeLists { get; set; }

            public int NumberCores { get; set; }

            public double PercentOverhead { get; set; }

            public double PercentCriticalPathOverhead { get; set; }

            public double FixedOverheadMillis { get; set; }

            public string ChangeListsInBuild { get; set; }

            public DateTime DateRun { get; set; }
        }

        public sealed class SimulatedBuildStats
        {
            public SimulatedBuildParams BuildParams { get; set; }

            public double CriticalPathMillis { get; set; }

            public double CpuTimeMillis { get; set; }

            public double CpuTimePerCore { get; set; }

            public double BuildTimeMillis { get; set; }

            public string LimitingFactor { get; set; }

            public double CoresRequired { get; set; }

            public double CacheHitRate { get; set; }

            public AbsolutePath HighestImpactingFile { get; set; }

            public AbsolutePath HighestImpactingCpuFile { get; set; }

            public AbsolutePath HighestImpactingCriticalPathFile { get; set; }

            public double HighestImpactingCpuFileTime { get; set; }

            public string HighestImpactingCpuPackage { get; set; }

            public double HighestImpactingCpuPackageTime { get; set; }

            public string HighestImpactingCriticalPathPackage { get; set; }

            public bool PackageWasHighestImpactingCriticalPath { get; set; }
        }

        public static void WriteSimulatedBuilds(StreamWriter writer, IEnumerable<SimulatedBuildStats> buildStats, Func<AbsolutePath, string> getFileName)
        {
            writer.WriteLine(
                string.Join(",",
                    new string[] { "Limiting_Factor",
                    "#_Changelists",
                    "#_of_cores",
                    "%_overhead",
                    "fixed_overhead_mins",
                    "crit_path_mins",
                    "cpu_time_per_core_mins",
                    "cpu_time_mins",
                    "build_time_mins",
                    "Changelists",
                    "Start_Time",
                    "Cores_Required",
                    "Machines_Required",
                    "Cpu_Impactful_File",
                    "Cpu_Impactful_File_Time",
                    "Crit_Impactful_File",
                    "Most_Impactful_File",
                    "Cache_Hit_Rate",
                    "Highest_Impacting_Cpu_Package",
                    "Highest_Impacting_Cpu_Package_Time",
                    "Highest_Impacting_Critical_Path_Package",
                    "Was_Pkg_Highest_Critical_Path"}));
            foreach (var simulatedTime in buildStats)
            {
                writer.WriteLine(
                    string.Join(",",
                    new string[] {
                        simulatedTime.LimitingFactor,
                        simulatedTime.BuildParams.NumberOfChangeLists.ToString(),
                        simulatedTime.BuildParams.NumberCores.ToString(),
                        simulatedTime.BuildParams.PercentOverhead.ToString(),
                        (simulatedTime.BuildParams.FixedOverheadMillis /1000/60).ToString(),
                        (simulatedTime.CriticalPathMillis / 1000 / 60).ToString(),
                        (simulatedTime.CpuTimePerCore / 1000 / 60).ToString(),
                        (simulatedTime.CpuTimeMillis / 1000 / 60).ToString(),
                        (simulatedTime.BuildTimeMillis / 1000 / 60).ToString(),
                        simulatedTime.BuildParams.ChangeListsInBuild.ToString(),
                        simulatedTime.BuildParams.DateRun.ToString(),
                        simulatedTime.CoresRequired.ToString(),
                        Math.Round(simulatedTime.CoresRequired/80 + 1.5).ToString(),
                        getFileName(simulatedTime.HighestImpactingCpuFile),
                        (simulatedTime.HighestImpactingCpuFileTime/1000/60).ToString(),
                        getFileName(simulatedTime.HighestImpactingCriticalPathFile),
                        getFileName(simulatedTime.HighestImpactingFile),
                        (simulatedTime.CacheHitRate*100).ToString(),
                        simulatedTime.HighestImpactingCpuPackage,
                        (simulatedTime.HighestImpactingCpuPackageTime/1000/60).ToString(),
                        simulatedTime.HighestImpactingCriticalPathPackage,
                        simulatedTime.PackageWasHighestImpactingCriticalPath.ToString()
                    }));
            }
        }

        public static List<SimulatedBuildStats> SimulateBuilds(
            int nodeCount,
            Func<NodeId, TimeSpan> getElapedTime,
            IDictionary<AbsolutePath, HashSet<NodeId>> pathToDownstreamPips,
            IDictionary<string, HashSet<NodeId>> packageToDownstreamPips,
            IDictionary<AbsolutePath, Tuple<TimeSpan, NodeId>> pathToCriticalPath,
            IDictionary<AbsolutePath, double> pathToCputTime,
            IDictionary<string, TimeSpan> packageToCriticalPath,
            IDictionary<string, double> packageToImpactingFileTime,
            List<Tuple<DateTime, long, List<AbsolutePath>, List<string>>> changeLists,
            int numberOfCores,
            double percentOverhead,
            double percentCriticalPathOverhead,
            double fixedOverheadMillis)
        {
            List<AbsolutePath> filesChangedInCurrentBuild = new List<AbsolutePath>();
            HashSet<string> packagesChangedInCurrentBuild = new HashSet<string>();

            DateTime nextReadyTime = changeLists.First().Item1;
            List<SimulatedBuildStats> buildTimes = new List<SimulatedBuildStats>();
            int i = 0;
            int numberChangelists = 0;
            string clsInBuild = string.Empty;
            foreach (var changeList in changeLists)
            {
                clsInBuild += changeList.Item2 + "_";
                numberChangelists++;
                filesChangedInCurrentBuild.AddRange(changeList.Item3);
                foreach (var package in changeList.Item4)
                {
                    if (packageToDownstreamPips.ContainsKey(package) && packageToCriticalPath.ContainsKey(package) && packageToImpactingFileTime.ContainsKey(package))
                    {
                        packagesChangedInCurrentBuild.Add(package);
                    }
                }

                if (nextReadyTime <= changeList.Item1 && filesChangedInCurrentBuild.Any())
                {
                    SimulatedBuildParams buildParams = new SimulatedBuildParams() { FixedOverheadMillis = fixedOverheadMillis, PercentOverhead = percentOverhead, PercentCriticalPathOverhead = percentCriticalPathOverhead, NumberCores = numberOfCores, NumberOfChangeLists = numberChangelists, ChangeListsInBuild = clsInBuild, DateRun = nextReadyTime };
                    SimulatedBuildStats btstats = GetSimulatedBuildTime(
                        nodeCount,
                        getElapedTime,
                        pathToDownstreamPips,
                        packageToDownstreamPips,
                        pathToCriticalPath,
                        pathToCputTime,
                        packageToCriticalPath,
                        packageToImpactingFileTime,
                        filesChangedInCurrentBuild,
                        packagesChangedInCurrentBuild,
                        buildParams);
                    Console.WriteLine("Simulated: " + i + " of " + changeLists.Count + " changelists");
                    buildTimes.Add(btstats);
                    numberChangelists = 0;
                    nextReadyTime += TimeSpan.FromMilliseconds(btstats.BuildTimeMillis);
                    filesChangedInCurrentBuild.Clear();
                    packagesChangedInCurrentBuild = new HashSet<string>();
                    clsInBuild = string.Empty;
                }

                i++;
            }

            if (filesChangedInCurrentBuild.Any())
            {
                SimulatedBuildParams buildParams = new SimulatedBuildParams() { FixedOverheadMillis = fixedOverheadMillis, PercentOverhead = percentOverhead, PercentCriticalPathOverhead = percentCriticalPathOverhead, NumberCores = numberOfCores, NumberOfChangeLists = numberChangelists, ChangeListsInBuild = clsInBuild, DateRun = nextReadyTime };
                SimulatedBuildStats btstats = GetSimulatedBuildTime(
                    nodeCount,
                    getElapedTime,
                    pathToDownstreamPips,
                    packageToDownstreamPips,
                    pathToCriticalPath,
                    pathToCputTime,
                    packageToCriticalPath,
                    packageToImpactingFileTime,
                    filesChangedInCurrentBuild,
                    packagesChangedInCurrentBuild,
                    buildParams);
                buildTimes.Add(btstats);
            }

            return buildTimes;
        }

        private static SimulatedBuildStats GetSimulatedBuildTime(
            int nodeCount,
            Func<NodeId, TimeSpan> getElapedTime,
            IDictionary<AbsolutePath, HashSet<NodeId>> pathToDownstreamPips,
            IDictionary<string, HashSet<NodeId>> packageToDownstreamPips,
            IDictionary<AbsolutePath, Tuple<TimeSpan, NodeId>> pathToCriticalPath,
            IDictionary<AbsolutePath, double> pathToImpactingFileTime,
            IDictionary<string, TimeSpan> packageToCriticalPath,
            IDictionary<string, double> packageToImpactingFileTime,
            List<AbsolutePath> files,
            IEnumerable<string> packages,
            SimulatedBuildParams buildParams)
        {
            double cacheHitRate;
            AbsolutePath highestImpactingCpuTimeFile;
            double maxCpuImpactingFileTime;
            string highestImpactingCpuTimePackage;
            double maxCpuImpactingPackageTime;
            double cpuMillis = GetSimulatedCpuMilliseconds(nodeCount, getElapedTime, pathToDownstreamPips, packageToDownstreamPips, pathToImpactingFileTime, packageToImpactingFileTime, files, packages, out cacheHitRate, out highestImpactingCpuTimeFile, out maxCpuImpactingFileTime, out highestImpactingCpuTimePackage, out maxCpuImpactingPackageTime);
            AbsolutePath highestImpactingCriticalPathFile;
            string highestImpactingCriticalPathPackage;
            bool packageWasHighestImpacting;
            TimeSpan criticalPathTime = GetSimulatedLongestCriticalPath(pathToCriticalPath, packageToCriticalPath, files, packages, out highestImpactingCriticalPathFile, out highestImpactingCriticalPathPackage, out packageWasHighestImpacting);
            double cpuMillisPerCore = cpuMillis / buildParams.NumberCores;
            double adjustedCpuMillis = cpuMillis * (1 + buildParams.PercentOverhead / 100);
            double adjustedCpuMillisPerCore = adjustedCpuMillis / buildParams.NumberCores;
            double adjustedCriticalPathMillis = criticalPathTime.TotalMilliseconds * (1 + buildParams.PercentCriticalPathOverhead / 100);
            double coresRequired = adjustedCpuMillis / adjustedCriticalPathMillis;
            string limitingFactor = adjustedCpuMillisPerCore > adjustedCriticalPathMillis ? "CPU Time" : "Critical Path";
            AbsolutePath highestImpactingFile = adjustedCpuMillisPerCore > adjustedCriticalPathMillis ? highestImpactingCpuTimeFile : highestImpactingCriticalPathFile;
            double buildTime = Math.Max(adjustedCpuMillisPerCore, adjustedCriticalPathMillis) + buildParams.FixedOverheadMillis;
            return new SimulatedBuildStats
            {
                CriticalPathMillis = criticalPathTime.TotalMilliseconds,
                CpuTimeMillis = cpuMillis,
                CpuTimePerCore = cpuMillisPerCore,
                BuildTimeMillis = buildTime,
                BuildParams = buildParams,
                LimitingFactor = limitingFactor,
                CoresRequired = coresRequired,
                CacheHitRate = cacheHitRate,
                HighestImpactingFile = highestImpactingFile,
                HighestImpactingCpuFile = highestImpactingCpuTimeFile,
                HighestImpactingCriticalPathFile = highestImpactingCriticalPathFile,
                HighestImpactingCpuFileTime = maxCpuImpactingFileTime,
                HighestImpactingCpuPackage = highestImpactingCpuTimePackage,
                HighestImpactingCpuPackageTime = maxCpuImpactingPackageTime,
                HighestImpactingCriticalPathPackage = highestImpactingCriticalPathPackage,
                PackageWasHighestImpactingCriticalPath = packageWasHighestImpacting
            };
        }

        private static TimeSpan GetSimulatedLongestCriticalPath(IDictionary<AbsolutePath, Tuple<TimeSpan, NodeId>> pathToCriticalPath, IDictionary<string, TimeSpan> packagesToCriticalPath, List<AbsolutePath> files, IEnumerable<string> packages, out AbsolutePath highestImpactingFile, out string highestImpactingPackage, out bool highestIsPackage)
        {
            TimeSpan criticalPathTime = TimeSpan.Zero;
            highestImpactingFile = files.FirstOrDefault();
            highestIsPackage = false;
            highestImpactingPackage = string.Empty;

            int hit = 0;
            int miss = 0;
            foreach (var file in files)
            {
                if (!pathToCriticalPath.ContainsKey(file))
                {
                    miss++;
                    continue;
                }

                hit++;
                if (pathToCriticalPath[file].Item1 > criticalPathTime)
                {
                    highestImpactingFile = file;
                    criticalPathTime = pathToCriticalPath[file].Item1;
                }
            }

            foreach (var package in packages)
            {
                if (packagesToCriticalPath[package] > criticalPathTime)
                {
                    highestIsPackage = true;
                    highestImpactingPackage = package;
                    criticalPathTime = packagesToCriticalPath[package];
                }
            }

            if (miss > 0)
            {
                Console.WriteLine("Miss > 0.  miss: " + miss + " hit: " + hit);
            }

            return criticalPathTime;
        }

        private static double GetSimulatedCpuMilliseconds(
            int nodeCount,
            Func<NodeId, TimeSpan> getElapedTime,
            IDictionary<AbsolutePath, HashSet<NodeId>> pathToDownstreamPips,
            IDictionary<string, HashSet<NodeId>> packageToDownstreamPips,
            IDictionary<AbsolutePath, double> pathToImpactingMillis,
            IDictionary<string, double> packageToImpactingMillis,
            List<AbsolutePath> files,
            IEnumerable<string> packages,
            out double cacheHitRate,
            out AbsolutePath highestImpactingFile,
            out double maxFileImpactingTime,
            out string highestImpactingPackage,
            out double maxPackageImpactingTime)
        {
            int size = nodeCount / 32 + 1;
            HashSet<NodeId> downstreamPips = new HashSet<NodeId>();
            highestImpactingFile = files.FirstOrDefault();
            maxFileImpactingTime = -1;
            maxPackageImpactingTime = -1;
            highestImpactingPackage = string.Empty;
            int miss = 0;
            int hit = 0;
            foreach (var file in files)
            {
                if (!pathToDownstreamPips.ContainsKey(file))
                {
                    miss++;
                    continue;
                }

                hit++;
                downstreamPips.UnionWith(pathToDownstreamPips[file]);

                if (pathToImpactingMillis[file] > maxFileImpactingTime)
                {
                    maxFileImpactingTime = pathToImpactingMillis[file];
                    highestImpactingFile = file;
                }
            }

            if (miss > 0)
            {
                Console.WriteLine("Files hit: " + hit + " misses: " + miss);
            }

            foreach (var package in packages)
            {
                downstreamPips.UnionWith(packageToDownstreamPips[package]);
                if (packageToImpactingMillis.ContainsKey(package) && packageToImpactingMillis[package] > maxPackageImpactingTime)
                {
                    maxPackageImpactingTime = packageToImpactingMillis[package];
                    highestImpactingPackage = package;
                }
            }

            double pipRunCount = downstreamPips.Count;
            cacheHitRate = 1 - pipRunCount / nodeCount;

            double time = 0;
            foreach (var pip in downstreamPips)
            {
                time += getElapedTime(pip).TotalMilliseconds;
            }

            return time;
        }
    }
}

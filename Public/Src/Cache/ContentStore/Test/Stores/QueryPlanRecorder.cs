// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using System.Text;
using BuildXL.Cache.ContentStore.Logging;
using BuildXL.Cache.ContentStore.Interfaces.FileSystem;
using ContentStoreTest.Test;

namespace ContentStoreTest.Stores
{
    public class QueryPlanRecorder
    {
        private readonly ConcurrentDictionary<string, string> _plans = new ConcurrentDictionary<string, string>();

        public bool HasName(string name)
        {
            Contract.Requires(name != null);
            return _plans.ContainsKey(name);
        }

        public void Add(string name, string command, string plan)
        {
            Contract.Requires(name != null);
            Contract.Requires(command != null);
            Contract.Requires(plan != null);

            var sb = new StringBuilder();
            sb.Append(name);
            sb.Append(": ");
            sb.AppendLine(command);
            sb.Append(plan);
            _plans[name] = sb.ToString();
        }

        private string Snapshot()
        {
            KeyValuePair<string, string>[] planArray = _plans.ToArray();

            var sb = new StringBuilder();
            foreach (var text in planArray.OrderBy(kvp => kvp.Key).Select(kvp => kvp.Value))
            {
                if (sb.Length > 0)
                {
                    sb.AppendLine();
                }

                sb.Append(text);
            }

            return sb.ToString();
        }

        public void LogToFile(string fileNameSegment)
        {
            Contract.Requires(fileNameSegment != null);

            var logger = TestGlobal.Logger as Logger;

            var fileLog = logger?.GetLog<FileLog>().FirstOrDefault();
            if (fileLog == null)
            {
                return;
            }

            var logPath = new AbsolutePath(fileLog.FilePath);
            var fileName = Path.GetFileNameWithoutExtension(logPath.FileName) + "-" + fileNameSegment + ".log";
            var planLogPath = logPath.Parent / fileName;

            string text = Snapshot();

            File.WriteAllText(planLogPath.Path, text, Encoding.UTF8);
        }
    }
}

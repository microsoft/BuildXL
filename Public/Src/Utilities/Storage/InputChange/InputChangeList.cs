// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.Diagnostics.ContractsLight;
using System.IO;
using System.Linq;
using BuildXL.Storage.ChangeTracking;
using BuildXL.Storage.Tracing;
using BuildXL.Utilities;
using BuildXL.Utilities.Instrumentation.Common;

namespace BuildXL.Storage.InputChange
{
    /// <summary>
    /// Class representing input change list.
    /// </summary>
    public sealed class InputChangeList : IObservable<ChangedPathInfo>
    {
        private readonly List<IObserver<ChangedPathInfo>> m_observers = new List<IObserver<ChangedPathInfo>>(2);
        private readonly List<ChangedPathInfo> m_changedPaths = new List<ChangedPathInfo>();
        private static char[] s_inputSeparator = new[] { '|' };

        /// <summary>
        /// Gets the list of input changes.
        /// </summary>
        public IEnumerable<ChangedPathInfo> ChangedPaths => m_changedPaths;

        private InputChangeList()
        {
        }

        /// <summary>
        /// Creates and instance of <see cref="InputChangeList"/> from file.
        /// </summary>
        public static InputChangeList CreateFromFile(
            LoggingContext loggingContext, 
            string path, 
            string sourceRoot = null, 
            DirectoryTranslator directoryTranslator = null)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(!string.IsNullOrEmpty(path));

            if (!File.Exists(path))
            {
                Logger.Log.InputChangeListFileNotFound(loggingContext, path);
                return null;
            }

            try
            {
                using (StreamReader reader = new StreamReader(path))
                {
                    return CreateFromStream(loggingContext, reader, path, directoryTranslator: directoryTranslator);
                }
            }
            catch (IOException ioException)
            {
                Logger.Log.ExceptionOnCreatingInputChangeList(loggingContext, path, ioException.ToString());
                return null;
            }
        }

        /// <summary>
        /// Creates an instance of <see cref="InputChangeList"/> from a stream reader.
        /// </summary>
        public static InputChangeList CreateFromStream(
            LoggingContext loggingContext, 
            TextReader reader, 
            string filePathOrigin = null,
            string sourceRoot = null,
            DirectoryTranslator directoryTranslator = null)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(reader != null);

            InputChangeList inputChangeList = new InputChangeList();

            try
            {   
                int lineNo = 0;
                string inputLine = null;

                while ((inputLine = reader.ReadLine()) != null)  
                {
                    ++lineNo;

                    if (string.IsNullOrEmpty(inputLine))
                    {
                        continue;
                    }

                    if (!TryParseInput(
                        loggingContext, 
                        inputLine, 
                        filePathOrigin ?? string.Empty, 
                        lineNo, 
                        out var changePathInfo,
                        sourceRoot,
                        directoryTranslator))
                    {
                        return null;
                    }

                    inputChangeList.m_changedPaths.Add(changePathInfo);
                }
            }
            catch (IOException ioException)
            {
                Logger.Log.ExceptionOnCreatingInputChangeList(loggingContext, filePathOrigin ?? string.Empty, ioException.ToString());
                return null;
            }

            return inputChangeList;
        }

        /// <summary>
        /// Tries to parse input line.
        /// </summary>
        /// <remarks>
        /// Input line must be of the form:
        ///   full path
        /// or
        ///   full path|comma separated <see cref="PathChanges"/>
        /// The former assumes that changes are <see cref="PathChanges.DataOrMetadataChanged"/>.
        /// </remarks>
        private static bool TryParseInput(
            LoggingContext loggingContext, 
            string input, 
            string filePath, 
            int lineNo,
            out ChangedPathInfo changedPathInfo,
            string sourceRoot = null,
            DirectoryTranslator directoryTranslator = null)
        {
            Contract.Requires(loggingContext != null);
            Contract.Requires(!string.IsNullOrEmpty(input));

            changedPathInfo = default;
            string[] splitInput = input.Split(s_inputSeparator);

            if (splitInput.Length > 2)
            {
                Logger.Log.InvalidFormatOfInputChange(loggingContext, input, filePath, lineNo);
                return false;
            }

            string changedPath = splitInput[0].Trim();
            string changesStr = null;

            if (splitInput.Length == 2)
            {
                changesStr = splitInput[1].Trim();
            }

            // Assume data or metadata change if unspecified.
            PathChanges changes = PathChanges.DataOrMetadataChanged;

            try
            {
                if (!Path.IsPathRooted(changedPath))
                {
                    if (string.IsNullOrEmpty(sourceRoot))
                    {
                        Logger.Log.InvalidChangedPathOfInputChange(loggingContext, changedPath, filePath, lineNo);
                        return false;
                    }

                    changedPath = Path.GetFullPath(Path.Combine(sourceRoot, changedPath));
                }

                if (directoryTranslator != null)
                {
                    changedPath = directoryTranslator.Translate(changedPath);
                }

                if (!string.IsNullOrEmpty(changesStr))
                {
                    if (!Enum.TryParse(changesStr, true, out changes))
                    {
                        string validKinds = string.Join(", ", ((PathChanges[])Enum.GetValues(typeof(PathChanges))).Select(c => c.ToString()));
                        Logger.Log.InvalidChangeKindsOfInputChange(loggingContext, changesStr, filePath, lineNo, validKinds);
                        return false;
                    }
                }
            }
            catch (ArgumentException argumentException)
            {
                Logger.Log.InvalidInputChange(loggingContext, input, filePath, lineNo, argumentException.ToString());
                return false;
            }

            changedPathInfo = new ChangedPathInfo(changedPath, changes);
            return true;
        }
        
        /// <inheritdoc />
        public IDisposable Subscribe(IObserver<ChangedPathInfo> observer)
        {
            Contract.Requires(observer != null);

            if (!m_observers.Contains(observer))
            {
                m_observers.Add(observer);
            }

            return new InputChangeListUnsubscriber(m_observers, observer);
        }

        /// <summary>
        /// Process input change list.
        /// </summary>
        public void ProcessChanges()
        {
            foreach (var changedPath in m_changedPaths)
            {
                ReportChanges(changedPath);
            }

            CompleteChanges();
        }

        private void ReportChanges(ChangedPathInfo changedPathInfo)
        {
            foreach (var observer in m_observers)
            {
                observer.OnNext(changedPathInfo);
            }
        }

        private void CompleteChanges()
        {
            foreach (var observer in m_observers)
            {
                observer.OnCompleted();
            }
        }
    }
}

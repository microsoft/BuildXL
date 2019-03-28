// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using System.Threading;
using BuildXL.Cache.ImplementationSupport;

namespace BuildXL.Cache.VerticalAggregator
{
    internal sealed class SessionCounters : BaseCounters
    {
        // While private, these field names are of a style that
        // is public as the name will be used for the actual stats
        // dictionary generation.  This way the counts are
        // handled in a uniform manner and extended consistently.
        #region AddOrGet
        private TimedCounter m_addOrGet_FingerprintsAddedLocalOnly = new TimedCounter();
        private TimedCounter m_addOrGet_FingerprintsAddedRemote = new TimedCounter();
        private TimedCounter m_addOrGet_DeterminismRecovered = new TimedCounter();
        private TimedCounter m_addOrGet_Failure = new TimedCounter();

        public struct AddOrGetCountCollector : IDisposable
        {
            private SessionCounters m_sessionCounters;
            private ElapsedTimer m_elapsed;

            private bool m_uploadDisregarded;
            private TimedCounter m_finalCounter;

            private CasCopyStats m_copyStats;

            internal AddOrGetCountCollector(SessionCounters sessionCounters)
            {
                Contract.Requires(sessionCounters != null);

                m_sessionCounters = sessionCounters;
                m_elapsed = ElapsedTimer.StartNew();
                m_uploadDisregarded = false;
                m_finalCounter = null;
                m_copyStats = new CasCopyStats();
            }

            public void AddedRemote()
            {
                m_finalCounter = m_sessionCounters.m_addOrGet_FingerprintsAddedRemote;
            }

            public void AddedLocal()
            {
                m_finalCounter = m_sessionCounters.m_addOrGet_FingerprintsAddedLocalOnly;
            }

            public void DeterminismRecovered()
            {
                m_finalCounter = m_sessionCounters.m_addOrGet_DeterminismRecovered;
            }

            public void Failure()
            {
                m_finalCounter = m_sessionCounters.m_addOrGet_Failure;
            }

            public void UploadDisregarded()
            {
                m_uploadDisregarded = true;
            }

            public CasCopyStats CopyStats { get { return m_copyStats; } }

            public void Dispose()
            {
                if (m_sessionCounters != null)
                {
                    Contract.Assume(m_finalCounter != null);

                    m_finalCounter.Add(m_elapsed);

                    if (CopyStats.TransitOccurred)
                    {
                        if (m_uploadDisregarded)
                        {
                            m_sessionCounters.m_filesTransitedToRemoteDisgarded.Add(CopyStats.FilesTransited, CopyStats.FilesSkipped, CopyStats.FileSizesUnknown, CopyStats.FileTransitsFailed, CopyStats.FilesSizes, m_elapsed);
                        }
                        else
                        {
                            m_sessionCounters.m_filesTransitedToRemote.Add(CopyStats.FilesTransited, CopyStats.FilesSkipped, CopyStats.FileSizesUnknown, CopyStats.FileTransitsFailed, CopyStats.FilesSizes, m_elapsed);
                        }
                    }

                    m_sessionCounters = null;
                }
            }
        }

        public AddOrGetCountCollector AddOrGetCounter()
        {
            return new AddOrGetCountCollector(this);
        }

        #endregion AddOrGet

        #region AddToCas

        public struct AddToCasCountCollector : IDisposable
        {
            private SessionCounters m_sessionCounters;
            private ElapsedTimer m_elapsed;
            private bool m_writeThrough;

            private CasCopyStats m_copyStats;

            internal AddToCasCountCollector(SessionCounters sessionCounters)
            {
                Contract.Requires(sessionCounters != null);

                m_sessionCounters = sessionCounters;
                m_elapsed = ElapsedTimer.StartNew();
                m_writeThrough = false;

                m_copyStats = new CasCopyStats();
            }

            public CasCopyStats CopyStats { get { return m_copyStats; } }

            public void WriteThrough()
            {
                m_writeThrough = true;
            }

            public void Dispose()
            {
                if (m_sessionCounters != null)
                {
                    if (m_writeThrough && CopyStats.TransitOccurred)
                    {
                        m_sessionCounters.m_filesTransitedToRemote.Add(CopyStats.FilesTransited, CopyStats.FilesSkipped, CopyStats.FileSizesUnknown, CopyStats.FileTransitsFailed, CopyStats.FilesSizes, m_elapsed);
                    }

                    m_sessionCounters = null;
                }
            }
        }

        public AddToCasCountCollector AddToCasCounter()
        {
            return new AddToCasCountCollector(this);
        }

        #endregion AddToCas

        #region EnumerateStrongFingerprints

        // EnumerateStrongFingerprints is a yield-return of some
        // number of fingerprints.  This provides both number of
        // calls and the amount returned
        private EnumerateStrongFingerprintsCounter m_enumerateStrongFingerprints = new EnumerateStrongFingerprintsCounter();

        public struct EnumerateStrongFingerprintsCountCollector : IDisposable
        {
            private SessionCounters m_sessionCounters;
            private ElapsedTimer m_elapsed;
            private double m_countLocal;
            private double m_countRemote;
            private long m_countSentintel;

            internal EnumerateStrongFingerprintsCountCollector(SessionCounters sessionCounters)
            {
                Contract.Requires(sessionCounters != null);

                m_sessionCounters = sessionCounters;
                m_elapsed = ElapsedTimer.StartNew();
                m_countLocal = 0;
                m_countRemote = 0;
                m_countSentintel = 0;
            }

            public void YieldReturnLocal()
            {
                m_countLocal++;
            }

            public void YieldReturnRemote()
            {
                m_countRemote++;
            }

            public void YieldReturnSenintel()
            {
                m_countSentintel++;
            }

            public void Dispose()
            {
                if (m_sessionCounters != null)
                {
                    m_sessionCounters.m_enumerateStrongFingerprints.Add(m_countLocal, m_countRemote, m_countSentintel, m_elapsed);
                    m_sessionCounters = null;
                }
            }
        }

        public EnumerateStrongFingerprintsCountCollector EnumerateStrongFingerprintsCounter()
        {
            return new EnumerateStrongFingerprintsCountCollector(this);
        }

        #endregion EnumerateStrongFingerprints

        #region GetStream

        private TimedCounter m_getStream_HitLocal = new TimedCounter();
        private TimedCounter m_getStream_HitRemote = new TimedCounter();
        private TimedCounter m_getStream_Miss = new TimedCounter();
        private TimedCounter m_getStream_Fail = new TimedCounter();

        public struct GetStreamCountCollector : IDisposable
        {
            private SessionCounters m_sessionCounters;
            private ElapsedTimer m_elapsed;
            private TimedCounter m_finalCounter;
            private CasCopyStats m_copyStats;

            internal GetStreamCountCollector(SessionCounters sessionCounters)
            {
                m_sessionCounters = sessionCounters;
                m_elapsed = ElapsedTimer.StartNew();
                m_copyStats = new CasCopyStats();
                m_finalCounter = null;
            }

            public void Miss()
            {
                m_finalCounter = m_sessionCounters.m_getStream_Miss;
            }

            public void Fail()
            {
                m_finalCounter = m_sessionCounters.m_getStream_Fail;
            }

            public void HitRemote()
            {
                m_finalCounter = m_sessionCounters.m_getStream_HitRemote;
            }

            public void HitLocal()
            {
                m_finalCounter = m_sessionCounters.m_getStream_HitLocal;
            }

            public CasCopyStats CopyStats { get { return m_copyStats; } }

            public void Dispose()
            {
                if (m_sessionCounters != null)
                {
                    Contract.Assume(m_finalCounter != null);

                    m_finalCounter.Add(m_elapsed);

                    if (CopyStats.TransitOccurred)
                    {
                        m_sessionCounters.m_filesTransitedToLocal.Add(CopyStats.FilesTransited, CopyStats.FilesSkipped, CopyStats.FileSizesUnknown, CopyStats.FileTransitsFailed, CopyStats.FilesSizes, m_elapsed);
                    }

                    m_sessionCounters = null;
                }
            }
        }

        public GetStreamCountCollector GetStreamCounter()
        {
            return new GetStreamCountCollector(this);
        }

        #endregion GetStream

        #region PinToCas
        private TimedCounter m_pinToCas_Miss = new TimedCounter();
        private TimedCounter m_pinToCas_HitLocal = new TimedCounter();
        private TimedCounter m_pinToCas_HitRemote = new TimedCounter();
        private TimedCounter m_pinToCas_Fail = new TimedCounter();

        public struct PinToCasCountCollector : IDisposable
        {
            private SessionCounters m_sessionCounters;
            private ElapsedTimer m_elapsed;
            private TimedCounter m_theCounter;

            internal PinToCasCountCollector(SessionCounters sessionCounters)
            {
                Contract.Requires(sessionCounters != null);

                m_sessionCounters = sessionCounters;
                m_elapsed = ElapsedTimer.StartNew();
                m_theCounter = null;
            }

            public void PinMiss()
            {
                m_theCounter = m_sessionCounters.m_pinToCas_Miss;
            }

            public void PinHitLocal()
            {
                m_theCounter = m_sessionCounters.m_pinToCas_HitLocal;
            }

            public void PinHitRemote()
            {
                m_theCounter = m_sessionCounters.m_pinToCas_HitRemote;
            }

            public void Fail()
            {
                m_theCounter = m_sessionCounters.m_pinToCas_Fail;
            }

            public void Dispose()
            {
                if (m_sessionCounters != null)
                {
                    m_sessionCounters = null;
                    var timeUsed = m_elapsed;

                    Contract.Assume(m_theCounter != null);

                    m_theCounter.Add(timeUsed);
                }
            }
        }

        public PinToCasCountCollector PinToCasCounter()
        {
            return new PinToCasCountCollector(this);
        }

        #endregion PinToCas

        #region ProduceFile

        // ProduceFile has 3 cases today but one may need to be renamed
        // Today, since pin is needed before get, the "unpinned" case
        // is the same as a "cache miss" so we are calling it a miss.
        private TimedCounter m_produceFile_HitLocal = new TimedCounter();
        private TimedCounter m_produceFile_HitRemote = new TimedCounter();
        private TimedCounter m_produceFile_Miss = new TimedCounter();
        private TimedCounter m_produceFile_Fail = new TimedCounter();

        public struct ProduceFileCountCollector : IDisposable
        {
            private SessionCounters m_sessionCounters;
            private ElapsedTimer m_elapsed;
            private TimedCounter m_finalCounter;
            private CasCopyStats m_copyStats;

            internal ProduceFileCountCollector(SessionCounters sessionCounters)
            {
                Contract.Requires(sessionCounters != null);

                m_sessionCounters = sessionCounters;
                m_elapsed = ElapsedTimer.StartNew();
                m_finalCounter = null;
                m_copyStats = new CasCopyStats();
            }

            public CasCopyStats CopyStats { get { return m_copyStats; } }

            public void Miss()
            {
                m_finalCounter = m_sessionCounters.m_produceFile_Miss;
            }

            public void Fail()
            {
                m_finalCounter = m_sessionCounters.m_produceFile_Fail;
            }

            public void HitLocal()
            {
                m_finalCounter = m_sessionCounters.m_produceFile_HitLocal;
            }

            public void HitRemote()
            {
                m_finalCounter = m_sessionCounters.m_produceFile_HitRemote;
            }

            public void Dispose()
            {
                if (m_sessionCounters != null)
                {
                    Contract.Assume(m_finalCounter != null);

                    m_finalCounter.Add(m_elapsed);

                    if (CopyStats.TransitOccurred)
                    {
                        m_sessionCounters.m_filesTransitedToLocal.Add(CopyStats.FilesTransited, CopyStats.FilesSkipped, CopyStats.FileSizesUnknown, CopyStats.FileTransitsFailed, CopyStats.FilesSizes, m_elapsed);
                    }

                    m_sessionCounters = null;
                }
            }
        }

        public ProduceFileCountCollector ProduceFileCounter()
        {
            return new ProduceFileCountCollector(this);
        }

        #endregion ProduceFile

        #region Transited Files
        private CountedMultiBytesDistribution m_filesTransitedToRemote = new CountedMultiBytesDistribution();
        private CountedMultiBytesDistribution m_filesTransitedToRemoteDisgarded = new CountedMultiBytesDistribution();
        private CountedMultiBytesDistribution m_filesTransitedToLocal = new CountedMultiBytesDistribution();

        /// <summary>
        /// Seperate class to track file movement
        /// </summary>
        /// <remarks>
        /// Since file transmission is done up and down the cache stack, this class represents the movement of one of more files
        /// in either direction so the private methods doing the transit can remain direction agnostic.
        /// </remarks>
        public sealed class CasCopyStats
        {
            private long m_filesTransited;

            public long FilesTransited { get { return m_filesTransited; } }

            private long m_filesSkipped;

            public long FilesSkipped { get { return m_filesSkipped; } }

            private long m_fileSizesTransited;

            public long FilesSizes { get { return m_fileSizesTransited; } }

            private long m_fileSizesUnknown;

            public long FileSizesUnknown { get { return m_fileSizesUnknown; } }

            private long m_fileTransitsFailed;

            public long FileTransitsFailed { get { return m_fileTransitsFailed; } }

            public CasCopyStats()
            {
                m_fileSizesTransited = 0;
                m_filesTransited = 0;
                m_filesSkipped = 0;
                m_fileSizesUnknown = 0;
                m_fileTransitsFailed = 0;
            }

            public void FileTransited(long fileSize)
            {
                Interlocked.Increment(ref m_filesTransited);
                Interlocked.Add(ref m_fileSizesTransited, fileSize);
            }

            public void FileSkipped()
            {
                Interlocked.Increment(ref m_filesSkipped);
            }

            public void FileSizeUnknown()
            {
                Interlocked.Increment(ref m_filesTransited);
                Interlocked.Increment(ref m_fileSizesUnknown);
            }

            public void FileTransitFailed()
            {
                Interlocked.Increment(ref m_fileTransitsFailed);
            }

            public bool TransitOccurred
            {
                get
                {
                    return m_fileSizesUnknown != 0 || m_filesTransited != 0 || m_filesSkipped != 0 || m_fileTransitsFailed != 0;
                }
            }
        }
        #endregion

        #region GetCacheEntry

        // GetCacheEntry has two cases - one hit and one miss
        private TimedCounter m_getCacheEntry_HitLocal = new TimedCounter();
        private TimedCounter m_getCacheEntry_HitRemote = new TimedCounter();
        private TimedCounter m_getCacheEntry_Miss = new TimedCounter();
        private TimedCounter m_getCacheEntry_FingerprintsPromotedRemote = new TimedCounter();
        private TimedCounter m_getCacheEntry_DeterminismRecovered = new TimedCounter();
        private TimedCounter m_getCacheEntry_Failure = new TimedCounter();

        public struct GetCacheEntryCountCollector : IDisposable
        {
            private SessionCounters m_sessionCounters;
            private ElapsedTimer m_elapsed;

            private bool m_uploadDisregarded;
            private CasCopyStats m_copyStats;

            private TimedCounter m_endStateCounter;

            internal GetCacheEntryCountCollector(SessionCounters sessionCounters)
            {
                Contract.Requires(sessionCounters != null);

                m_sessionCounters = sessionCounters;
                m_elapsed = ElapsedTimer.StartNew();

                m_uploadDisregarded = false;
                m_copyStats = new CasCopyStats();

                m_endStateCounter = null;
            }

            public void CacheHitLocal()
            {
                m_endStateCounter = m_sessionCounters.m_getCacheEntry_HitLocal;
            }

            public void CacheHitRemote()
            {
                m_endStateCounter = m_sessionCounters.m_getCacheEntry_HitRemote;
            }

            public void PromotedRemote()
            {
                m_endStateCounter = m_sessionCounters.m_getCacheEntry_FingerprintsPromotedRemote;
            }

            public void DeterminismRecovered()
            {
                m_endStateCounter = m_sessionCounters.m_getCacheEntry_DeterminismRecovered;
            }

            public void Failure()
            {
                m_endStateCounter = m_sessionCounters.m_getCacheEntry_Failure;
            }

            public void UploadDisregarded()
            {
                m_uploadDisregarded = true;
            }

            public CasCopyStats CopyStats { get { return m_copyStats; } }

            public void Dispose()
            {
                if (m_sessionCounters != null)
                {
                    if (m_endStateCounter != null)
                    {
                        m_endStateCounter.Add(m_elapsed);

                        if (CopyStats.TransitOccurred)
                        {
                            if (m_uploadDisregarded)
                            {
                                m_sessionCounters.m_filesTransitedToRemoteDisgarded.Add(CopyStats.FilesTransited, CopyStats.FilesSkipped, CopyStats.FileSizesUnknown, CopyStats.FileTransitsFailed, CopyStats.FilesSizes, m_elapsed);
                            }
                            else
                            {
                                m_sessionCounters.m_filesTransitedToRemote.Add(CopyStats.FilesTransited, CopyStats.FilesSkipped, CopyStats.FileSizesUnknown, CopyStats.FileTransitsFailed, CopyStats.FilesSizes, m_elapsed);
                            }
                        }
                    }
                    else
                    {
                        m_sessionCounters.m_getCacheEntry_Miss.Add(m_elapsed);
                    }

                    m_sessionCounters = null;
                }
            }
        }

        public GetCacheEntryCountCollector GetCacheEntryCounter()
        {
            return new GetCacheEntryCountCollector(this);
        }
        #endregion GetCacheEntry

        #region IncorporateRecords

        private TimedCounter m_incorporateRecords = new TimedCounter();

        public struct IncorporateRecordsCountCollector : IDisposable
        {
            private SessionCounters m_sessionCounters;
            private ElapsedTimer m_elapsed;

            internal IncorporateRecordsCountCollector(SessionCounters sessionCounters)
            {
                Contract.Requires(sessionCounters != null);

                m_sessionCounters = sessionCounters;
                m_elapsed = ElapsedTimer.StartNew();
            }

            public void Dispose()
            {
                if (m_sessionCounters != null)
                {
                    m_sessionCounters.m_incorporateRecords.Add(m_elapsed);
                    m_sessionCounters = null;
                }
            }
        }

        public IncorporateRecordsCountCollector IncorporateRecordsCounter()
        {
            return new IncorporateRecordsCountCollector(this);
        }

        #endregion IncorporateRecords
    }
}

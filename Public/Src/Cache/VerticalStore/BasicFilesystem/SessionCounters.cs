// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.ContractsLight;
using BuildXL.Cache.ImplementationSupport;
using BuildXL.Cache.Interfaces;

namespace BuildXL.Cache.BasicFilesystem
{
    // This is my first attempt to make the counters that have the data that may be
    // interesting and that is broken out by the type of information we can get.
    // However, I am not yet ready to make this a common standard for the system
    // as I don't yet know how it composes elsewhere.  Plus I would like to unify
    // this with our activity/ETW story (which needs some restructuring)
    internal sealed class SessionCounters : BaseCounters
    {
        // While private, these field names are of a style that
        // is public as the name will be used for the actual stats
        // dictionary generation.  This way the counts are
        // handled in a uniform manner and extended consistently.

        // EnumerateStrongFingerprints is a yield-return of some
        // number of fingerprints.  This provides both number of
        // calls and the amount returned
        private CountedNumberDistribution m_enumerateStrongFingerprints = new CountedNumberDistribution();

        // GetCacheEntry has two cases - one hit and one miss
        private TimedCounter m_getCacheEntry_Hit = new TimedCounter();
        private TimedCounter m_getCacheEntry_Miss = new TimedCounter();

        // PinToCAS as 4 outcomes:
        //   Miss  - failed pin attempt - content did not exist
        //   Hit   - successful pin
        //   Dup   - successful pin - already pinned earlier
        //   Raced - successful pin - another thread raced and pinned it
        // Raced is recorded separately since it still does the I/O for
        // pinning so it still has the cost but would not have been
        // needed a few moments later as another pending pin completed on
        // the same CAS item.
        private TimedCounter m_pinToCas_Miss = new TimedCounter();
        private TimedCounter m_pinToCas_Hit = new TimedCounter();
        private TimedCounter m_pinToCas_Dup = new TimedCounter();
        private TimedCounter m_pinToCas_Raced = new TimedCounter();

        // ProduceFile has 3 cases today but one may need to be renamed
        // Today, since pin is needed before get, the "unpinned" case
        // is the same as a "cache miss" so we are calling it a miss.
        private CountedBytesDistribution m_produceFile_Hit = new CountedBytesDistribution();
        private TimedCounter m_produceFile_Miss = new TimedCounter();
        private TimedCounter m_produceFile_Fail = new TimedCounter();

        private CountedBytesDistribution m_getStream_Hit = new CountedBytesDistribution();
        private TimedCounter m_getStream_Miss = new TimedCounter();
        private TimedCounter m_getStream_Fail = new TimedCounter();

        // Counters for the validation of content API (rarely called)
        private CountedBytesDistribution m_validateContent_Ok = new CountedBytesDistribution();
        private CountedBytesDistribution m_validateContent_Remediated = new CountedBytesDistribution();
        private CountedBytesDistribution m_validateContent_Invalid = new CountedBytesDistribution();
        private TimedCounter m_validateContent_Fail = new TimedCounter();

        // Since close should be called only once this is a bit
        // of overkill but it makes for consistent behaviors
        private TimedCounter m_close = new TimedCounter();
        private SafeDouble m_close_Fingerprints = default(SafeDouble);

        private CountedBytesDistribution m_addToCas_Stream_New = new CountedBytesDistribution();
        private CountedBytesDistribution m_addToCas_Stream_Dup = new CountedBytesDistribution();
        private CountedBytesDistribution m_addToCas_Stream_Failed = new CountedBytesDistribution();
        private CountedBytesDistribution m_addToCas_File_New = new CountedBytesDistribution();
        private CountedBytesDistribution m_addToCas_File_Dup = new CountedBytesDistribution();
        private CountedBytesDistribution m_addToCas_File_Failed = new CountedBytesDistribution();

        private CountedNumberDistribution m_addOrGet_New = new CountedNumberDistribution();
        private CountedNumberDistribution m_addOrGet_Dup = new CountedNumberDistribution();
        private CountedNumberDistribution m_addOrGet_Repair = new CountedNumberDistribution();
        private CountedNumberDistribution m_addOrGet_Det = new CountedNumberDistribution();
        private CountedNumberDistribution m_addOrGet_Get = new CountedNumberDistribution();
        private CountedNumberDistribution m_addOrGet_Failed = new CountedNumberDistribution();

        private TimedCounter m_incorporateRecords = new TimedCounter();
        private SafeDouble m_incorporateRecords_New = default(SafeDouble);
        private SafeDouble m_incorporateRecords_Dup = default(SafeDouble);

        private SafeDouble m_cacheDisconnected = default(SafeDouble);

        // Within each operation we may store other values as we find them
        // increment them.  Since this is a single call, no interlocked operations
        // should be needed there (at least on the Intel memory model and the CLR
        // documented behaviors).  The reason is that one can not switch to another
        // thread except at await boundaries and those can only happen within what
        // seems as a logically single thread of execution (even if it is a different
        // physical thread).  So for a single call into an async function, unless
        // the call itself uses some parallelism, it is logically executing single
        // threaded.  (Just not against other calls to the same function)
        // Thus the per-call counter types here are used to collect timing
        // and other values that are not collected into the main counters until
        // the "dispose" operation and collection into the main counters must use
        // the "safe" modes as defined above.
        #region EnumerateStrongFingerprints

        public struct EnumerateStrongFingerprintsCountCollector : IDisposable
        {
            private SessionCounters m_sessionCounters;
            private ElapsedTimer m_elapsed;
            private double m_count;

            internal EnumerateStrongFingerprintsCountCollector(SessionCounters sessionCounters)
            {
                Contract.Requires(sessionCounters != null);

                m_sessionCounters = sessionCounters;
                m_elapsed = ElapsedTimer.StartNew();
                m_count = 0;
            }

            public void YieldReturn()
            {
                m_count++;
            }

            public void Dispose()
            {
                if (m_sessionCounters != null)
                {
                    m_sessionCounters.m_enumerateStrongFingerprints.Add(m_count, m_elapsed);
                    m_sessionCounters = null;
                }
            }
        }

        public EnumerateStrongFingerprintsCountCollector EnumerateStrongFingerprintsCounter()
        {
            return new EnumerateStrongFingerprintsCountCollector(this);
        }

        #endregion EnumerateStrongFingerprints

        #region GetCacheEntry

        public struct GetCacheEntryCountCollector : IDisposable
        {
            private SessionCounters m_sessionCounters;
            private ElapsedTimer m_elapsed;
            private bool m_hit;

            internal GetCacheEntryCountCollector(SessionCounters sessionCounters)
            {
                Contract.Requires(sessionCounters != null);

                m_sessionCounters = sessionCounters;
                m_elapsed = ElapsedTimer.StartNew();
                m_hit = false;
            }

            public void CacheHit()
            {
                m_hit = true;
            }

            public void Dispose()
            {
                if (m_sessionCounters != null)
                {
                    if (m_hit)
                    {
                        m_sessionCounters.m_getCacheEntry_Hit.Add(m_elapsed);
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

        #region PinToCas

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
                m_theCounter = m_sessionCounters.m_pinToCas_Hit;
            }

            public void PinMiss()
            {
                m_theCounter = m_sessionCounters.m_pinToCas_Miss;
            }

            public void PinRaced()
            {
                m_theCounter = m_sessionCounters.m_pinToCas_Raced;
            }

            public void PinDup()
            {
                m_theCounter = m_sessionCounters.m_pinToCas_Dup;
            }

            public void Dispose()
            {
                if (m_sessionCounters != null)
                {
                    m_sessionCounters = null;
                    var timeUsed = m_elapsed;

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

        public struct ProduceFileCountCollector : IDisposable
        {
            private enum State
            {
                Hit = 0,
                Miss,
                Fail,
            }

            private SessionCounters m_sessionCounters;
            private ElapsedTimer m_elapsed;
            private State m_state;
            private long m_size;

            internal ProduceFileCountCollector(SessionCounters sessionCounters)
            {
                Contract.Requires(sessionCounters != null);

                m_sessionCounters = sessionCounters;
                m_elapsed = ElapsedTimer.StartNew();
                m_state = State.Hit;
                m_size = 0;
            }

            public void Miss()
            {
                m_state = State.Miss;
            }

            public void Fail()
            {
                m_state = State.Fail;
            }

            public void FileSize(long size)
            {
                m_size = size;
            }

            public void Dispose()
            {
                if (m_sessionCounters != null)
                {
                    var timeUsed = m_elapsed;

                    switch (m_state)
                    {
                        case State.Hit:
                            m_sessionCounters.m_produceFile_Hit.Add(m_size, timeUsed);
                            break;

                        case State.Miss:
                            m_sessionCounters.m_produceFile_Miss.Add(timeUsed);
                            break;

                        case State.Fail:
                            m_sessionCounters.m_produceFile_Fail.Add(timeUsed);
                            break;
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

        #region GetStream

        public struct GetStreamCountCollector : IDisposable
        {
            private enum State
            {
                Hit = 0,
                Miss,
                Fail,
            }

            private SessionCounters m_sessionCounters;
            private ElapsedTimer m_elapsed;
            private State m_state;
            private long m_size;

            internal GetStreamCountCollector(SessionCounters sessionCounters)
            {
                m_sessionCounters = sessionCounters;
                m_elapsed = ElapsedTimer.StartNew();
                m_state = State.Hit;
                m_size = 0;
            }

            public void Miss()
            {
                m_state = State.Miss;
            }

            public void Fail()
            {
                m_state = State.Fail;
            }

            public void FileSize(long size)
            {
                m_size = size;
            }

            public void Dispose()
            {
                if (m_sessionCounters != null)
                {
                    var timeUsed = m_elapsed;

                    switch (m_state)
                    {
                        case State.Hit:
                            m_sessionCounters.m_getStream_Hit.Add(m_size, timeUsed);
                            break;

                        case State.Miss:
                            m_sessionCounters.m_getStream_Miss.Add(timeUsed);
                            break;

                        case State.Fail:
                            m_sessionCounters.m_getStream_Fail.Add(timeUsed);
                            break;
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

        #region ValidateContent

        public struct ValidateContentCountCollector : IDisposable
        {
            private SessionCounters m_sessionCounters;
            private ElapsedTimer m_elapsed;
            private ValidateContentStatus m_state;
            private long m_size;

            internal ValidateContentCountCollector(SessionCounters sessionCounters)
            {
                m_sessionCounters = sessionCounters;
                m_elapsed = ElapsedTimer.StartNew();
                m_state = ValidateContentStatus.NotSupported;
                m_size = 0;
            }

            public void FileSize(long size)
            {
                m_size = size;
            }

            public ValidateContentStatus Ok()
            {
                m_state = ValidateContentStatus.Ok;
                return m_state;
            }

            public ValidateContentStatus Remediated()
            {
                m_state = ValidateContentStatus.Remediated;
                return m_state;
            }

            public ValidateContentStatus Invalid()
            {
                m_state = ValidateContentStatus.Invalid;
                return m_state;
            }

            public void Dispose()
            {
                if (m_sessionCounters != null)
                {
                    var timeUsed = m_elapsed;

                    switch (m_state)
                    {
                        case ValidateContentStatus.NotSupported:
                            // A failure has no size available
                            m_sessionCounters.m_validateContent_Fail.Add(timeUsed);
                            break;

                        case ValidateContentStatus.Ok:
                            m_sessionCounters.m_validateContent_Ok.Add(m_size, timeUsed);
                            break;

                        case ValidateContentStatus.Remediated:
                            m_sessionCounters.m_validateContent_Remediated.Add(m_size, timeUsed);
                            break;

                        case ValidateContentStatus.Invalid:
                            m_sessionCounters.m_validateContent_Invalid.Add(m_size, timeUsed);
                            break;
                    }

                    m_sessionCounters = null;
                }
            }
        }

        public ValidateContentCountCollector ValidateSessionCounter()
        {
            return new ValidateContentCountCollector(this);
        }

        #endregion ValidateContent

        #region Close

        public struct CloseCountCollector : IDisposable
        {
            private SessionCounters m_sessionCounters;
            private ElapsedTimer m_elapsed;
            private double m_count;

            internal CloseCountCollector(SessionCounters sessionCounters)
            {
                Contract.Requires(sessionCounters != null);

                m_sessionCounters = sessionCounters;
                m_elapsed = ElapsedTimer.StartNew();
                m_count = 0;
            }

            public void SessionFingerprints(int count)
            {
                m_count += count;
            }

            public void Dispose()
            {
                if (m_sessionCounters != null)
                {
                    m_sessionCounters.m_close.Add(m_elapsed);
                    m_sessionCounters.m_close_Fingerprints.Add(m_count);

                    m_sessionCounters = null;
                }
            }
        }

        public CloseCountCollector CloseCounter()
        {
            return new CloseCountCollector(this);
        }

        #endregion Close

        #region AddToCas

        public struct AddToCasCountCollector : IDisposable
        {
            private SessionCounters m_sessionCounters;
            private ElapsedTimer m_elapsed;
            private long m_size;
            private bool m_file;
            private CountedBytesDistribution m_theCounter;

            internal AddToCasCountCollector(SessionCounters sessionCounters, bool file)
            {
                Contract.Requires(sessionCounters != null);

                m_sessionCounters = sessionCounters;
                m_elapsed = ElapsedTimer.StartNew();
                m_file = file;
                m_size = 0;

                m_theCounter = m_file ? m_sessionCounters.m_addToCas_File_New : m_sessionCounters.m_addToCas_Stream_New;
            }

            public void ContentSize(long size)
            {
                m_size = size;
            }

            public void DuplicatedContent()
            {
                m_theCounter = m_file ? m_sessionCounters.m_addToCas_File_Dup : m_sessionCounters.m_addToCas_Stream_Dup;
            }

            public void Failed()
            {
                m_theCounter = m_file ? m_sessionCounters.m_addToCas_File_Failed : m_sessionCounters.m_addToCas_Stream_Failed;
            }

            public void Dispose()
            {
                if (m_sessionCounters != null)
                {
                    m_sessionCounters = null;

                    var timeUsed = m_elapsed;

                    m_theCounter.Add(m_size, timeUsed);
                }
            }
        }

        public AddToCasCountCollector AddToCasCounterStream()
        {
            return new AddToCasCountCollector(this, false);
        }

        public AddToCasCountCollector AddToCasCounterFile()
        {
            return new AddToCasCountCollector(this, true);
        }

        #endregion AddToCas

        #region AddOrGet

        public struct AddOrGetCountCollector : IDisposable
        {
            private SessionCounters m_sessionCounters;
            private ElapsedTimer m_elapsed;
            private CountedNumberDistribution m_theCounter;
            private int m_entriesCount;

            internal AddOrGetCountCollector(SessionCounters sessionCounters)
            {
                Contract.Requires(sessionCounters != null);

                m_sessionCounters = sessionCounters;
                m_elapsed = ElapsedTimer.StartNew();
                m_theCounter = m_sessionCounters.m_addOrGet_New;
                m_entriesCount = 0;
            }

            public void SetEntriesCount(int count)
            {
                m_entriesCount = count;
            }

            public void Get()
            {
                m_theCounter = m_sessionCounters.m_addOrGet_Get;
            }

            public void Det()
            {
                m_theCounter = m_sessionCounters.m_addOrGet_Det;
            }

            public void Dup()
            {
                m_theCounter = m_sessionCounters.m_addOrGet_Dup;
            }

            public void Failed()
            {
                m_theCounter = m_sessionCounters.m_addOrGet_Failed;
            }

            public void Repair()
            {
                m_theCounter = m_sessionCounters.m_addOrGet_Repair;
            }

            public void Dispose()
            {
                if (m_sessionCounters != null)
                {
                    m_sessionCounters = null;
                    var timeUsed = m_elapsed;

                    m_theCounter.Add(m_entriesCount, timeUsed);
                }
            }
        }

        public AddOrGetCountCollector AddOrGetCounter()
        {
            return new AddOrGetCountCollector(this);
        }

        #endregion AddOrGet

        #region IncorporateRecords

        public struct IncorporateRecordsCountCollector : IDisposable
        {
            private SessionCounters m_sessionCounters;
            private ElapsedTimer m_elapsed;
            private long m_new;
            private long m_dup;

            internal IncorporateRecordsCountCollector(SessionCounters sessionCounters)
            {
                Contract.Requires(sessionCounters != null);

                m_sessionCounters = sessionCounters;
                m_elapsed = ElapsedTimer.StartNew();
                m_new = 0;
                m_dup = 0;
            }

            public void NewRecord()
            {
                m_new++;
            }

            public void DupRecord()
            {
                m_dup++;
            }

            public void Dispose()
            {
                if (m_sessionCounters != null)
                {
                    m_sessionCounters.m_incorporateRecords.Add(m_elapsed);
                    m_sessionCounters.m_incorporateRecords_New.Add(m_new);
                    m_sessionCounters.m_incorporateRecords_Dup.Add(m_dup);
                    m_sessionCounters = null;
                }
            }
        }

        public IncorporateRecordsCountCollector IncorporateRecordsCounter()
        {
            return new IncorporateRecordsCountCollector(this);
        }

        #endregion IncorporateRecords

        #region CacheDisconnected
        public void SetCacheDisconnectedCounter(double count = 1)
        {
            this.m_cacheDisconnected.Add(count);
        }
        #endregion CacheDisconnected
    }
}

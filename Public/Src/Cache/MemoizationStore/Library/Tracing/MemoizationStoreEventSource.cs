// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.Tracing;

// disable 'Missing XML comment for publicly visible type' warnings.
#pragma warning disable 1591
#pragma warning disable SA1600 // Elements must be documented
#pragma warning disable SA1602 // Enumeration items must be documented

namespace BuildXL.Cache.MemoizationStore.Tracing
{
    public enum EventId
    {
        // ReSharper disable once UnusedMember.Global
        None = 0,
        Message,
        StartupStart,
        StartupStop,
        ShutdownStart,
        ShutdownStop,
        GetStatsStart,
        GetStatsStop,
        GetSelectorsStart,
        GetSelectorsStop,
        GetContentHashListStart,
        GetContentHashListStop,
        AddOrGetContentHashListStart,
        AddOrGetContentHashListStop,
        TouchStart,
        TouchStop,
        PurgeStart,
        PurgeStop,
        IncorporateStrongFingerprintsStart,
        IncorporateStrongFingerprintsStop
    }

    [EventSource(Name = "MemoizationStoreTelemetry")]
    public class MemoizationStoreEventSource : EventSource
    {
        // ReSharper disable once MemberCanBePrivate.Global
        public static class Keywords
        {
            public const EventKeywords Message = (EventKeywords)0x0001;
            public const EventKeywords Startup = (EventKeywords)0x0002;
            public const EventKeywords Shutdown = (EventKeywords)0x0004;
            public const EventKeywords GetStats = (EventKeywords)0x0008;
            public const EventKeywords GetSelectors = (EventKeywords)0x0010;
            public const EventKeywords GetContentHashList = (EventKeywords)0x0020;
            public const EventKeywords AddOrGetContentHashList = (EventKeywords)0x0040;
            public const EventKeywords Touch = (EventKeywords)0x0080;
            public const EventKeywords Purge = (EventKeywords)0x0100;
            public const EventKeywords IncorporateStrongFingerprints = (EventKeywords)0x0200;
        }

        public static readonly MemoizationStoreEventSource Instance = new MemoizationStoreEventSource();

        [Event((int)EventId.Message, Keywords = Keywords.Message, Level = EventLevel.Verbose)]
        public void Message(string id, int severity, string value)
        {
            WriteEvent((int)EventId.Message, id, severity, value);
        }

        [Event((int)EventId.StartupStart, Keywords = Keywords.Startup, Level = EventLevel.Verbose)]
        public void StartupStart(string id)
        {
            WriteEvent((int)EventId.StartupStart, id);
        }

        [Event((int)EventId.StartupStop, Keywords = Keywords.Startup, Level = EventLevel.Verbose)]
        public void StartupStop(string id, bool succeeded, string error)
        {
            WriteEvent((int)EventId.StartupStop, id, succeeded, error ?? string.Empty);
        }

        [Event((int)EventId.ShutdownStart, Keywords = Keywords.Shutdown, Level = EventLevel.Verbose)]
        public void ShutdownStart(string id)
        {
            WriteEvent((int)EventId.ShutdownStart, id);
        }

        [Event((int)EventId.ShutdownStop, Keywords = Keywords.Shutdown, Level = EventLevel.Verbose)]
        public void ShutdownStop(string id, bool succeeded, string error)
        {
            WriteEvent((int)EventId.ShutdownStop, id, succeeded, error ?? string.Empty);
        }

        [Event((int)EventId.GetStatsStart, Keywords = Keywords.GetStats, Level = EventLevel.Verbose)]
        public void GetStatsStart()
        {
            WriteEvent((int)EventId.GetStatsStart);
        }

        [Event((int)EventId.GetStatsStop, Keywords = Keywords.GetStats, Level = EventLevel.Verbose)]
        public void GetStatsStop(bool succeeded, string error)
        {
            WriteEvent((int)EventId.GetStatsStop, succeeded, error ?? string.Empty);
        }

        [Event((int)EventId.GetSelectorsStart, Keywords = Keywords.GetSelectors, Level = EventLevel.Verbose)]
        public void GetSelectorsStart(string id, string hash)
        {
            WriteEvent((int)EventId.GetSelectorsStart, id, hash);
        }

        [Event((int)EventId.GetSelectorsStop, Keywords = Keywords.GetSelectors, Level = EventLevel.Verbose)]
        public void GetSelectorsStop(string id)
        {
            WriteEvent((int)EventId.GetSelectorsStop, id);
        }

        [Event((int)EventId.GetContentHashListStart, Keywords = Keywords.GetContentHashList, Level = EventLevel.Verbose)]
        public void GetContentHashListStart(string id, string weakHash, string contentHash, int outputBytes)
        {
            WriteEvent((int)EventId.GetContentHashListStart, id, weakHash, contentHash, outputBytes);
        }

        [Event((int)EventId.GetContentHashListStop, Keywords = Keywords.GetContentHashList, Level = EventLevel.Verbose)]
        public void GetContentHashListStop(string id, bool succeeded, string error)
        {
            WriteEvent((int)EventId.GetContentHashListStop, id, succeeded, error ?? string.Empty);
        }

        [Event((int)EventId.AddOrGetContentHashListStart, Keywords = Keywords.AddOrGetContentHashList, Level = EventLevel.Verbose)]
        public void AddOrGetContentHashListStart(string id, string weakHash, string contentHash, int outputBytes)
        {
            WriteEvent((int)EventId.AddOrGetContentHashListStart, id, weakHash, contentHash, outputBytes);
        }

        [Event((int)EventId.AddOrGetContentHashListStop, Keywords = Keywords.AddOrGetContentHashList, Level = EventLevel.Verbose)]
        public void AddOrGetContentHashListStop(string id, int code, string error)
        {
            WriteEvent((int)EventId.AddOrGetContentHashListStop, id, code, error ?? string.Empty);
        }

        [Event((int)EventId.IncorporateStrongFingerprintsStart, Keywords = Keywords.IncorporateStrongFingerprints, Level = EventLevel.Verbose)]
        public void IncorporateStrongFingerprintsStart(string id)
        {
            WriteEvent((int)EventId.IncorporateStrongFingerprintsStart, id);
        }

        [Event((int)EventId.IncorporateStrongFingerprintsStop, Keywords = Keywords.IncorporateStrongFingerprints, Level = EventLevel.Verbose)]
        public void IncorporateStrongFingerprintsStop(string id, bool succeeded, string error)
        {
            WriteEvent((int)EventId.IncorporateStrongFingerprintsStop, id, succeeded, error ?? string.Empty);
        }

        [Event((int)EventId.TouchStart, Keywords = Keywords.Touch, Level = EventLevel.Verbose)]
        public void TouchStart(string id, int count)
        {
            WriteEvent((int)EventId.TouchStart, id, count);
        }

        [Event((int)EventId.TouchStop, Keywords = Keywords.Touch, Level = EventLevel.Verbose)]
        public void TouchStop(string id)
        {
            WriteEvent((int)EventId.TouchStop, id);
        }

        [Event((int)EventId.PurgeStart, Keywords = Keywords.Purge, Level = EventLevel.Verbose)]
        public void PurgeStart(string id, int count)
        {
            WriteEvent((int)EventId.PurgeStart, id, count);
        }

        [Event((int)EventId.PurgeStop, Keywords = Keywords.Purge, Level = EventLevel.Verbose)]
        public void PurgeStop(string id)
        {
            WriteEvent((int)EventId.PurgeStop, id);
        }

        [NonEvent]
        private unsafe void WriteEvent(int eventId, string arg1, int arg2, string arg3)
        {
            const int count = 3;
            if (arg1 == null)
            {
                arg1 = string.Empty;
            }

            if (arg3 == null)
            {
                arg3 = string.Empty;
            }

            fixed (char* arg1Bytes = arg1)
            fixed (char* arg3Bytes = arg3)
            {
                EventData* descrs = stackalloc EventData[count];
                descrs[0].DataPointer = (IntPtr)arg1Bytes;
                descrs[0].Size = (arg1.Length + 1) * 2;
                descrs[1].DataPointer = (IntPtr)(&arg2);
                descrs[1].Size = 4;
                descrs[2].DataPointer = (IntPtr)arg3Bytes;
                descrs[2].Size = (arg3.Length + 1) * 2;
                WriteEventCore(eventId, count, descrs);
            }
        }

        [NonEvent]
        private unsafe void WriteEvent(int eventId, bool arg1, string arg2)
        {
            const int count = 2;
            if (arg2 == null)
            {
                arg2 = string.Empty;
            }

            fixed (char* arg2Bytes = arg2)
            {
                EventData* descrs = stackalloc EventData[count];
                descrs[0].DataPointer = (IntPtr)(&arg1);
                descrs[0].Size = 4;
                descrs[1].DataPointer = (IntPtr)arg2Bytes;
                descrs[1].Size = (arg2.Length + 1) * 2;
                WriteEventCore(eventId, count, descrs);
            }
        }
    }
}

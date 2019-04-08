// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Diagnostics.Tracing;

// disable 'Missing XML comment for publicly visible type' warnings.
#pragma warning disable 1591
#pragma warning disable SA1600 // Elements must be documented
#pragma warning disable SA1602 // Enumeration items must be documented

namespace BuildXL.Cache.ContentStore.Tracing
{
    [EventSource(Name = "ContentStoreTelemetry")]
    public class ContentStoreInternalEventSource : EventSource
    {
        private enum EventId
        {
            // ReSharper disable once UnusedMember.Local
            None = 0,
            Message,
            StartupStart,
            StartupStop,
            ShutdownStart,
            ShutdownStop,
            GetStatsStart,
            GetStatsStop,
            OpenStreamStart,
            OpenStreamStop,
            PinStart,
            PinStop,
            PlaceFileStart,
            PlaceFileStop,
            PutFileStart,
            PutFileStop,
            PutStreamStart,
            PutStreamStop,
            PurgeStart,
            PurgeStop,
            CreateHardLink,
            PlaceFileCopy,
            ReconstructDirectory,
            EvictStart,
            EvictStop,
            CalibrateStart,
            CalibrateStop
        }

        // ReSharper disable once MemberCanBePrivate.Global
        public static class Keywords
        {
            public const EventKeywords Message = (EventKeywords)0x0001;
            public const EventKeywords Startup = (EventKeywords)0x0002;
            public const EventKeywords Shutdown = (EventKeywords)0x0004;
            public const EventKeywords GetStats = (EventKeywords)0x0008;
            public const EventKeywords OpenStream = (EventKeywords)0x0010;
            public const EventKeywords Pin = (EventKeywords)0x0020;
            public const EventKeywords PlaceFile = (EventKeywords)0x0040;
            public const EventKeywords PutFile = (EventKeywords)0x0080;
            public const EventKeywords PutStream = (EventKeywords)0x0100;
            public const EventKeywords Purge = (EventKeywords)0x0200;
            public const EventKeywords CreateHardLink = (EventKeywords)0x0400;
            public const EventKeywords PlaceFileCopy = (EventKeywords)0x0800;
            public const EventKeywords ReconstructDirectory = (EventKeywords)0x2000;
            public const EventKeywords Evict = (EventKeywords)0x4000;
            public const EventKeywords Calibrate = (EventKeywords)0x8000;
        }

        public static readonly ContentStoreInternalEventSource Instance = new ContentStoreInternalEventSource();

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

        [Event((int)EventId.OpenStreamStart, Keywords = Keywords.OpenStream, Level = EventLevel.Verbose)]
        public void OpenStreamStart(string id, string hash)
        {
            WriteEvent((int)EventId.OpenStreamStart, id, hash);
        }

        [Event((int)EventId.OpenStreamStop, Keywords = Keywords.OpenStream, Level = EventLevel.Verbose)]
        public void OpenStreamStop(string id, int code, string error)
        {
            WriteEvent((int)EventId.OpenStreamStop, id, code, error ?? string.Empty);
        }

        [Event((int)EventId.PinStart, Keywords = Keywords.Pin, Level = EventLevel.Verbose)]
        public void PinStart(string id, string hash)
        {
            WriteEvent((int)EventId.PinStart, id, hash);
        }

        [Event((int)EventId.PinStop, Keywords = Keywords.Pin, Level = EventLevel.Verbose)]
        public void PinStop(string id, int code, string error)
        {
            WriteEvent((int)EventId.PinStop, id, code, error ?? string.Empty);
        }

        [Event((int)EventId.PlaceFileStart, Keywords = Keywords.PlaceFile, Level = EventLevel.Verbose)]
        public void PlaceFileStart(string id, string hash, string path, int accessMode, int replacementMode, int realizationMode)
        {
            WriteEvent((int)EventId.PlaceFileStart, id, hash, path, accessMode, replacementMode, realizationMode);
        }

        [Event((int)EventId.PlaceFileStop, Keywords = Keywords.PlaceFile, Level = EventLevel.Verbose)]
        public void PlaceFileStop(string id, int code, string error)
        {
            WriteEvent((int)EventId.PlaceFileStop, id, code, error ?? string.Empty);
        }

        [Event((int)EventId.PutFileStart, Keywords = Keywords.PutFile, Level = EventLevel.Verbose)]
        public void PutFileStart(string id, string path, int mode, string hash)
        {
            WriteEvent((int)EventId.PutFileStart, id, path, mode, hash ?? string.Empty);
        }

        [Event((int)EventId.PutFileStop, Keywords = Keywords.PutFile, Level = EventLevel.Verbose)]
        public void PutFileStop(string id, bool succeeded, string error, string hash)
        {
            WriteEvent((int)EventId.PutFileStop, id, succeeded, error ?? string.Empty, hash);
        }

        [Event((int)EventId.PutStreamStart, Keywords = Keywords.PutStream, Level = EventLevel.Verbose)]
        public void PutStreamStart(string id, string hash)
        {
            WriteEvent((int)EventId.PutStreamStart, id, hash);
        }

        [Event((int)EventId.PutStreamStop, Keywords = Keywords.PutStream, Level = EventLevel.Verbose)]
        public void PutStreamStop(string id, bool succeeded, string error, string hash)
        {
            WriteEvent((int)EventId.PutStreamStop, id, succeeded, error ?? string.Empty, hash);
        }

        [Event((int)EventId.PurgeStart, Keywords = Keywords.Purge, Level = EventLevel.Verbose)]
        public void PurgeStart()
        {
            WriteEvent((int)EventId.PurgeStart);
        }

        [Event((int)EventId.PurgeStop, Keywords = Keywords.Purge, Level = EventLevel.Verbose)]
        public void PurgeStop(bool succeeded, string error, long size, long files, long pinned)
        {
            WriteEvent((int)EventId.PurgeStop, succeeded, error ?? string.Empty, size, files, pinned);
        }

        [Event((int)EventId.CalibrateStart, Keywords = Keywords.Calibrate, Level = EventLevel.Verbose)]
        public void CalibrateStart()
        {
            WriteEvent((int)EventId.CalibrateStart);
        }

        [Event((int)EventId.CalibrateStop, Keywords = Keywords.Calibrate, Level = EventLevel.Verbose)]
        public void CalibrateStop(bool succeeded, string error, long newSize)
        {
            WriteEvent((int)EventId.CalibrateStop, succeeded, error ?? string.Empty, newSize);
        }

        [Event((int)EventId.EvictStart, Keywords = Keywords.Evict, Level = EventLevel.Verbose)]
        public void EvictStart(string hash)
        {
            WriteEvent((int)EventId.EvictStart, hash);
        }

        [Event((int)EventId.EvictStop, Keywords = Keywords.Evict, Level = EventLevel.Verbose)]
        public void EvictStop(bool succeeded, long size, long files, long pinned)
        {
            WriteEvent((int)EventId.EvictStop, succeeded, size, files, pinned);
        }

        [Event((int)EventId.CreateHardLink, Keywords = Keywords.CreateHardLink, Level = EventLevel.Verbose)]
        public void CreateHardLink(string id, int result, string source, string destination, int mode, bool replace)
        {
            WriteEvent((int)EventId.CreateHardLink, id, result, source, destination, mode, replace);
        }

        [Event((int)EventId.PlaceFileCopy, Keywords = Keywords.PlaceFileCopy, Level = EventLevel.Verbose)]
        public void PlaceFileCopy(string id, string path, string hash)
        {
            WriteEvent((int)EventId.PlaceFileCopy, id, path, hash);
        }

        [Event((int)EventId.ReconstructDirectory, Keywords = Keywords.ReconstructDirectory, Level = EventLevel.Verbose)]
        public void ReconstructDirectory()
        {
            WriteEvent((int)EventId.ReconstructDirectory);
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

        [NonEvent]
        private unsafe void WriteEvent(int eventId, string arg1, string arg2, string arg3, int arg4, int arg5, int arg6)
        {
            const int count = 6;
            if (arg1 == null)
            {
                arg1 = string.Empty;
            }

            if (arg2 == null)
            {
                arg2 = string.Empty;
            }

            if (arg3 == null)
            {
                arg3 = string.Empty;
            }

            fixed (char* arg1Bytes = arg1)
            fixed (char* arg2Bytes = arg2)
            fixed (char* arg3Bytes = arg3)
            {
                EventData* descrs = stackalloc EventData[count];
                descrs[0].DataPointer = (IntPtr)arg1Bytes;
                descrs[0].Size = (arg1.Length + 1) * 2;
                descrs[1].DataPointer = (IntPtr)arg2Bytes;
                descrs[1].Size = (arg2.Length + 1) * 2;
                descrs[2].DataPointer = (IntPtr)arg3Bytes;
                descrs[2].Size = (arg3.Length + 1) * 2;
                descrs[3].DataPointer = (IntPtr)(&arg4);
                descrs[3].Size = 4;
                descrs[4].DataPointer = (IntPtr)(&arg5);
                descrs[4].Size = 4;
                descrs[5].DataPointer = (IntPtr)(&arg6);
                descrs[5].Size = 4;
                WriteEventCore(eventId, count, descrs);
            }
        }

        [NonEvent]
        private unsafe void WriteEvent(int eventId, string arg1, string arg2, int arg3, string arg4)
        {
            const int count = 4;
            if (arg1 == null)
            {
                arg1 = string.Empty;
            }

            if (arg2 == null)
            {
                arg2 = string.Empty;
            }

            if (arg4 == null)
            {
                arg4 = string.Empty;
            }

            fixed (char* arg1Bytes = arg1)
            fixed (char* arg2Bytes = arg2)
            fixed (char* arg4Bytes = arg4)
            {
                EventData* descrs = stackalloc EventData[count];
                descrs[0].DataPointer = (IntPtr)arg1Bytes;
                descrs[0].Size = (arg1.Length + 1) * 2;
                descrs[1].DataPointer = (IntPtr)arg2Bytes;
                descrs[1].Size = (arg2.Length + 1) * 2;
                descrs[2].DataPointer = (IntPtr)(&arg3);
                descrs[2].Size = 4;
                descrs[3].DataPointer = (IntPtr)arg4Bytes;
                descrs[3].Size = (arg4.Length + 1) * 2;
                WriteEventCore(eventId, count, descrs);
            }
        }

        [NonEvent]
        private unsafe void WriteEvent(int eventId, string arg1, int arg2, string arg3, string arg4, int arg5, bool arg6)
        {
            const int count = 6;
            if (arg1 == null)
            {
                arg1 = string.Empty;
            }

            if (arg3 == null)
            {
                arg3 = string.Empty;
            }

            if (arg4 == null)
            {
                arg4 = string.Empty;
            }

            fixed (char* arg1Bytes = arg1)
            fixed (char* arg3Bytes = arg3)
            fixed (char* arg4Bytes = arg4)
            {
                EventData* descrs = stackalloc EventData[count];
                descrs[0].DataPointer = (IntPtr)arg1Bytes;
                descrs[0].Size = (arg1.Length + 1) * 2;
                descrs[1].DataPointer = (IntPtr)(&arg2);
                descrs[1].Size = 4;
                descrs[2].DataPointer = (IntPtr)arg3Bytes;
                descrs[2].Size = (arg3.Length + 1) * 2;
                descrs[3].DataPointer = (IntPtr)arg4Bytes;
                descrs[3].Size = (arg4.Length + 1) * 2;
                descrs[4].DataPointer = (IntPtr)(&arg5);
                descrs[4].Size = 4;
                descrs[5].DataPointer = (IntPtr)(&arg6);
                descrs[5].Size = 4;
                WriteEventCore(eventId, count, descrs);
            }
        }
    }
}

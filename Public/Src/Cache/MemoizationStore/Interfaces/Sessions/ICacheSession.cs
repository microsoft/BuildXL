// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BuildXL.Cache.ContentStore.Interfaces.Sessions;

namespace BuildXL.Cache.MemoizationStore.Interfaces.Sessions
{
    /// <nodoc />
    public interface ICacheSession : IMemoizationSession, IContentSession, IConfigurablePin
    {
    }

    /// <nodoc />
    public interface ICacheSessionWithLevelSelectors : ICacheSession, IMemoizationSessionWithLevelSelectors
    {
    }
}

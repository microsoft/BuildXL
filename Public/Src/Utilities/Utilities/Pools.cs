// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using BuildXL.Utilities.Collections;

namespace BuildXL.Utilities
{
    /// <summary>
    /// A common set of object pools.
    /// </summary>
    public static class Pools
    {
        /// <summary>
        /// Global pool for memory streams.
        /// </summary>
        public static readonly ObjectPool<MemoryStream> MemoryStreamPool = new ObjectPool<MemoryStream>(
            () => new MemoryStream(),
            // Use Func instead of Action to avoid redundant delegate reconstruction.
            stream => { stream.Position = 0; return stream; });

        /// <summary>
        /// Global pool of HashSet&lt;PathAtom&gt; instances.
        /// </summary>
        public static readonly ObjectPool<HashSet<PathAtom>> PathAtomSetPool = CreateSetPool<PathAtom>();

        /// <summary>
        /// Creates a list pool for the specified type
        /// </summary>
        public static ObjectPool<List<T>> CreateListPool<T>()
        {
            return new ObjectPool<List<T>>(
                () => new List<T>(),
                // Use Func instead of Action to avoid redundant delegate reconstruction.
                list => { list.Clear(); return list; });
        }

        /// <summary>
        /// Creates a queue pool for the specified type
        /// </summary>
        public static ObjectPool<Queue<T>> CreateQueuePool<T>()
        {
            return new ObjectPool<Queue<T>>(
                () => new Queue<T>(),
                // Use Func instead of Action to avoid redundant delegate reconstruction.
                queue => { queue.Clear(); return queue; });
        }

        /// <summary>
        /// Creates a hash set pool for the specified type
        /// </summary>
        public static ObjectPool<HashSet<T>> CreateSetPool<T>(IEqualityComparer<T> comparer = null)
        {
            comparer = comparer ?? EqualityComparer<T>.Default;
            return new ObjectPool<HashSet<T>>(
                () => new HashSet<T>(comparer),
                // Use Func instead of Action to avoid redundant delegate reconstruction.
                set => { set.Clear(); return set; });
        }

        /// <summary>
        /// Global pool of maps from <see cref="BuildXL.Utilities.FileArtifact"/> to <see cref="BuildXL.Utilities.DirectoryArtifact"/>.
        /// </summary>
        public static ObjectPool<Dictionary<FileArtifact, DirectoryArtifact>> FileDirectoryMapPool { get; } =
            new ObjectPool<Dictionary<FileArtifact, DirectoryArtifact>>(
                () => new Dictionary<FileArtifact, DirectoryArtifact>(),
                map => { map.Clear(); return map; });

        /// <summary>
        /// Global pool of maps from <see cref="BuildXL.Utilities.FileArtifact"/> to many <see cref="BuildXL.Utilities.DirectoryArtifact"/>.
        /// </summary>
        public static ObjectPool<MultiValueDictionary<FileArtifact, DirectoryArtifact>> FileMultiDirectoryMapPool { get; } =
            new ObjectPool<MultiValueDictionary<FileArtifact, DirectoryArtifact>>(
                () => new MultiValueDictionary<FileArtifact, DirectoryArtifact>(),
                map => { map.Clear(); return map; });

        /// <summary>
        /// Global pool of maps from <see cref="BuildXL.Utilities.AbsolutePath"/> to <see cref="BuildXL.Utilities.FileArtifactWithAttributes"/>.
        /// </summary>
        public static ObjectPool<Dictionary<AbsolutePath, FileArtifactWithAttributes>> AbsolutePathFileArtifactWithAttributesMap { get; } =
            new ObjectPool<Dictionary<AbsolutePath, FileArtifactWithAttributes>>(
                () => new Dictionary<AbsolutePath, FileArtifactWithAttributes>(),
                map => { map.Clear(); return map;});

        /// <summary>
        /// Global pool of maps from string to <see cref="BuildXL.Utilities.FileArtifactWithAttributes"/>.
        /// </summary>
        public static ObjectPool<Dictionary<string, FileArtifactWithAttributes>> StringFileArtifactWithAttributesMap { get; } =
            new ObjectPool<Dictionary<string, FileArtifactWithAttributes>>(
                () => new Dictionary<string, FileArtifactWithAttributes>(),
                map => { map.Clear(); return map; });

        /// <summary>
        /// Global pool of StringBuilder instances.
        /// </summary>
        public static ObjectPool<StringBuilder> StringBuilderPool { get; } = new ObjectPool<StringBuilder>(
            () => new StringBuilder(),
            // Use Func instead of Action to avoid redundant delegate reconstruction.
            sb => { sb.Clear(); return sb; });

        /// <summary>
        /// Global pool of List&lt;string&gt; instances.
        /// </summary>
        public static ObjectPool<List<string>> StringListPool { get; } = CreateListPool<string>();

        /// <summary>
        /// Global pool of HashSet&lt;string&gt; instances.
        /// </summary>
        public static ObjectPool<HashSet<string>> StringSetPool { get; } = CreateSetPool<string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Global pool of List&lt;StringId&gt; instances.
        /// </summary>
        public static ObjectPool<List<StringId>> StringIdListPool { get; } = CreateListPool<StringId>();

        /// <summary>
        /// Global pool of List&lt;FileArtifact&gt; instances.
        /// </summary>
        public static ObjectPool<List<FileArtifact>> FileArtifactListPool { get; } = CreateListPool<FileArtifact>();

        /// <summary>
        /// Global pool of List&lt;FileArtifactWithAttributes&gt; instances.
        /// </summary>
        public static ObjectPool<List<FileArtifactWithAttributes>> FileArtifactWithAttributesListPool { get; } = CreateListPool<FileArtifactWithAttributes>();

        /// <summary>
        /// Global pool of List&lt;DirectoryArtifact&gt; instances.
        /// </summary>
        public static ObjectPool<List<DirectoryArtifact>> DirectoryArtifactListPool { get; } = CreateListPool<DirectoryArtifact>();

        /// <summary>
        /// Global pool of Queue&lt;DirectoryArtifact&gt; instances.
        /// </summary>
        public static ObjectPool<Queue<DirectoryArtifact>> DirectoryArtifactQueuePool { get; } = CreateQueuePool<DirectoryArtifact>();

        /// <summary>
        /// Global pool of List&lt;AbsolutePath&gt; instances.
        /// </summary>
        public static ObjectPool<List<AbsolutePath>> AbsolutePathListPool { get; } = CreateListPool<AbsolutePath>();

        /// <summary>
        /// Global pool of List&lt;PathAtom&gt; instances.
        /// </summary>
        public static ObjectPool<List<PathAtom>> PathAtomListPool { get; } = CreateListPool<PathAtom>();

        /// <summary>
        /// Global pool of List&lt;IdentifierAtom&gt; instances.
        /// </summary>
        public static ObjectPool<List<SymbolAtom>> IdentifierAtomListPool { get; } = CreateListPool<SymbolAtom>();

        /// <summary>
        /// Global pool of HashSet&lt;FileArtifact&gt; instances.
        /// </summary>
        public static ObjectPool<HashSet<FileArtifact>> FileArtifactSetPool { get; } = CreateSetPool<FileArtifact>();

        /// <summary>
        /// Global pool of HashSet&lt;FileArtifactWithAttributes&gt; instances.
        /// </summary>
        public static ObjectPool<HashSet<FileArtifactWithAttributes>> FileArtifactWithAttributesSetPool { get; } = CreateSetPool<FileArtifactWithAttributes>();

        /// <summary>
        /// Global pool of HashSet&lt;DirectoryArtifact&gt; instances.
        /// </summary>
        public static ObjectPool<HashSet<DirectoryArtifact>> DirectoryArtifactSetPool { get; } = CreateSetPool<DirectoryArtifact>();

        /// <summary>
        /// Global pool of HashSet&lt;AbsolutePath&gt; instances.
        /// </summary>
        public static ObjectPool<HashSet<AbsolutePath>> AbsolutePathSetPool { get; } = CreateSetPool<AbsolutePath>();

        /// <summary>
        /// Global pool of char[] instances.
        /// </summary>
        public static ArrayPool<char> CharArrayPool { get; } = new ArrayPool<char>(260 /* MAX PATH */);

        /// <summary>
        /// Global pool of byte[] instances.
        /// </summary>
        public static ArrayPool<byte> ByteArrayPool { get; } = new ArrayPool<byte>(1024);
        
        /// <summary>
        /// Global pool of object[] instances (used by evaluation)
        /// </summary>
        public static ArrayPool<object> ObjectArrayPool { get; } = new ArrayPool<object>(1024);

        /// <summary>
        /// Global pool of HashSet&lt;StringId&gt; instances.
        /// </summary>
        public static ObjectPool<HashSet<StringId>> StringIdSetPool { get; } = CreateSetPool<StringId>();

        /// <summary>
        /// Gets a StringBuilder instance from a common object pool.
        /// </summary>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the StringBuilder. Calling Dispose returns the StringBuilder to the
        /// pool.
        /// </remarks>
        public static PooledObjectWrapper<StringBuilder> GetStringBuilder()
        {
            return StringBuilderPool.GetInstance();
        }

        /// <summary>
        /// Gets a char[] instance from a common object pool.
        /// </summary>
        /// <param name="minimumCapacity">the minimum capacity of the returned char array.</param>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the char[]. Calling Dispose returns the char[] to the
        /// pool.
        /// </remarks>
        public static PooledObjectWrapper<char[]> GetCharArray(int minimumCapacity)
        {
            return CharArrayPool.GetInstance(minimumCapacity);
        }

        /// <summary>
        /// Gets a byte[] instance from a common object pool.
        /// </summary>
        /// <param name="minimumCapacity">the minimum capacity of the returned byte array.</param>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the byte[]. Calling Dispose returns the byte[] to the
        /// pool.
        /// </remarks>
        public static PooledObjectWrapper<byte[]> GetByteArray(int minimumCapacity)
        {
            return ByteArrayPool.GetInstance(minimumCapacity);
        }

        /// <summary>
        /// Gets an object[] instance from a common object pool.
        /// </summary>
        public static PooledObjectWrapper<object[]> GetObjectArray(int minimumCapacity)
        {
            return ObjectArrayPool.GetInstance(minimumCapacity);
        }

        /// <summary>
        /// Gets an List&lt;string&gt; instance from a common object pool.
        /// </summary>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the list. Calling Dispose returns the list to the
        /// pool.
        /// </remarks>
        public static PooledObjectWrapper<List<string>> GetStringList()
        {
            return StringListPool.GetInstance();
        }

        /// <summary>
        /// Gets an HashSet&lt;string&gt; instance from a common object pool.
        /// </summary>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the list. Calling Dispose returns the list to the
        /// pool.
        /// </remarks>
        public static PooledObjectWrapper<HashSet<string>> GetStringSet()
        {
            return StringSetPool.GetInstance();
        }

        /// <summary>
        /// Gets an List&lt;StringId&gt; instance from a common object pool.
        /// </summary>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the list. Calling Dispose returns the list to the
        /// pool.
        /// </remarks>
        public static PooledObjectWrapper<List<StringId>> GetStringIdList()
        {
            return StringIdListPool.GetInstance();
        }

        /// <summary>
        /// Gets an List&lt;FileArtifact&gt; instance from a common object pool.
        /// </summary>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the list. Calling Dispose returns the list to the
        /// pool.
        /// </remarks>
        public static PooledObjectWrapper<List<FileArtifact>> GetFileArtifactList()
        {
            return FileArtifactListPool.GetInstance();
        }
    
        /// <summary>
        /// Gets an List&lt;FileArtifact&gt; instance from a common object pool.
        /// </summary>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the list. Calling Dispose returns the list to the
        /// pool.
        /// </remarks>
        public static PooledObjectWrapper<List<FileArtifactWithAttributes>> GetFileArtifactWithAttributesList()
        {
            return FileArtifactWithAttributesListPool.GetInstance();
        }

        /// <summary>
        /// Gets an List&lt;DirectoryArtifact&gt; instance from a common object pool.
        /// </summary>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the list. Calling Dispose returns the list to the
        /// pool.
        /// </remarks>
        public static PooledObjectWrapper<List<DirectoryArtifact>> GetDirectoryArtifactList()
        {
            return DirectoryArtifactListPool.GetInstance();
        }

        /// <summary>
        /// Gets an List&lt;AbsolutePath&gt; instance from a common object pool.
        /// </summary>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the list. Calling Dispose returns the list to the
        /// pool.
        /// </remarks>
        public static PooledObjectWrapper<List<AbsolutePath>> GetAbsolutePathList()
        {
            return AbsolutePathListPool.GetInstance();
        }

        /// <summary>
        /// Gets a List&lt;PathAtom&gt; instance from a common object pool.
        /// </summary>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the list. Calling Dispose returns the list to the
        /// pool.
        /// </remarks>
        public static PooledObjectWrapper<List<PathAtom>> GetPathAtomList()
        {
            return PathAtomListPool.GetInstance();
        }

        /// <summary>
        /// Gets a HashSet&lt;PathAtom&gt; instance from a common object pool.
        /// </summary>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the list. Calling Dispose returns the list to the
        /// pool.
        /// </remarks>
        public static PooledObjectWrapper<HashSet<PathAtom>> GetPathAtomSet()
        {
            return PathAtomSetPool.GetInstance();
        }

        /// <summary>
        /// Gets a List&lt;IdentifierAtom&gt; instance from a common object pool.
        /// </summary>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the list. Calling Dispose returns the list to the
        /// pool.
        /// </remarks>
        public static PooledObjectWrapper<List<SymbolAtom>> GetIdentifierAtomList()
        {
            return IdentifierAtomListPool.GetInstance();
        }

        /// <summary>
        /// Gets an HashSet&lt;FileArtifact&gt; instance from a common object pool.
        /// </summary>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the set. Calling Dispose returns the set to the
        /// pool.
        /// </remarks>
        public static PooledObjectWrapper<HashSet<FileArtifact>> GetFileArtifactSet()
        {
            return FileArtifactSetPool.GetInstance();
        }

        /// <summary>
        /// Gets an HashSet&lt;FileArtifactWithAttributes&gt; instance from a common object pool.
        /// </summary>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the set. Calling Dispose returns the set to the
        /// pool.
        /// </remarks>
        public static PooledObjectWrapper<HashSet<FileArtifactWithAttributes>> GetFileArtifactWithAttributesSet()
        {
            return FileArtifactWithAttributesSetPool.GetInstance();
        }
        

        /// <summary>
        /// Gets an HashSet&lt;DirectoryArtifact&gt; instance from a common object pool.
        /// </summary>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the set. Calling Dispose returns the set to the
        /// pool.
        /// </remarks>
        public static PooledObjectWrapper<HashSet<DirectoryArtifact>> GetDirectoryArtifactSet()
        {
            return DirectoryArtifactSetPool.GetInstance();
        }

        /// <summary>
        /// Gets an HashSet&lt;AbsolutePath&gt; instance from a common object pool.
        /// </summary>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the set. Calling Dispose returns the set to the
        /// pool.
        /// </remarks>
        public static PooledObjectWrapper<HashSet<AbsolutePath>> GetAbsolutePathSet()
        {
            return AbsolutePathSetPool.GetInstance();
        }

        /// <summary>
        /// Gets a mapping from <see cref="BuildXL.Utilities.FileArtifact"/> to <see cref="DirectoryArtifact"/> from a common object pool.
        /// </summary>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the set. Calling Dispose returns the set to the
        /// pool.
        /// </remarks>
        public static PooledObjectWrapper<Dictionary<FileArtifact, DirectoryArtifact>> GetFileDirectoryMap()
        {
            return FileDirectoryMapPool.GetInstance();
        }

        /// <summary>
        /// Gets a mapping from <see cref="BuildXL.Utilities.FileArtifact"/> to many <see cref="DirectoryArtifact"/> from a common object pool.
        /// </summary>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the set. Calling Dispose returns the set to the pool.
        /// </remarks>
        public static PooledObjectWrapper<MultiValueDictionary<FileArtifact, DirectoryArtifact>> GetFileMultiDirectoryMap()
        {
            return FileMultiDirectoryMapPool.GetInstance();
        }

        /// <summary>
        /// Gets an HashSet&lt;StringId&gt; instance from a common object pool.
        /// </summary>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the list. Calling Dispose returns the list to the
        /// pool.
        /// </remarks>
        public static PooledObjectWrapper<HashSet<StringId>> GetStringIdSet()
        {
            return StringIdSetPool.GetInstance();
        }

        /// <summary>
        /// Gets a mapping from <see cref="BuildXL.Utilities.AbsolutePath"/> to <see cref="FileArtifactWithAttributes"/> from a common object pool.
        /// </summary>
        /// <remarks>
        /// You are expected to call the Dispose method on the returned PooledObjectWrapper instance
        /// when you are done with the set. Calling Dispose returns the set to the pool.
        /// </remarks>
        public static PooledObjectWrapper<Dictionary<AbsolutePath, FileArtifactWithAttributes>> GetAbsolutePathFileArtifactWithAttributesMap()
        {
            return AbsolutePathFileArtifactWithAttributesMap.GetInstance();
        }
    }
}

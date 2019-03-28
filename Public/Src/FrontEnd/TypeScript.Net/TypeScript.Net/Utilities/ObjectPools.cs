// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System.Collections.Generic;
using System.Text;
using System.Threading;
using BuildXL.Utilities;
using TypeScript.Net.Binding;
using TypeScript.Net.Parsing;
using TypeScript.Net.Types;

namespace TypeScript.Net
{
    internal static class ObjectPools
    {
        // Using thread local object pool for performance reasons.
        private static readonly ThreadLocal<ThreadLocalObjectPool<List<NodeWalker.NodeOrNodeArray>>> s_nodeListPool =
            new ThreadLocal<ThreadLocalObjectPool<List<NodeWalker.NodeOrNodeArray>>>(
                () => new ThreadLocalObjectPool<List<NodeWalker.NodeOrNodeArray>>(
                    () => new List<NodeWalker.NodeOrNodeArray>(),
                    lst => lst.Clear()));

        public static ThreadLocalObjectPool<List<NodeWalker.NodeOrNodeArray>> NodeListPool => s_nodeListPool.Value;

        public static readonly ObjectPool<List<IExpression>> ExpressionListPool = new ObjectPool<List<IExpression>>(
            () => new List<IExpression>(),
            lst => { lst.Clear(); return lst; });

        public static readonly ObjectPool<StringBuilder> StringBuilderPool = new ObjectPool<StringBuilder>(
            () => new StringBuilder(),
            lst => { lst.Clear(); return lst; });

        public static readonly ObjectPool<Binder.ModuleInstanceStateContext> ModuleInstanceStateContextPool = new ObjectPool<Binder.ModuleInstanceStateContext>(
            () => new Binder.ModuleInstanceStateContext(),
            v => { v.State = ModuleInstanceState.NonInstantiated; return v; });

        public static readonly ObjectPool<List<IType>> TypeResolutionPool = Pools.CreateListPool<IType>();
    }
}

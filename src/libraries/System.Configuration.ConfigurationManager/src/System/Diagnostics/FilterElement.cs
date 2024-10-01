// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace System.Diagnostics
{
    [RequiresUnreferencedCode("Base type has RUC: https://github.com/dotnet/runtime/issues/107660")]
    internal sealed class FilterElement : TypedElement
    {
        private static readonly ConditionalWeakTable<TraceFilter, string> s_initData = new();

        public FilterElement() : base(typeof(TraceFilter)) { }

        public TraceFilter GetRuntimeObject()
        {
            TraceFilter newFilter = (TraceFilter)BaseGetRuntimeObject();
            s_initData.AddOrUpdate(newFilter, InitData);
            return newFilter;
        }

        internal TraceFilter RefreshRuntimeObject(TraceFilter filter)
        {
            if (Type.GetType(TypeName) != filter.GetType() || InitDataChanged(filter))
            {
                // Type or initdata changed.
                _runtimeObject = null;
                return GetRuntimeObject();
            }
            else
            {
                return filter;
            }
        }

        private bool InitDataChanged(TraceFilter filter) => !s_initData.TryGetValue(filter, out string previousInitData) || InitData != previousInitData;
    }
}

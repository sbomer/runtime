// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace System.Reflection.TypeLoading
{
    internal abstract partial class RoModule
    {
        [RequiresUnreferencedCode("Types might be removed")]
        private static class TypeFactoryDelegates
        {
            public static readonly Func<RoType, RoArrayType> SzArray = (e) => new RoArrayType(e, multiDim: false, rank: 1);
            public static readonly Func<RoArrayType.Key, RoArrayType> MdArray = (k) => new RoArrayType(k.ElementType, multiDim: true, rank: k.Rank);
            public static readonly Func<RoType, RoByRefType> ByRef = (e) => new RoByRefType(e);
            public static readonly Func<RoConstructedGenericType.Key, RoConstructedGenericType> ConstructedGenericType =
                (k) => new RoConstructedGenericType(k.GenericTypeDefinition, k.GenericTypeArguments);
        }

        //
        // SzArrays
        //
        [RequiresUnreferencedCode("Types might be removed")]
        internal RoArrayType GetUniqueArrayType(RoType elementType)
        {
            // Modified types do not support Equals\GetHashCode.
            return elementType is RoModifiedType ?
                TypeFactoryDelegates.SzArray(elementType) :
                _szArrayDict.GetOrAdd(elementType, TypeFactoryDelegates.SzArray);
        }
        private readonly ConcurrentDictionary<RoType, RoArrayType> _szArrayDict = new ConcurrentDictionary<RoType, RoArrayType>();

        //
        // MdArrays
        //
        [RequiresUnreferencedCode("Types might be removed")]
        internal RoArrayType GetUniqueArrayType(RoType elementType, int rank)
        {
            // Modified types do not support Equals\GetHashCode.
            RoArrayType.Key key = new(elementType, rank: rank);
            return elementType is RoModifiedType ?
                TypeFactoryDelegates.MdArray(key) :
                _mdArrayDict.GetOrAdd(key, TypeFactoryDelegates.MdArray);
        }
        private readonly ConcurrentDictionary<RoArrayType.Key, RoArrayType> _mdArrayDict = new ConcurrentDictionary<RoArrayType.Key, RoArrayType>();

        //
        // ByRefs
        //
        [RequiresUnreferencedCode("Types might be removed")]
        internal RoByRefType GetUniqueByRefType(RoType elementType)
        {
            // Modified types do not support Equals\GetHashCode.
            return elementType is RoModifiedType ?
                TypeFactoryDelegates.ByRef(elementType) :
                _byRefDict.GetOrAdd(elementType, TypeFactoryDelegates.ByRef);
        }
        private readonly ConcurrentDictionary<RoType, RoByRefType> _byRefDict = new ConcurrentDictionary<RoType, RoByRefType>();

        //
        // Pointers
        //
        [RequiresUnreferencedCode("Types might be removed")]
        internal RoPointerType GetUniquePointerType(RoType elementType)
        {
            return elementType is RoModifiedType ?
                new RoPointerType(elementType) :
                _pointerDict.GetOrAdd(elementType, (e) => new RoPointerType(e));
        }
        private readonly ConcurrentDictionary<RoType, RoPointerType> _pointerDict = new ConcurrentDictionary<RoType, RoPointerType>();

        //
        // Constructed Generic Types
        //
        [RequiresUnreferencedCode("Types might be removed")]
        internal RoConstructedGenericType GetUniqueConstructedGenericType(RoDefinitionType genericTypeDefinition, RoType[] genericTypeArguments)
        {
            return _constructedGenericTypeDict.GetOrAdd(new RoConstructedGenericType.Key(genericTypeDefinition, genericTypeArguments), TypeFactoryDelegates.ConstructedGenericType);
        }
        private readonly ConcurrentDictionary<RoConstructedGenericType.Key, RoConstructedGenericType> _constructedGenericTypeDict = new ConcurrentDictionary<RoConstructedGenericType.Key, RoConstructedGenericType>();
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics.CodeAnalysis;

namespace System.Reflection.TypeLoading
{
    /// <summary>
    /// A convenience class that holds the palette of core types that were successfully loaded (or the reason they were not.)
    /// </summary>
    internal sealed class CoreTypes
    {
        private readonly AnnotatedRoType[] _coreTypes;
        private readonly Exception?[] _exceptions;

        internal readonly struct AnnotatedRoType
        {
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
            public readonly RoType? Type;

            public AnnotatedRoType([DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)] RoType? type) => Type = type;
        }

        [RequiresUnreferencedCode(Helpers.RequiresUnreferencedCodeMessage)]
        internal CoreTypes(MetadataLoadContext loader, string? coreAssemblyName)
        {
            int numCoreTypes = (int)CoreType.NumCoreTypes;
            AnnotatedRoType[] coreTypes = new AnnotatedRoType[numCoreTypes];
            Exception?[] exceptions = new Exception[numCoreTypes];
            RoAssembly? coreAssembly = loader.TryGetCoreAssembly(coreAssemblyName, out Exception? e);
            if (coreAssembly == null)
            {
                // If the core assembly was not found, don't continue.
                throw e!;
            }
            else
            {
                for (int i = 0; i < numCoreTypes; i++)
                {
                    ((CoreType)i).GetFullName(out ReadOnlySpan<byte> ns, out ReadOnlySpan<byte> name);
                    RoType? type = coreAssembly.GetTypeCore(ns, name, ignoreCase: false, out e);
                    coreTypes[i] = new AnnotatedRoType(type);
                    if (type == null)
                    {
                        exceptions[i] = e;
                    }
                }
            }
            _coreTypes = coreTypes;
            _exceptions = exceptions;
        }

        /// <summary>
        /// Returns null if the specific core type did not exist or could not be loaded. Call GetException(coreType) to get detailed info.
        /// </summary>
        public RoType? this[CoreType coreType]
        {
            [return: DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors)]
            get => _coreTypes[(int)coreType].Type;
        }
        public Exception? GetException(CoreType coreType) => _exceptions[(int)coreType];
    }
}

// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Collections.Generic;

using Internal.IL;
using Internal.TypeSystem;
using Internal.TypeSystem.Ecma;

namespace ILCompiler.Dataflow
{
    public class ILTrimILProvider : Internal.IL.ILProvider
    {
        private readonly object _lock = new();
        private readonly Dictionary<MethodDesc, MethodIL> _methodILCache = new();

        public override MethodIL GetMethodIL(MethodDesc method)
        {
            var ecmaMethod = (EcmaMethod)method.GetTypicalMethodDefinition();

            lock (_lock)
            {
                if (_methodILCache.TryGetValue(method, out MethodIL methodIL))
                    return methodIL;

                if (!_methodILCache.TryGetValue(ecmaMethod, out MethodIL definitionIL))
                {
                    definitionIL = EcmaMethodIL.Create(ecmaMethod);
                    _methodILCache.Add(ecmaMethod, definitionIL);
                }

                if (method == ecmaMethod || definitionIL is null)
                    return definitionIL;

                methodIL = new InstantiatedMethodIL(method, definitionIL);
                _methodILCache.Add(method, methodIL);

                return methodIL;
            }
        }
    }
}

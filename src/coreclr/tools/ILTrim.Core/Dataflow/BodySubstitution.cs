// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.Diagnostics;

using Internal.IL;
using Internal.TypeSystem;

namespace ILCompiler
{
    public sealed class BodySubstitution
    {
        private static readonly object Throw = new();

        private readonly object _value;

        public static readonly BodySubstitution ThrowingBody = new(Throw);
        public static readonly BodySubstitution EmptyBody = new(null);

        private BodySubstitution(object value)
        {
            _value = value;
        }

        public object Value
        {
            get
            {
                Debug.Assert(_value != Throw);
                return _value;
            }
        }

        public static BodySubstitution Create(object value) => new(value);

        public MethodIL EmitIL(MethodDesc method)
        {
            byte[] ilBytes;
            if (_value == Throw)
            {
                ilBytes = [(byte)ILOpcode.ldnull, (byte)ILOpcode.throw_];
            }
            else if (_value is null)
            {
                Debug.Assert(method.Signature.ReturnType.IsVoid);
                ilBytes = [(byte)ILOpcode.ret];
            }
            else
            {
                Debug.Assert(_value is int);
                int value = (int)_value;
                ilBytes = value switch
                {
                    -1 => [(byte)ILOpcode.ldc_i4_m1, (byte)ILOpcode.ret],
                    0 => [(byte)ILOpcode.ldc_i4_0, (byte)ILOpcode.ret],
                    1 => [(byte)ILOpcode.ldc_i4_1, (byte)ILOpcode.ret],
                    2 => [(byte)ILOpcode.ldc_i4_2, (byte)ILOpcode.ret],
                    3 => [(byte)ILOpcode.ldc_i4_3, (byte)ILOpcode.ret],
                    4 => [(byte)ILOpcode.ldc_i4_4, (byte)ILOpcode.ret],
                    5 => [(byte)ILOpcode.ldc_i4_5, (byte)ILOpcode.ret],
                    6 => [(byte)ILOpcode.ldc_i4_6, (byte)ILOpcode.ret],
                    7 => [(byte)ILOpcode.ldc_i4_7, (byte)ILOpcode.ret],
                    8 => [(byte)ILOpcode.ldc_i4_8, (byte)ILOpcode.ret],
                    >= sbyte.MinValue and <= sbyte.MaxValue =>
                    [
                        (byte)ILOpcode.ldc_i4_s,
                        unchecked((byte)(sbyte)value),
                        (byte)ILOpcode.ret,
                    ],
                    _ =>
                    [
                        (byte)ILOpcode.ldc_i4,
                        (byte)value,
                        (byte)(value >> 8),
                        (byte)(value >> 16),
                        (byte)(value >> 24),
                        (byte)ILOpcode.ret,
                    ],
                };
            }

            return new SubstitutionMethodIL(method, ilBytes);
        }

        private sealed class SubstitutionMethodIL : MethodIL
        {
            private readonly MethodDesc _method;
            private readonly byte[] _ilBytes;

            public SubstitutionMethodIL(MethodDesc method, byte[] ilBytes)
            {
                _method = method;
                _ilBytes = ilBytes;
            }

            public override MethodDesc OwningMethod => _method;
            public override int MaxStack => 1;
            public override bool IsInitLocals => false;
            public override byte[] GetILBytes() => _ilBytes;
            public override LocalVariableDefinition[] GetLocals() => Array.Empty<LocalVariableDefinition>();
            public override ILExceptionRegion[] GetExceptionRegions() => Array.Empty<ILExceptionRegion>();
            public override object GetObject(int token, NotFoundBehavior notFoundBehavior = NotFoundBehavior.Throw) =>
                throw new InvalidOperationException();
        }
    }
}

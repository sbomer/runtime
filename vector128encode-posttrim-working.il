
  .method private hidebysig static void  Vector128Encode<(class System.Buffers.Text.Base64Helper/IBase64Encoder`1<!!T>) TBase64Encoder,valuetype .ctor (class System.ValueType modreq(System.Runtime.InteropServices.UnmanagedType)) T>(!!TBase64Encoder encoder,
                                                                                                                                                                                                                                        uint8*& srcBytes,
                                                                                                                                                                                                                                        !!T*& destBytes,
                                                                                                                                                                                                                                        uint8* srcEnd,
                                                                                                                                                                                                                                        int32 sourceLength,
                                                                                                                                                                                                                                        int32 destLength,
                                                                                                                                                                                                                                        uint8* srcStart,
                                                                                                                                                                                                                                        !!T* destStart) cil managed aggressiveinlining
  {
    .custom instance void System.Runtime.CompilerServices.CompExactlyDependsOnAttribute::.ctor(class System.Type) = ( 01 00 23 53 79 73 74 65 6D 2E 52 75 6E 74 69 6D   // ..#System.Runtim
                                                                                                                      65 2E 49 6E 74 72 69 6E 73 69 63 73 2E 58 38 36   // e.Intrinsics.X86
                                                                                                                      2E 53 73 73 65 33 00 00 )                         // .Ssse3..
    .custom instance void System.Runtime.CompilerServices.CompExactlyDependsOnAttribute::.ctor(class System.Type) = ( 01 00 2B 53 79 73 74 65 6D 2E 52 75 6E 74 69 6D   // ..+System.Runtim
                                                                                                                      65 2E 49 6E 74 72 69 6E 73 69 63 73 2E 41 72 6D   // e.Intrinsics.Arm
                                                                                                                      2E 41 64 76 53 69 6D 64 2B 41 72 6D 36 34 00 00 ) // .AdvSimd+Arm64..
    .param type T 
      .custom instance void System.Runtime.CompilerServices.IsUnmanagedAttribute::.ctor() = ( 01 00 00 00 ) 
    // Code size       501 (0x1f5)
    .maxstack  5
    .locals (valuetype System.Runtime.Intrinsics.Vector128`1<uint8> V_0,
             valuetype System.Runtime.Intrinsics.Vector128`1<uint8> V_1,
             valuetype System.Runtime.Intrinsics.Vector128`1<uint8> V_2,
             valuetype System.Runtime.Intrinsics.Vector128`1<uint8> V_3,
             valuetype System.Runtime.Intrinsics.Vector128`1<uint16> V_4,
             valuetype System.Runtime.Intrinsics.Vector128`1<int16> V_5,
             valuetype System.Runtime.Intrinsics.Vector128`1<uint8> V_6,
             valuetype System.Runtime.Intrinsics.Vector128`1<int8> V_7,
             valuetype System.Runtime.Intrinsics.Vector128`1<uint8> V_8,
             uint8* V_9,
             !!T* V_10,
             valuetype System.Runtime.Intrinsics.Vector128`1<uint8> V_11,
             valuetype System.Runtime.Intrinsics.Vector128`1<uint8> V_12,
             valuetype System.Runtime.Intrinsics.Vector128`1<uint8> V_13,
             valuetype System.Runtime.Intrinsics.Vector128`1<uint16> V_14,
             valuetype System.Runtime.Intrinsics.Vector128`1<int16> V_15,
             valuetype System.Runtime.Intrinsics.Vector128`1<uint8> V_16,
             valuetype System.Runtime.Intrinsics.Vector128`1<int8> V_17,
             valuetype System.Runtime.Intrinsics.Vector128`1<int8> V_18,
             bool V_19,
             bool V_20,
             object V_21,
             object V_22,
             bool V_23,
             bool V_24,
             bool V_25)
    IL_0000:  nop
    IL_0001:  ldc.i4     0x1020001
    IL_0006:  ldc.i4     0x4050304
    IL_000b:  ldc.i4     0x7080607
    IL_0010:  ldc.i4     0xa0b090a
    IL_0015:  call       valuetype System.Runtime.Intrinsics.Vector128`1<int32> System.Runtime.Intrinsics.Vector128::Create(int32,
                                                                                                                            int32,
                                                                                                                            int32,
                                                                                                                            int32)
    IL_001a:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::AsByte<int32>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_001f:  stloc.0
    IL_0020:  ldc.i4     0xfcfc4741
    IL_0025:  ldc.i4     0xfcfcfcfc
    IL_002a:  ldc.i4     0xfcfcfcfc
    IL_002f:  ldarga.s   encoder
    IL_0031:  constrained. !!TBase64Encoder
    IL_0037:  callvirt   instance uint32 class System.Buffers.Text.Base64Helper/IBase64Encoder`1<!!T>::get_Ssse3AdvSimdLutE3()
    IL_003c:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint32> System.Runtime.Intrinsics.Vector128::Create(uint32,
                                                                                                                             uint32,
                                                                                                                             uint32,
                                                                                                                             uint32)
    IL_0041:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::AsByte<uint32>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0046:  stloc.1
    IL_0047:  ldc.i4     0xfc0fc00
    IL_004c:  call       valuetype System.Runtime.Intrinsics.Vector128`1<int32> System.Runtime.Intrinsics.Vector128::Create(int32)
    IL_0051:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::AsByte<int32>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0056:  stloc.2
    IL_0057:  ldc.i4     0x3f03f0
    IL_005c:  call       valuetype System.Runtime.Intrinsics.Vector128`1<int32> System.Runtime.Intrinsics.Vector128::Create(int32)
    IL_0061:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::AsByte<int32>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0066:  stloc.3
    IL_0067:  ldc.i4     0x4000040
    IL_006c:  call       valuetype System.Runtime.Intrinsics.Vector128`1<int32> System.Runtime.Intrinsics.Vector128::Create(int32)
    IL_0071:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint16> System.Runtime.Intrinsics.Vector128::AsUInt16<int32>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0076:  stloc.s    V_4
    IL_0078:  ldc.i4     0x1000010
    IL_007d:  call       valuetype System.Runtime.Intrinsics.Vector128`1<int32> System.Runtime.Intrinsics.Vector128::Create(int32)
    IL_0082:  call       valuetype System.Runtime.Intrinsics.Vector128`1<int16> System.Runtime.Intrinsics.Vector128::AsInt16<int32>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0087:  stloc.s    V_5
    IL_0089:  ldc.i4.s   51
    IL_008b:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::Create(uint8)
    IL_0090:  stloc.s    V_6
    IL_0092:  ldc.i4.s   25
    IL_0094:  call       valuetype System.Runtime.Intrinsics.Vector128`1<int8> System.Runtime.Intrinsics.Vector128::Create(int8)
    IL_0099:  stloc.s    V_7
    IL_009b:  ldc.i4     0x8f
    IL_00a0:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::Create(uint8)
    IL_00a5:  stloc.s    V_8
    IL_00a7:  ldarg.1
    IL_00a8:  ldind.i
    IL_00a9:  stloc.s    V_9
    IL_00ab:  ldarg.2
    IL_00ac:  ldind.i
    IL_00ad:  stloc.s    V_10
    IL_00af:  nop
    IL_00b0:  ldloc.s    V_9
    IL_00b2:  ldarg.s    srcStart
    IL_00b4:  ldarg.s    sourceLength
    IL_00b6:  call       void System.Buffers.Text.Base64Helper::AssertRead<valuetype System.Runtime.Intrinsics.Vector128`1<int8>>(uint8*,
                                                                                                                                  uint8*,
                                                                                                                                  int32)
    IL_00bb:  nop
    IL_00bc:  ldloc.s    V_9
    IL_00be:  call       valuetype System.Runtime.Intrinsics.Vector128`1<!!0> System.Runtime.Intrinsics.Vector128::LoadUnsafe<uint8>(!!0&)
    IL_00c3:  stloc.s    V_11
    IL_00c5:  ldloc.s    V_11
    IL_00c7:  ldloc.0
    IL_00c8:  ldloc.s    V_8
    IL_00ca:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Buffers.Text.Base64Helper::SimdShuffle(valuetype System.Runtime.Intrinsics.Vector128`1<uint8>,
                                                                                                                              valuetype System.Runtime.Intrinsics.Vector128`1<uint8>,
                                                                                                                              valuetype System.Runtime.Intrinsics.Vector128`1<uint8>)
    IL_00cf:  stloc.s    V_11
    IL_00d1:  ldloc.s    V_11
    IL_00d3:  ldloc.2
    IL_00d4:  call       valuetype System.Runtime.Intrinsics.Vector128`1<!0> valuetype System.Runtime.Intrinsics.Vector128`1<uint8>::op_BitwiseAnd(valuetype System.Runtime.Intrinsics.Vector128`1<!0>,
                                                                                                                                                   valuetype System.Runtime.Intrinsics.Vector128`1<!0>)
    IL_00d9:  stloc.s    V_12
    IL_00db:  ldloc.s    V_11
    IL_00dd:  ldloc.3
    IL_00de:  call       valuetype System.Runtime.Intrinsics.Vector128`1<!0> valuetype System.Runtime.Intrinsics.Vector128`1<uint8>::op_BitwiseAnd(valuetype System.Runtime.Intrinsics.Vector128`1<!0>,
                                                                                                                                                   valuetype System.Runtime.Intrinsics.Vector128`1<!0>)
    IL_00e3:  stloc.s    V_13
    IL_00e5:  call       bool System.Runtime.Intrinsics.X86.Ssse3::get_IsSupported()
    IL_00ea:  stloc.s    V_19
    IL_00ec:  ldloc.s    V_19
    IL_00ee:  brfalse.s  IL_0104

    IL_00f0:  nop
    IL_00f1:  ldloc.s    V_12
    IL_00f3:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint16> System.Runtime.Intrinsics.Vector128::AsUInt16<uint8>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_00f8:  ldloc.s    V_4
    IL_00fa:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint16> System.Runtime.Intrinsics.X86.Sse2::MultiplyHigh(valuetype System.Runtime.Intrinsics.Vector128`1<uint16>,
                                                                                                                                  valuetype System.Runtime.Intrinsics.Vector128`1<uint16>)
    IL_00ff:  stloc.s    V_14
    IL_0101:  nop
    IL_0102:  br.s       IL_011b

    IL_0104:  ldc.i4.0
    IL_0105:  stloc.s    V_20
    IL_0107:  ldloc.s    V_20
    IL_0109:  brfalse.s  IL_010b

    IL_010b:  nop
    IL_010c:  call       void System.Buffers.Text.Base64Helper::ThrowUnreachableException()
    IL_0111:  nop
    IL_0112:  ldloca.s   V_14
    IL_0114:  initobj    valuetype System.Runtime.Intrinsics.Vector128`1<uint16>
    IL_011a:  nop
    IL_011b:  ldloc.s    V_13
    IL_011d:  call       valuetype System.Runtime.Intrinsics.Vector128`1<int16> System.Runtime.Intrinsics.Vector128::AsInt16<uint8>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0122:  ldloc.s    V_5
    IL_0124:  call       valuetype System.Runtime.Intrinsics.Vector128`1<!0> valuetype System.Runtime.Intrinsics.Vector128`1<int16>::op_Multiply(valuetype System.Runtime.Intrinsics.Vector128`1<!0>,
                                                                                                                                                 valuetype System.Runtime.Intrinsics.Vector128`1<!0>)
    IL_0129:  stloc.s    V_15
    IL_012b:  ldloc.s    V_14
    IL_012d:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::AsByte<uint16>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0132:  ldloc.s    V_15
    IL_0134:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::AsByte<int16>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0139:  call       valuetype System.Runtime.Intrinsics.Vector128`1<!0> valuetype System.Runtime.Intrinsics.Vector128`1<uint8>::op_BitwiseOr(valuetype System.Runtime.Intrinsics.Vector128`1<!0>,
                                                                                                                                                  valuetype System.Runtime.Intrinsics.Vector128`1<!0>)
    IL_013e:  stloc.s    V_11
    IL_0140:  call       bool System.Runtime.Intrinsics.X86.Ssse3::get_IsSupported()
    IL_0145:  stloc.s    V_23
    IL_0147:  ldloc.s    V_23
    IL_0149:  brfalse.s  IL_015f

    IL_014b:  nop
    IL_014c:  ldloc.s    V_11
    IL_014e:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::AsByte<uint8>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0153:  ldloc.s    V_6
    IL_0155:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.X86.Sse2::SubtractSaturate(valuetype System.Runtime.Intrinsics.Vector128`1<uint8>,
                                                                                                                                     valuetype System.Runtime.Intrinsics.Vector128`1<uint8>)
    IL_015a:  stloc.s    V_16
    IL_015c:  nop
    IL_015d:  br.s       IL_0176

    IL_015f:  ldc.i4.0
    IL_0160:  stloc.s    V_24
    IL_0162:  ldloc.s    V_24
    IL_0164:  brfalse.s  IL_0166

    IL_0166:  nop
    IL_0167:  call       void System.Buffers.Text.Base64Helper::ThrowUnreachableException()
    IL_016c:  nop
    IL_016d:  ldloca.s   V_16
    IL_016f:  initobj    valuetype System.Runtime.Intrinsics.Vector128`1<uint8>
    IL_0175:  nop
    IL_0176:  ldloc.s    V_11
    IL_0178:  call       valuetype System.Runtime.Intrinsics.Vector128`1<int8> System.Runtime.Intrinsics.Vector128::AsSByte<uint8>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_017d:  ldloc.s    V_7
    IL_017f:  call       valuetype System.Runtime.Intrinsics.Vector128`1<!!0> System.Runtime.Intrinsics.Vector128::GreaterThan<int8>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>,
                                                                                                                                     valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0184:  stloc.s    V_17
    IL_0186:  ldloc.s    V_16
    IL_0188:  call       valuetype System.Runtime.Intrinsics.Vector128`1<int8> System.Runtime.Intrinsics.Vector128::AsSByte<uint8>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_018d:  ldloc.s    V_17
    IL_018f:  call       valuetype System.Runtime.Intrinsics.Vector128`1<!0> valuetype System.Runtime.Intrinsics.Vector128`1<int8>::op_Subtraction(valuetype System.Runtime.Intrinsics.Vector128`1<!0>,
                                                                                                                                                   valuetype System.Runtime.Intrinsics.Vector128`1<!0>)
    IL_0194:  stloc.s    V_18
    IL_0196:  ldloc.s    V_11
    IL_0198:  ldloc.1
    IL_0199:  ldloc.s    V_18
    IL_019b:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::AsByte<int8>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_01a0:  ldloc.s    V_8
    IL_01a2:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Buffers.Text.Base64Helper::SimdShuffle(valuetype System.Runtime.Intrinsics.Vector128`1<uint8>,
                                                                                                                              valuetype System.Runtime.Intrinsics.Vector128`1<uint8>,
                                                                                                                              valuetype System.Runtime.Intrinsics.Vector128`1<uint8>)
    IL_01a7:  call       valuetype System.Runtime.Intrinsics.Vector128`1<!0> valuetype System.Runtime.Intrinsics.Vector128`1<uint8>::op_Addition(valuetype System.Runtime.Intrinsics.Vector128`1<!0>,
                                                                                                                                                 valuetype System.Runtime.Intrinsics.Vector128`1<!0>)
    IL_01ac:  stloc.s    V_11
    IL_01ae:  ldarga.s   encoder
    IL_01b0:  ldloc.s    V_10
    IL_01b2:  ldarg.s    destStart
    IL_01b4:  ldarg.s    destLength
    IL_01b6:  ldloc.s    V_11
    IL_01b8:  constrained. !!TBase64Encoder
    IL_01be:  callvirt   instance void class System.Buffers.Text.Base64Helper/IBase64Encoder`1<!!T>::StoreVector128ToDestination(!0*,
                                                                                                                                 !0*,
                                                                                                                                 int32,
                                                                                                                                 valuetype System.Runtime.Intrinsics.Vector128`1<uint8>)
    IL_01c3:  nop
    IL_01c4:  ldloc.s    V_9
    IL_01c6:  ldc.i4.s   12
    IL_01c8:  add
    IL_01c9:  stloc.s    V_9
    IL_01cb:  ldloc.s    V_10
    IL_01cd:  ldc.i4.s   16
    IL_01cf:  conv.i
    IL_01d0:  sizeof     !!T
    IL_01d6:  mul
    IL_01d7:  add
    IL_01d8:  stloc.s    V_10
    IL_01da:  nop
    IL_01db:  ldloc.s    V_9
    IL_01dd:  ldarg.3
    IL_01de:  cgt.un
    IL_01e0:  ldc.i4.0
    IL_01e1:  ceq
    IL_01e3:  stloc.s    V_25
    IL_01e5:  ldloc.s    V_25
    IL_01e7:  brtrue     IL_00af

    IL_01ec:  ldarg.1
    IL_01ed:  ldloc.s    V_9
    IL_01ef:  stind.i
    IL_01f0:  ldarg.2
    IL_01f1:  ldloc.s    V_10
    IL_01f3:  stind.i
    IL_01f4:  ret
  } // end of method Base64Helper::Vector128Encode
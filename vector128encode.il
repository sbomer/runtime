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
    // Code size       547 (0x223)
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
             valuetype System.Runtime.Intrinsics.Vector128`1<uint16> V_13,
             valuetype System.Runtime.Intrinsics.Vector128`1<int16> V_14,
             valuetype System.Runtime.Intrinsics.Vector128`1<uint8> V_15,
             valuetype System.Runtime.Intrinsics.Vector128`1<int8> V_16,
             valuetype System.Runtime.Intrinsics.Vector128`1<int8> V_17,
             valuetype System.Runtime.Intrinsics.Vector128`1<uint16> V_18)
    IL_0000:  ldc.i4     0x1020001
    IL_0005:  ldc.i4     0x4050304
    IL_000a:  ldc.i4     0x7080607
    IL_000f:  ldc.i4     0xa0b090a
    IL_0014:  call       valuetype System.Runtime.Intrinsics.Vector128`1<int32> System.Runtime.Intrinsics.Vector128::Create(int32,
                                                                                                                            int32,
                                                                                                                            int32,
                                                                                                                            int32)
    IL_0019:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::AsByte<int32>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_001e:  stloc.0
    IL_001f:  ldc.i4     0xfcfc4741
    IL_0024:  ldc.i4     0xfcfcfcfc
    IL_0029:  ldc.i4     0xfcfcfcfc
    IL_002e:  ldarga.s   encoder
    IL_0030:  constrained. !!TBase64Encoder
    IL_0036:  callvirt   instance uint32 class System.Buffers.Text.Base64Helper/IBase64Encoder`1<!!T>::get_Ssse3AdvSimdLutE3()
    IL_003b:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint32> System.Runtime.Intrinsics.Vector128::Create(uint32,
                                                                                                                             uint32,
                                                                                                                             uint32,
                                                                                                                             uint32)
    IL_0040:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::AsByte<uint32>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0045:  stloc.1
    IL_0046:  ldc.i4     0xfc0fc00
    IL_004b:  call       valuetype System.Runtime.Intrinsics.Vector128`1<int32> System.Runtime.Intrinsics.Vector128::Create(int32)
    IL_0050:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::AsByte<int32>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0055:  stloc.2
    IL_0056:  ldc.i4     0x3f03f0
    IL_005b:  call       valuetype System.Runtime.Intrinsics.Vector128`1<int32> System.Runtime.Intrinsics.Vector128::Create(int32)
    IL_0060:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::AsByte<int32>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0065:  stloc.3
    IL_0066:  ldc.i4     0x4000040
    IL_006b:  call       valuetype System.Runtime.Intrinsics.Vector128`1<int32> System.Runtime.Intrinsics.Vector128::Create(int32)
    IL_0070:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint16> System.Runtime.Intrinsics.Vector128::AsUInt16<int32>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0075:  stloc.s    V_4
    IL_0077:  ldc.i4     0x1000010
    IL_007c:  call       valuetype System.Runtime.Intrinsics.Vector128`1<int32> System.Runtime.Intrinsics.Vector128::Create(int32)
    IL_0081:  call       valuetype System.Runtime.Intrinsics.Vector128`1<int16> System.Runtime.Intrinsics.Vector128::AsInt16<int32>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0086:  stloc.s    V_5
    IL_0088:  ldc.i4.s   51
    IL_008a:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::Create(uint8)
    IL_008f:  stloc.s    V_6
    IL_0091:  ldc.i4.s   25
    IL_0093:  call       valuetype System.Runtime.Intrinsics.Vector128`1<int8> System.Runtime.Intrinsics.Vector128::Create(int8)
    IL_0098:  stloc.s    V_7
    IL_009a:  ldc.i4     0x8f
    IL_009f:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::Create(uint8)
    IL_00a4:  stloc.s    V_8
    IL_00a6:  ldarg.1
    IL_00a7:  ldind.i
    IL_00a8:  stloc.s    V_9
    IL_00aa:  ldarg.2
    IL_00ab:  ldind.i
    IL_00ac:  stloc.s    V_10
    IL_00ae:  ldloc.s    V_9
    IL_00b0:  ldarg.s    srcStart
    IL_00b2:  ldarg.s    sourceLength
    IL_00b4:  call       void System.Buffers.Text.Base64Helper::AssertRead<valuetype System.Runtime.Intrinsics.Vector128`1<int8>>(uint8*,
                                                                                                                                  uint8*,
                                                                                                                                  int32)
    IL_00b9:  ldloc.s    V_9
    IL_00bb:  call       valuetype System.Runtime.Intrinsics.Vector128`1<!!0> System.Runtime.Intrinsics.Vector128::LoadUnsafe<uint8>(!!0&)
    IL_00c0:  stloc.s    V_11
    IL_00c2:  ldloc.s    V_11
    IL_00c4:  ldloc.0
    IL_00c5:  ldloc.s    V_8
    IL_00c7:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Buffers.Text.Base64Helper::SimdShuffle(valuetype System.Runtime.Intrinsics.Vector128`1<uint8>,
                                                                                                                              valuetype System.Runtime.Intrinsics.Vector128`1<uint8>,
                                                                                                                              valuetype System.Runtime.Intrinsics.Vector128`1<uint8>)
    IL_00cc:  stloc.s    V_11
    IL_00ce:  ldloc.s    V_11
    IL_00d0:  ldloc.2
    IL_00d1:  call       valuetype System.Runtime.Intrinsics.Vector128`1<!0> valuetype System.Runtime.Intrinsics.Vector128`1<uint8>::op_BitwiseAnd(valuetype System.Runtime.Intrinsics.Vector128`1<!0>,
                                                                                                                                                   valuetype System.Runtime.Intrinsics.Vector128`1<!0>)
    IL_00d6:  stloc.s    V_12
    IL_00d8:  ldloc.s    V_11
    IL_00da:  ldloc.3
    IL_00db:  call       valuetype System.Runtime.Intrinsics.Vector128`1<!0> valuetype System.Runtime.Intrinsics.Vector128`1<uint8>::op_BitwiseAnd(valuetype System.Runtime.Intrinsics.Vector128`1<!0>,
                                                                                                                                                   valuetype System.Runtime.Intrinsics.Vector128`1<!0>)
    IL_00e0:  call       bool System.Runtime.Intrinsics.X86.Ssse3::get_IsSupported()
    IL_00e5:  brfalse.s  IL_00f9

    IL_00e7:  ldloc.s    V_12
    IL_00e9:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint16> System.Runtime.Intrinsics.Vector128::AsUInt16<uint8>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_00ee:  ldloc.s    V_4
    IL_00f0:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint16> System.Runtime.Intrinsics.X86.Sse2::MultiplyHigh(valuetype System.Runtime.Intrinsics.Vector128`1<uint16>,
                                                                                                                                  valuetype System.Runtime.Intrinsics.Vector128`1<uint16>)
    IL_00f5:  stloc.s    V_13
    IL_00f7:  br.s       IL_014d

    IL_00f9:  call       bool System.Runtime.Intrinsics.Arm.AdvSimd/Arm64::get_IsSupported()
    IL_00fe:  brfalse.s  IL_0140

    IL_0100:  ldloc.s    V_12
    IL_0102:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint16> System.Runtime.Intrinsics.Vector128::AsUInt16<uint8>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0107:  ldloc.s    V_12
    IL_0109:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint16> System.Runtime.Intrinsics.Vector128::AsUInt16<uint8>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_010e:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint16> System.Runtime.Intrinsics.Arm.AdvSimd/Arm64::UnzipOdd(valuetype System.Runtime.Intrinsics.Vector128`1<uint16>,
                                                                                                                                       valuetype System.Runtime.Intrinsics.Vector128`1<uint16>)
    IL_0113:  ldc.i4.6
    IL_0114:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint16> System.Runtime.Intrinsics.Vector128::ShiftRightLogical(valuetype System.Runtime.Intrinsics.Vector128`1<uint16>,
                                                                                                                                        int32)
    IL_0119:  stloc.s    V_18
    IL_011b:  ldloc.s    V_12
    IL_011d:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint16> System.Runtime.Intrinsics.Vector128::AsUInt16<uint8>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0122:  ldloc.s    V_12
    IL_0124:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint16> System.Runtime.Intrinsics.Vector128::AsUInt16<uint8>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0129:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint16> System.Runtime.Intrinsics.Arm.AdvSimd/Arm64::UnzipEven(valuetype System.Runtime.Intrinsics.Vector128`1<uint16>,
                                                                                                                                        valuetype System.Runtime.Intrinsics.Vector128`1<uint16>)
    IL_012e:  ldc.i4.s   10
    IL_0130:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint16> System.Runtime.Intrinsics.Vector128::ShiftRightLogical(valuetype System.Runtime.Intrinsics.Vector128`1<uint16>,
                                                                                                                                        int32)
    IL_0135:  ldloc.s    V_18
    IL_0137:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint16> System.Runtime.Intrinsics.Arm.AdvSimd/Arm64::ZipLow(valuetype System.Runtime.Intrinsics.Vector128`1<uint16>,
                                                                                                                                     valuetype System.Runtime.Intrinsics.Vector128`1<uint16>)
    IL_013c:  stloc.s    V_13
    IL_013e:  br.s       IL_014d

    IL_0140:  call       void System.Buffers.Text.Base64Helper::ThrowUnreachableException()
    IL_0145:  ldloca.s   V_13
    IL_0147:  initobj    valuetype System.Runtime.Intrinsics.Vector128`1<uint16>
    IL_014d:  call       valuetype System.Runtime.Intrinsics.Vector128`1<int16> System.Runtime.Intrinsics.Vector128::AsInt16<uint8>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0152:  ldloc.s    V_5
    IL_0154:  call       valuetype System.Runtime.Intrinsics.Vector128`1<!0> valuetype System.Runtime.Intrinsics.Vector128`1<int16>::op_Multiply(valuetype System.Runtime.Intrinsics.Vector128`1<!0>,
                                                                                                                                                 valuetype System.Runtime.Intrinsics.Vector128`1<!0>)
    IL_0159:  stloc.s    V_14
    IL_015b:  ldloc.s    V_13
    IL_015d:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::AsByte<uint16>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0162:  ldloc.s    V_14
    IL_0164:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::AsByte<int16>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0169:  call       valuetype System.Runtime.Intrinsics.Vector128`1<!0> valuetype System.Runtime.Intrinsics.Vector128`1<uint8>::op_BitwiseOr(valuetype System.Runtime.Intrinsics.Vector128`1<!0>,
                                                                                                                                                  valuetype System.Runtime.Intrinsics.Vector128`1<!0>)
    IL_016e:  stloc.s    V_11
    IL_0170:  call       bool System.Runtime.Intrinsics.X86.Ssse3::get_IsSupported()
    IL_0175:  brfalse.s  IL_0189

    IL_0177:  ldloc.s    V_11
    IL_0179:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::AsByte<uint8>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_017e:  ldloc.s    V_6
    IL_0180:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.X86.Sse2::SubtractSaturate(valuetype System.Runtime.Intrinsics.Vector128`1<uint8>,
                                                                                                                                     valuetype System.Runtime.Intrinsics.Vector128`1<uint8>)
    IL_0185:  stloc.s    V_15
    IL_0187:  br.s       IL_01af

    IL_0189:  call       bool System.Runtime.Intrinsics.Arm.AdvSimd::get_IsSupported()
    IL_018e:  brfalse.s  IL_01a2

    IL_0190:  ldloc.s    V_11
    IL_0192:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::AsByte<uint8>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_0197:  ldloc.s    V_6
    IL_0199:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Arm.AdvSimd::SubtractSaturate(valuetype System.Runtime.Intrinsics.Vector128`1<uint8>,
                                                                                                                                        valuetype System.Runtime.Intrinsics.Vector128`1<uint8>)
    IL_019e:  stloc.s    V_15
    IL_01a0:  br.s       IL_01af

    IL_01a2:  call       void System.Buffers.Text.Base64Helper::ThrowUnreachableException()
    IL_01a7:  ldloca.s   V_15
    IL_01a9:  initobj    valuetype System.Runtime.Intrinsics.Vector128`1<uint8>
    IL_01af:  ldloc.s    V_11
    IL_01b1:  call       valuetype System.Runtime.Intrinsics.Vector128`1<int8> System.Runtime.Intrinsics.Vector128::AsSByte<uint8>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_01b6:  ldloc.s    V_7
    IL_01b8:  call       valuetype System.Runtime.Intrinsics.Vector128`1<!!0> System.Runtime.Intrinsics.Vector128::GreaterThan<int8>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>,
                                                                                                                                     valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_01bd:  stloc.s    V_16
    IL_01bf:  ldloc.s    V_15
    IL_01c1:  call       valuetype System.Runtime.Intrinsics.Vector128`1<int8> System.Runtime.Intrinsics.Vector128::AsSByte<uint8>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_01c6:  ldloc.s    V_16
    IL_01c8:  call       valuetype System.Runtime.Intrinsics.Vector128`1<!0> valuetype System.Runtime.Intrinsics.Vector128`1<int8>::op_Subtraction(valuetype System.Runtime.Intrinsics.Vector128`1<!0>,
                                                                                                                                                   valuetype System.Runtime.Intrinsics.Vector128`1<!0>)
    IL_01cd:  stloc.s    V_17
    IL_01cf:  ldloc.s    V_11
    IL_01d1:  ldloc.1
    IL_01d2:  ldloc.s    V_17
    IL_01d4:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Runtime.Intrinsics.Vector128::AsByte<int8>(valuetype System.Runtime.Intrinsics.Vector128`1<!!0>)
    IL_01d9:  ldloc.s    V_8
    IL_01db:  call       valuetype System.Runtime.Intrinsics.Vector128`1<uint8> System.Buffers.Text.Base64Helper::SimdShuffle(valuetype System.Runtime.Intrinsics.Vector128`1<uint8>,
                                                                                                                              valuetype System.Runtime.Intrinsics.Vector128`1<uint8>,
                                                                                                                              valuetype System.Runtime.Intrinsics.Vector128`1<uint8>)
    IL_01e0:  call       valuetype System.Runtime.Intrinsics.Vector128`1<!0> valuetype System.Runtime.Intrinsics.Vector128`1<uint8>::op_Addition(valuetype System.Runtime.Intrinsics.Vector128`1<!0>,
                                                                                                                                                 valuetype System.Runtime.Intrinsics.Vector128`1<!0>)
    IL_01e5:  stloc.s    V_11
    IL_01e7:  ldarga.s   encoder
    IL_01e9:  ldloc.s    V_10
    IL_01eb:  ldarg.s    destStart
    IL_01ed:  ldarg.s    destLength
    IL_01ef:  ldloc.s    V_11
    IL_01f1:  constrained. !!TBase64Encoder
    IL_01f7:  callvirt   instance void class System.Buffers.Text.Base64Helper/IBase64Encoder`1<!!T>::StoreVector128ToDestination(!0*,
                                                                                                                                 !0*,
                                                                                                                                 int32,
                                                                                                                                 valuetype System.Runtime.Intrinsics.Vector128`1<uint8>)
    IL_01fc:  ldloc.s    V_9
    IL_01fe:  ldc.i4.s   12
    IL_0200:  add
    IL_0201:  stloc.s    V_9
    IL_0203:  ldloc.s    V_10
    IL_0205:  ldc.i4.s   16
    IL_0207:  conv.i
    IL_0208:  sizeof     !!T
    IL_020e:  mul
    IL_020f:  add
    IL_0210:  stloc.s    V_10
    IL_0212:  ldloc.s    V_9
    IL_0214:  ldarg.3
    IL_0215:  ble.un     IL_00ae

    IL_021a:  ldarg.1
    IL_021b:  ldloc.s    V_9
    IL_021d:  stind.i
    IL_021e:  ldarg.2
    IL_021f:  ldloc.s    V_10
    IL_0221:  stind.i
    IL_0222:  ret
  } // end of method Base64Helper::Vector128Encode
  
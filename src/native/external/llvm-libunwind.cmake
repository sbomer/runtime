# IMPORTANT: do not use add_compile_options(), add_definitions() or similar functions here since it will leak to the including projects

if(NOT CLR_CMAKE_TARGET_ARCH_WASM)
  include_directories(BEFORE "${CMAKE_CURRENT_LIST_DIR}/llvm-libunwind/include")
endif()

# Only Unwind-EHABI.cpp is needed (for _Unwind_VRS_Interpret on ARM32).
# libunwind.cpp provides the public unw_* C API which the NativeAOT runtime
# does not use — UnwindHelpers.cpp consumes the internal C++ template API
# directly. Excluding libunwind.cpp avoids exporting duplicate unw_*/
# _Unwind_* symbols that conflict with the platform's copy of libunwind
# (e.g. when linking with libc++_static.a on Android).
set (LLVM_LIBUNWIND_SOURCES_BASE
    src/Unwind-EHABI.cpp
)

set(LLVM_LIBUNWIND_ASM_SOURCES_BASE
    src/UnwindRegistersRestore.S
    src/UnwindRegistersSave.S
)

addprefix(LLVM_LIBUNWIND_SOURCES "${CMAKE_CURRENT_LIST_DIR}/llvm-libunwind" "${LLVM_LIBUNWIND_SOURCES_BASE}")
addprefix(LLVM_LIBUNWIND_ASM_SOURCES "${CMAKE_CURRENT_LIST_DIR}/llvm-libunwind" "${LLVM_LIBUNWIND_ASM_SOURCES_BASE}")

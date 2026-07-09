// Standalone config for building native C libraries via the Ninja resolver.
//
// Prerequisites: CMake must have been run first to generate build.ninja:
//   export __CMakeBinDir=$PWD/artifacts/obj/native/linux-x64-Debug/bin
//   cmake --no-warn-unused-cli -G Ninja \
//     -DCMAKE_BUILD_TYPE=Debug \
//     -DCMAKE_INSTALL_PREFIX=$__CMakeBinDir \
//     -DFEATURE_DISTRO_AGNOSTIC_SSL=1 \
//     -DCMAKE_STATIC_LIB_LINK=0 \
//     -S src/native/libs \
//     -B artifacts/obj/native/linux-x64-Debug
//
// Usage:
//   bxl /c:config.native.dsc

config({
    resolvers: [
        {
            kind: "Ninja",
            moduleName: "NativeLibs",
            root: d`artifacts/obj/native/linux-x64-Debug`,
            // Build all top-level library targets (shared + static)
            targets: [
                "System.Native",
                "System.Native-Static",
                "System.Globalization.Native",
                "System.Globalization.Native-Static",
                "System.IO.Compression.Native",
                "System.IO.Compression.Native-Static",
                "System.IO.Ports.Native",
                "System.IO.Ports.Native-Static",
                "System.Net.Security.Native",
                "System.Net.Security.Native-Static",
                "System.Security.Cryptography.Native.OpenSsl",
                "System.Security.Cryptography.Native.OpenSsl-Static",
            ],
            // Keep the generated JSON graph file for debugging
            keepProjectGraphFile: true,
            // The build commands reference system headers and tools at absolute paths.
            // Untrack these to avoid DFA violations from reads of system-provided files.
            untrackedDirectoryScopes: [
                // System headers (gcc/libc/openssl/icu)
                d`/usr/include`,
                d`/usr/lib`,
                d`/usr/lib64`,
                // GCC internal headers and libraries
                d`/usr/lib/gcc`,
                // Compiler, linker, ar, ranlib, cmake
                d`/usr/bin`,
                // Temporary files created during compilation
                d`/tmp`,
                // /proc and /sys accessed by some tools
                d`/proc`,
                d`/sys`,
                // Dynamic linker cache
                d`/etc`,
            ],
            // Additional output directories beyond the build root where outputs are written
            additionalOutputDirectories: [
                p`artifacts/obj/native/linux-x64-Debug/bin`,
            ],
        },
    ]
});

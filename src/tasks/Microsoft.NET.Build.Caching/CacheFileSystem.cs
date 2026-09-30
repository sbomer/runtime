// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Microsoft.NET.Build.Caching;

internal static partial class CacheFileSystem
{
    private const int ErrorFileExists = 80;
    private const int ErrorAlreadyExists = 183;
    private const int ErrorSharingViolation = 32;
    private const int UnixFileExists = 17;
    private const int UnixInterrupted = 4;
    private const int LinuxWouldBlock = 11;
    private const int MacOSWouldBlock = 35;
    private const int LockShared = 1;
    private const int LockExclusive = 2;
    private const int LockNonBlocking = 4;
    private const int RetryDelayMilliseconds = 25;

    internal static bool IsWindows => Path.DirectorySeparatorChar == '\\';
    private static int UnixWouldBlock => RuntimeInformation.IsOSPlatform(OSPlatform.OSX) ? MacOSWouldBlock : LinuxWouldBlock;

    // File.Move's Unix implementation may use check-then-rename, which can replace a
    // concurrent winner. link() instead publishes a complete file only if absent.
    internal static bool Publish(string temporaryPath, string destinationPath)
    {
        if (IsWindows)
        {
            if (MoveFile(temporaryPath, destinationPath))
            {
                return true;
            }

            int error = Marshal.GetLastWin32Error();
            if (error is ErrorFileExists or ErrorAlreadyExists)
            {
                return false;
            }

            throw NativeError(destinationPath, error);
        }

        if (Link(temporaryPath, destinationPath) == 0)
        {
            return true;
        }

        int unixError = Marshal.GetLastWin32Error();
        if (unixError == UnixFileExists)
        {
            return false;
        }

        throw NativeError(destinationPath, unixError);
    }

    internal static async Task<FileStream> AcquireLockAsync(string path, bool exclusive, CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            FileStream stream;
            try
            {
                // Never truncate, delete, or replace this file: all owners must lock
                // the same filesystem object, including after a process crashes.
                stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                    exclusive ? FileShare.None : FileShare.ReadWrite);
            }
            catch (IOException e) when (IsWindows ? (e.HResult & 0xffff) == ErrorSharingViolation : e.HResult == UnixWouldBlock)
            {
                await Task.Delay(RetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
                continue;
            }

            bool acquired = false;
            try
            {
                if (!IsWindows)
                {
                    // FileStream can silently ignore unsupported Unix locking. An
                    // explicit flock makes that a failure rather than false safety.
                    while (Flock(stream.SafeFileHandle, (exclusive ? LockExclusive : LockShared) | LockNonBlocking) != 0)
                    {
                        int error = Marshal.GetLastWin32Error();
                        if (error != UnixInterrupted && error != UnixWouldBlock)
                        {
                            throw NativeError(path, error);
                        }

                        await Task.Delay(RetryDelayMilliseconds, cancellationToken).ConfigureAwait(false);
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                acquired = true;
                return stream;
            }
            finally
            {
                if (!acquired)
                {
                    stream.Dispose();
                }
            }
        }
    }

    private static IOException NativeError(string path, int error) =>
        new IOException(SR.Format(SR.CacheFileSystemError, path, error), new Win32Exception(error));

#if NETFRAMEWORK
    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(string oldPath, string newPath);

    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int Flock(SafeFileHandle handle, int operation);

    [DllImport("kernel32.dll", EntryPoint = "MoveFileW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFile(string oldPath, string newPath);
#else
    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Link(string oldPath, string newPath);

    [LibraryImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static partial int Flock(SafeFileHandle handle, int operation);

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveFile(string oldPath, string newPath);
#endif
}

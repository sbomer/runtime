// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;

namespace Microsoft.NET.Build.Caching;

internal static partial class DiskCacheFileSystem
{
    private const int UnixFileExists = 17;
    private const int WindowsFileExists = 80;
    private const int WindowsAlreadyExists = 183;
    private const int WindowsMaxPath = 260;

    internal static FileStream CreatePrivateFile(string path, FileAccess access)
    {
#if NETFRAMEWORK
        return new FileStream(path, FileMode.CreateNew, access, FileShare.None, 4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
#else
        return new FileStream(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = access,
            Share = FileShare.None,
            Options = FileOptions.Asynchronous | FileOptions.SequentialScan,
            UnixCreateMode = OperatingSystem.IsWindows() ? null : UnixFileMode.UserRead | UnixFileMode.UserWrite
        });
#endif
    }

    internal static bool Publish(string temporary, string destination)
    {
        bool windows = Path.DirectorySeparatorChar == '\\';
        // File.Move can use check-then-rename on Unix and overwrite a competing winner.
        // link publishes our completed private file atomically; the caller removes the temporary name.
        bool published = windows
            ? MoveFile(GetWindowsPublicationPath(temporary), GetWindowsPublicationPath(destination), flags: 0)
            : Link(temporary, destination) == 0;
        if (published)
        {
            return true;
        }

        int error = Marshal.GetLastWin32Error();
        if (windows ? error is WindowsFileExists or WindowsAlreadyExists : error == UnixFileExists)
        {
            return false;
        }

        throw new IOException(SR.Format(SR.PublicationFailed, destination, error), new Win32Exception(error));
    }

    internal static string GetWindowsPublicationPath(string path)
    {
        // DiskCache supplies normalized absolute paths; adding a prefix disables Win32 normalization.
        if (path.Length < WindowsMaxPath ||
            path.StartsWith(@"\\?\", StringComparison.Ordinal) ||
            path.StartsWith(@"\\.\", StringComparison.Ordinal) ||
            path.StartsWith(@"\??\", StringComparison.Ordinal))
        {
            return path;
        }

        return path.StartsWith(@"\\", StringComparison.Ordinal) ? path.Insert(2, @"?\UNC\") : @"\\?\" + path;
    }

#if NETFRAMEWORK
    [DllImport("libc", EntryPoint = "link", SetLastError = true)]
    private static extern int Link(
        [MarshalAs(UnmanagedType.LPUTF8Str)] string existingPath,
        [MarshalAs(UnmanagedType.LPUTF8Str)] string newPath);

    [DllImport("kernel32.dll", EntryPoint = "MoveFileExW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MoveFile(string existingPath, string newPath, int flags);
#else
    [LibraryImport("libc", EntryPoint = "link", StringMarshalling = StringMarshalling.Utf8, SetLastError = true)]
    private static partial int Link(string existingPath, string newPath);

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveFile(string existingPath, string newPath, int flags);
#endif
}

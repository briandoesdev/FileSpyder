using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;

namespace FileSpyder.Win32
{
    public class FileApi
    {
        private const string UNC_PREFIX = @"\\?\UNC\";
        private const string FS_PREFIX = @"\\?\";
        
        public static List<FileInformation> FindFirstFileEx(
            string path,
            string searchPattern,
            bool getDirectory,
            bool recurse,
            bool parallel,
            bool suppressErrors,
            bool largeFetch
        )
        {
            object resultListLock = new object();
            Win32Native.WIN32_FIND_DATAW lpFindFileData;
            Win32Native.FINDEX_ADDITIONAL_FLAGS flags = 
                largeFetch ? 0 : Win32Native.FINDEX_ADDITIONAL_FLAGS.FindFirstExLargeFetch;

            string prefixedPath;
            if (path.StartsWith(@"\\"))
            {
                prefixedPath = path.Replace(@"\\", UNC_PREFIX);
            }
            else
            {
                prefixedPath = string.Concat(FS_PREFIX, path);
            }

            if (prefixedPath.EndsWith(@"\"))
            {
                prefixedPath = String.Concat(prefixedPath, @"*");
            }
            else
            {
                prefixedPath = String.Concat(prefixedPath, @"\*");
            }
            

            var handle = Win32Native.FindFirstFileExW(
                lpFileName: prefixedPath,
                fInfoLevelId: Win32Native.FINDEX_INFO_LEVELS.FindExInfoBasic,
                lpFindFileData: out lpFindFileData,
                fSearchOp: Win32Native.FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                lpSearchFilter: IntPtr.Zero,
                dwAdditionalFlags: flags
            );
            
            List<FileInformation> fileResults = new List<FileInformation>();
            List<FileInformation> subDirectoryList = new List<FileInformation>();
            List<FileInformation> hiddenList = new List<FileInformation>();
            
            // verify FindFirstFileEx didnt return an error
            if (!handle.IsInvalid)
            {
                do
                {
                    // skip . and .. files
                    if (lpFindFileData.cFileName != "." && lpFindFileData.cFileName != "..")
                    {
                        if (lpFindFileData.dwFileAttributes == FileAttributes.Hidden)
                        {
                            string fullName = Path.Combine(path, lpFindFileData.cFileName);
                            hiddenList.Add(new FileInformation() { Path = fullName });
                        }
                        
                        // check if we are working with a directory
                        if ((lpFindFileData.dwFileAttributes & FileAttributes.Directory) == FileAttributes.Directory)
                        {
                            if (!recurse) continue;
                            
                            string fullName = Path.Combine(path, lpFindFileData.cFileName);
                            subDirectoryList.Add(new FileInformation() { Path = fullName });
                        }
                        else
                        {
                            if (lpFindFileData.dwFileAttributes == FileAttributes.Directory && !getDirectory) continue;
                            
                            if (FileMatch(lpFindFileData.cFileName, searchPattern))
                            {
                                string fullName = Path.Combine(path, lpFindFileData.cFileName);
                                long? fileSize = null;

                                if (lpFindFileData.dwFileAttributes != FileAttributes.Directory)
                                {
                                    fileSize = (lpFindFileData.nFileSizeHigh * (2 ^ 32) + lpFindFileData.nFileSizeLow);
                                }
                                
                                fileResults.Add(new FileInformation()
                                {
                                    Name = lpFindFileData.cFileName, 
                                    Path = Path.Combine(path, lpFindFileData.cFileName), 
                                    Parent = path, 
                                    Attributes = lpFindFileData.dwFileAttributes, 
                                    FileSize = fileSize, 
                                    CreationTime = lpFindFileData.ftCreationTime.ToDateTime(), 
                                    LastAccessTime = lpFindFileData.ftLastAccessTime.ToDateTime(), 
                                    LastWriteTime = lpFindFileData.ftLastWriteTime.ToDateTime() 
                                });
                            }
                        }
                    }
                } while (Win32Native.FindNextFile(handle, out lpFindFileData));

                handle.Dispose();

                if (recurse)
                {
                    if (parallel)
                    {
                        subDirectoryList.AsParallel().ForAll(x =>
                        {
                            List<FileInformation> resultSubDirectory = FindFirstFileEx(
                                x.Path,
                                searchPattern,
                                getDirectory,
                                true,
                                false,
                                suppressErrors,
                                largeFetch
                            );

                            lock (resultListLock)
                            {
                                fileResults.AddRange(resultSubDirectory);
                            }
                        });
                    }
                    else
                    {
                        foreach (FileInformation directory in subDirectoryList)
                        {
                            var results = FindFirstFileEx(
                                directory.Path,
                                searchPattern,
                                getDirectory,
                                true,
                                false,
                                suppressErrors,
                                largeFetch
                            );
                                
                            foreach (FileInformation result in results)
                            {
                                fileResults.Add(result);
                            }
                        }
                    }
                }
            }
            else
            {
                if (!suppressErrors)
                {
                    int hr = Marshal.GetLastWin32Error();
                    if (hr != 2 && hr != 0x12)
                    {
                        throw new Win32Exception(hr);
                    }
                }
            }

            return fileResults;
        }

        private static bool FileMatch(string fileName, string searchPattern)
        {
            if (Win32Native.PathMatchSpec(fileName, searchPattern))
            {
                return true;
            }

            return false;
        }
    }
    
    internal sealed class Win32Native
    {
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern SafeFindHandle FindFirstFileExW(
            string lpFileName,
            FINDEX_INFO_LEVELS fInfoLevelId,
            out WIN32_FIND_DATAW lpFindFileData,
            FINDEX_SEARCH_OPS fSearchOp,
            IntPtr lpSearchFilter,
            FINDEX_ADDITIONAL_FLAGS dwAdditionalFlags
        );
        
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        public static extern bool FindNextFile(SafeFindHandle hFindFile, out WIN32_FIND_DATAW lpFindFileData);

        [DllImport("kernel32.dll")]
        public static extern bool FindClose(IntPtr hFindFile);
        
        [DllImport("shlwapi.dll", CharSet = CharSet.Auto)]
        public static extern bool PathMatchSpec([In] String pszFileParam, [In] String pszSpec);
        
        // Structs and enums
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct WIN32_FIND_DATAW
        {
            public FileAttributes dwFileAttributes;
            internal System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            internal System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            internal System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string cFileName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }
        
        public enum FINDEX_INFO_LEVELS
        {
            FindExInfoStandard,
            FindExInfoBasic,
            FindExInfoMaxInfoLevel
        }
        
        public enum FINDEX_SEARCH_OPS
        {
            FindExSearchNameMatch,
            FindExSearchLimitToDirectories,
            FindExSearchLimitToDevices
        }
        
        [Flags]
        public enum FINDEX_ADDITIONAL_FLAGS
        {
            FindFirstExCaseSensitive,
            FindFirstExLargeFetch
        }
    }
    
    [SecurityCritical]
    internal class SafeFindHandle : SafeHandleZeroOrMinusOneIsInvalid
    {
        [SecurityCritical]
        public SafeFindHandle() : base(true)
        { }

        [SecurityCritical]
        protected override bool ReleaseHandle()
        {
            return Win32Native.FindClose(base.handle);
        }
    }
    
    [Serializable]
    public class FileInformation
    {
        public string Name;
        public string Path;
        public string Parent;
        public FileAttributes Attributes;
        public long? FileSize;
        public DateTime CreationTime;
        public DateTime LastAccessTime;
        public DateTime LastWriteTime;
    }
    
    public static class FileTimeExtensions
    {
        public static DateTime ToDateTime(this System.Runtime.InteropServices.ComTypes.FILETIME time)
        {
            ulong high = (ulong)time.dwHighDateTime;
            ulong low = (ulong)time.dwLowDateTime;
            long fileTime = (long)((high << 32) + low);
            return DateTime.FromFileTimeUtc(fileTime);
        }
    }
}
using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;

namespace FileSpyder.Win32
{
    public class FileApi
    {
        private const string UNC_PREFIX = @"\\?\UNC\";
        private const string FS_PREFIX = @"\\?\";
        
        public static List<string> FindFirstFileEx(
            string path,
            string searchPattern,
            bool getDirectory,
            bool recurse,
            bool parallel,
            bool showErrors,
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
                prefixedPath,
                Win32Native.FINDEX_INFO_LEVELS.FindExInfoBasic,
                out lpFindFileData,
                Win32Native.FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                IntPtr.Zero,
                flags
            );
            
            List<string> fileResults = new List<string>();
            List<string> subDirectoryList = new List<string>();
            
            // verify FindFirstFileEx didnt return an error
            if (!handle.IsInvalid)
            {
                do
                {
                    // skip . and .. files
                    if (lpFindFileData.cFileName != "." && lpFindFileData.cFileName != "..")
                    {
                        // check if we are working with a directory
                        if ((lpFindFileData.dwFileAttributes & FileAttributes.Directory) == FileAttributes.Directory)
                        {
                            if (!recurse) continue;
                            
                            subDirectoryList.Add(Path.Combine(path, lpFindFileData.cFileName));
                        }
                        else
                        {
                            if (lpFindFileData.dwFileAttributes == FileAttributes.Directory && !getDirectory) continue;
                            
                            if (FileMatch(lpFindFileData.cFileName, searchPattern))
                            {
                                fileResults.Add(string.Concat(path, lpFindFileData.cFileName));
                            }
                        }
                    }
                } while (Win32Native.FindNextFile(handle, out lpFindFileData));

                handle.Dispose();

                if (recurse)
                {
                    if (parallel)
                    {
                        Parallel.ForEach(subDirectoryList, dir =>
                        {
                            List<string> resultSubDirectory = FindFirstFileEx(
                                dir,
                                searchPattern,
                                getDirectory,
                                true,
                                false,
                                showErrors,
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
                        foreach (string directory in subDirectoryList)
                        {
                            var results = FindFirstFileEx(
                                directory,
                                searchPattern,
                                getDirectory,
                                true,
                                false,
                                showErrors,
                                largeFetch
                            );
                                
                            foreach (string result in results)
                            {
                                fileResults.Add(result);
                            }
                        }
                    }
                }
            }
            else
            {
                if (showErrors)
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
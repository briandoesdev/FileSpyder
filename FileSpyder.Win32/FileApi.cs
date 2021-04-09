using Microsoft.Win32.SafeHandles;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Security;
using System.Threading.Tasks;

namespace FileSpyder.Win32
{
    public class FileApi
    {
        // One of these prefixes will be added before the directory to search. This allows us to go over the MAX_PATH
        // of the file system.
        // https://docs.microsoft.com/en-us/windows/win32/fileio/maximum-file-path-limitation
        private const string UNC_PREFIX = @"\\?\UNC\";
        private const string FS_PREFIX = @"\\?\";
        
        public static (List<string> files, List<string> errors) FindFirstFileEx(
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

            // When looking for a file we need to know if its on a local drive or network drive.
            // This will allow us to correctly prefix the path.
            // Below we check if a path starts with '\\' which signals a network path (UNC). If it does, add the
            // UNC prefix, otherwise add the FileSystem prefix
            var prefixedPath = path.StartsWith(@"\\") ? path.Replace(@"\\", UNC_PREFIX) : string.Concat(FS_PREFIX, path);
            prefixedPath = string.Concat(prefixedPath, prefixedPath.EndsWith(@"\") ? @"*" : @"\*");
            
            
            // I will use this handler to start the initial search with the first file in the directory.
            // Afterwards, the handler will be passed to FindNextFile() in our Do..While{} loop as we continue searching
            // for files in the specified directory.
            var handle = Win32Native.FindFirstFileExW(
                prefixedPath,
                Win32Native.FINDEX_INFO_LEVELS.FindExInfoBasic,
                out lpFindFileData,
                Win32Native.FINDEX_SEARCH_OPS.FindExSearchNameMatch,
                IntPtr.Zero,
                flags
            );
            
            
            // Going off my inspiration from Communary.FileExtensions, I chose to save the results in a List of type 
            // string. I felt it was better to keep it simple then use the FileInformation type.
            List<string> fileResults = new List<string>();
            List<string> errorResults = new List<string>();
            List<string> subDirectoryList = new List<string>();
            
            // Now, we check to make sure that something didnt raise an exception of some kind.
            if (!handle.IsInvalid)
            {
                
                // Start the do..while{} loop. This will only search files in the specified directory. Parallelism 
                // is added by seeing if the current item being checked is a directory, if it is and the recurse flag
                // was raised we add it to the subdirectoryResults list. This list will be ran through the Parallel.ForEach
                // function after the handler is disposed.
                do
                {
                    // skip . and .. files
                    if (lpFindFileData.cFileName != "." && lpFindFileData.cFileName != "..")
                    {
                        // this is where we do our check for the directory file attribute. I gotta learn why the bitwise
                        // & works here and not logical &&.
                        if ((lpFindFileData.dwFileAttributes & FileAttributes.Directory) == FileAttributes.Directory)
                        {
                            if (!recurse) continue;
                            
                            // remember! this will be ran through either parallel or a foreach block below!
                            subDirectoryList.Add(Path.Combine($"{path}\\", lpFindFileData.cFileName));
                        }
                        else
                        {
                            // At this point we *should* not be working with a directory, but never wanna take that 
                            // change right?
                            if (lpFindFileData.dwFileAttributes == FileAttributes.Directory && !getDirectory) continue;
                            
                            // This is the real important part, verify the file names match. If there is a match,
                            // add it to the fileResults list.
                            if (FileMatch(lpFindFileData.cFileName, searchPattern))
                            {
                                fileResults.Add(string.Concat($"{path}\\", lpFindFileData.cFileName));
                            }
                        }
                    }
                } while (Win32Native.FindNextFile(handle, out lpFindFileData));
                
                
                // Once there are no more files to search we dispose of our handle.
                handle.Dispose();
                
                
                // Verify we have the recurse flag enabled
                if (recurse)
                {
                    // Im not sure the real benefits here of using parallelism on the local drive vs a network drive.
                    // Since a spinning drive can only have one read occur at a time is it really faster to perform
                    // parallel runs? I can see it working for a SSD or network SAN with multiple drives for more
                    // the one read at a time. Idk Ill do more research and look into the performance of using the
                    // parallel flag and disabling it.
                    if (parallel)
                    {
                        // Originally Communary.FileExtensions used subDirectoryList.AsParallel().ForAll(x => {});
                        // That was causing some really bad performance for me. Could be my mangled code but moving
                        // to Parallel.ForEach sped up searched by nearly 4x speeds. Now, from what I gathered this 
                        // still is not optimal. As previously mentioned, I/O reads could be affected on a single 
                        // platter drive. But for SSD/multiple drive? May work better?
                        // Also, from what I gathered, Parallel.ForEach still has its own issues that are being masked.
                        // https://stackoverflow.com/a/25950320
                        Parallel.ForEach(subDirectoryList, dir =>
                        {
                            var (resultSubDirectory, errorResult) = FindFirstFileEx(
                                dir,
                                searchPattern,
                                getDirectory,
                                true,
                                true,
                                showErrors,
                                largeFetch
                            );
                            
                            lock (resultListLock)
                            {
                                fileResults.AddRange(resultSubDirectory);
                                errorResults.AddRange(errorResult);
                            }
                        });
                    }
                    else
                    {
                        // same as above, just a normal foreach so not parallel.
                        foreach (string directory in subDirectoryList)
                        {
                            var (results, errorResult) = FindFirstFileEx(
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

                            foreach (string error in errorResult)
                            {
                                errorResults.Add(error);
                            }
                        }
                    }
                }
            }
            else
            {
                // I hope no one uses this unless they dont like getting results
                if (showErrors)
                {
                    errorResults.Add(path);
                    /*int hr = Marshal.GetLastWin32Error();
                    if (hr != 2 && hr != 0x12)
                    {
                        throw new Win32Exception(hr);
                    }*/
                }
            }

            return (fileResults, errorResults);
        }
        
        // This actually does the filename matching
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
}
using System;
using FileSpyder.Win32;

namespace FileSpyder
{
    internal class Program
    {
        /// <param name="path">Directory to search for the specified file(s)</param>
        /// <param name="fileName">Name of the file(s) to search for</param>
        /// <param name="recurse">Search subdirectories for specified file</param>
        /// <param name="parallel">Search multiple subdirectories at once</param>
        /// <param name="showErrors">Suppress errors</param>
        /// <param name="largeFetch">Uses a larger buffer for directory queries, which can increase performance of the find operation.</param>
        public static void Main(string path, string fileName, bool recurse, bool parallel, bool showErrors, bool largeFetch)
        {
#if DEBUG
            path = @"C:\temp";
            fileName = "test.json";
            recurse = true;
            parallel = true;
            showErrors = false;
            largeFetch = false;
#endif
            var files = FileApi.FindFirstFileEx(
                path,
                fileName,
                false,
                recurse,
                parallel,
                showErrors,
                largeFetch
            );
            
            files.ForEach(x =>
            {
                Console.WriteLine(x.ToString());
            });
        }
    }
}
using System;
using System.Collections.Generic;
using System.Reflection;
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
            path = @"C:\Windows";
            fileName = "*.txt";
            recurse = true;
            parallel = true;
            showErrors = true;
            largeFetch = true;
#endif

            List<string> errorList = new List<string>();
            
            var (files, errors) = FileApi.FindFirstFileEx(
                path,
                fileName,
                false,
                recurse,
                parallel,
                showErrors,
                largeFetch
            );

            string appVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            Console.WriteLine($"FileSpyder v{appVersion}\n");

            Console.WriteLine($"Successfully found:");
            files.ForEach(x =>
            {
                Console.WriteLine(x.ToString());
            });

            Console.WriteLine($"\nErrors found:");
            errors.ForEach(x =>
            {
                Console.WriteLine(x.ToString());
            });

            Console.WriteLine($"\nFound: {files.Count} file(s)");
            Console.WriteLine($"Errors: {errors.Count}");
        }
    }
}
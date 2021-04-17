using System;
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
        public static void Main(string path, string fileName, bool recurse = true, bool parallel = true, bool showErrors = true, bool largeFetch = true)
        {
#if DEBUG
            path = @"C:\Windows";
            fileName = "*.txt";
#endif

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
            Console.WriteLine("---------------------------");
            Console.WriteLine($"FileSpyder v{appVersion}\n");
            Console.WriteLine($"Path: {path}");
            Console.WriteLine($"File name: {fileName}");
            Console.WriteLine($"Recursive search: {recurse}");
            Console.WriteLine($"Parallel search: {parallel}");
            Console.WriteLine($"Show errors: {showErrors}");
            Console.WriteLine("---------------------------\n\n");


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

            Console.WriteLine($"\nFiles Found: {files.Count}");
            Console.WriteLine($"Total Errors: {errors.Count}");
        }
    }
}
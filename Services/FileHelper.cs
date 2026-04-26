using System;
using System.Collections.Generic;
using System.IO;

namespace AdminInfoTools.Helpers
{
    public static class FileHelper
    {
        /// <summary>
        /// Saves a list of strings to a text file, with each string on a new line.
        /// </summary>
        /// <param name="lines">The collection of strings to write.</param>
        /// <param name="fileName">The name of the file to save.</param>
        /// <returns>The full path to the saved file.</returns>
        public static string SaveLinesToFile(IEnumerable<string> lines, string fileName)
        {
            File.WriteAllLines(fileName, lines);

            return Path.GetFullPath(fileName);
        }
    }
}
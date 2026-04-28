using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace AdminInfoTools.Helpers
{
    public static class HostnameHelper
    {
        /// <summary>
        /// Generates a sequence of hostnames based on a starting name and a count.
        /// The starting name must contain a numeric sequence (e.g., "AF-W-0001").
        /// </summary>
        /// <param name="startName">The starting hostname (e.g., "AF-W-0001").</param>
        /// <param name="count">The number of hostnames to generate.</param>
        /// <returns>An array of generated hostnames, or an empty array if generation fails.</returns>
        public static string[] GenerateSequential(string startName, int count)
        {
            // RightToLeft finds the last cluster of digits, so "AF-W-0001" correctly extracts "0001"
            var match = Regex.Match(startName, @"^(.*?)(\d+)(.*?)$", RegexOptions.RightToLeft);
            
            if (!match.Success)
            {
                // In a helper, we don't show MessageBox. We return an empty array or throw a specific exception.
                // For now, returning empty array and letting the caller handle the UI feedback.
                return Array.Empty<string>(); 
            }

            string prefix = match.Groups[1].Value;
            string numberStr = match.Groups[2].Value;
            string suffix = match.Groups[3].Value;
            
            int padding = numberStr.Length; 
            
            if (!int.TryParse(numberStr, out int startNumber)) 
            {
                return Array.Empty<string>(); 
            }

            var hostnames = new List<string>();
            for (int i = 0; i < count; i++)
            {
                string newNumberStr = (startNumber + i).ToString().PadLeft(padding, '0');
                hostnames.Add($"{prefix}{newNumberStr}{suffix}");
            }
            return hostnames.ToArray();
        }
    }
}
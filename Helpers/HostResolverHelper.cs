using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;

namespace AdminInfoTools.Helpers
{
    public static class HostResolverHelper
    {
        /// <summary>
        /// Mimics Windows Computer Management behavior: if empty, defaults to local machine.
        /// Otherwise, parses and attempts to resolve the provided hostnames.
        /// </summary>
        public static (string[] ResolvedHosts, string[] UnresolvedHosts) ResolveTargetHosts(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return (new[] { Environment.MachineName }, Array.Empty<string>());
            }

            var hosts = input.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                             .Select(h => h.Trim())
                             .Where(h => !string.IsNullOrEmpty(h))
                             .Distinct()
                             .ToArray();

            var resolved = new List<string>();
            var unresolved = new List<string>();

            foreach (var host in hosts)
            {
                try { var entry = Dns.GetHostEntry(host); resolved.Add(host); }
                catch { unresolved.Add(host); }
            }

            return (resolved.ToArray(), unresolved.ToArray());
        }
    }
}
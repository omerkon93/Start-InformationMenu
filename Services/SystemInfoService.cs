using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Threading.Tasks;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;
using AdminInfoTools.Models;

namespace AdminInfoTools.Services
{
    public class SystemInfoService
    {
        /// <summary>
        /// Asynchronously pings a computer to check if it is reachable on the network.
        /// </summary>
        public async Task<bool> PingComputerAsync(string hostname, int timeout = 1500)
        {
            try
            {
                using (Ping pingSender = new Ping())
                {
                    PingReply reply = await pingSender.SendPingAsync(hostname, timeout);
                    return reply.Status == IPStatus.Success;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Retrieves CIM/WMI information from a remote computer.
        /// Replaces the Get-CimInstance PowerShell calls.
        /// </summary>
        public async Task<ComputerInfoResult> GetComputerDataAsync(string hostname)
        {
            // First, quickly check if it's online to avoid long WMI timeouts
            bool isOnline = await PingComputerAsync(hostname);

            if (!isOnline)
            {
                return GetFallbackObject(hostname, "Offline");
            }

            // Wrap the CIM calls in Task.Run to keep the WPF UI thread completely responsive
            return await Task.Run(() =>
            {
                try
                {
                    // By default, CimSession uses WinRM just like Get-CimInstance
                    using (CimSession session = CimSession.Create(hostname))
                    {
                        var cs = session.QueryInstances(@"root\cimv2", "WQL", "SELECT Model, Name, UserName FROM Win32_ComputerSystem").FirstOrDefault();
                        var os = session.QueryInstances(@"root\cimv2", "WQL", "SELECT Caption, Version FROM Win32_OperatingSystem").FirstOrDefault();
                        var bios = session.QueryInstances(@"root\cimv2", "WQL", "SELECT Caption FROM Win32_BIOS").FirstOrDefault();
                        
                        // A machine can have multiple physical processors
                        var processors = session.QueryInstances(@"root\cimv2", "WQL", "SELECT Name FROM Win32_Processor");
                        var cpuNames = string.Join(", ", processors.Select(p => p.CimInstanceProperties["Name"].Value?.ToString().Trim()));

                        return new ComputerInfoResult
                        {
                            ComputerName = hostname,
                            CsModel = cs?.CimInstanceProperties["Model"].Value?.ToString() ?? "N/A",
                            CsName = cs?.CimInstanceProperties["Name"].Value?.ToString() ?? "N/A",
                            CsUserName = cs?.CimInstanceProperties["UserName"].Value?.ToString() ?? "N/A",
                            OsName = os?.CimInstanceProperties["Caption"].Value?.ToString() ?? "N/A",
                            OSDisplayVersion = os?.CimInstanceProperties["Version"].Value?.ToString() ?? "N/A",
                            BiosCaption = bios?.CimInstanceProperties["Caption"].Value?.ToString() ?? "N/A",
                            CsProcessors = string.IsNullOrEmpty(cpuNames) ? "N/A" : cpuNames,
                            Status = "Online",
                            IsOnline = true
                        };
                    }
                }
                catch (CimException ex)
                {
                    // Catch access denied, RPC server unavailable, etc.
                    return GetFallbackObject(hostname, $"Online (CIM Failed: {ex.Message})");
                }
                catch (Exception)
                {
                    return GetFallbackObject(hostname, "Online (CIM Failed)");
                }
            });
        }

        /// <summary>
        /// Generates the standard failure object when a PC cannot be reached or queried.
        /// </summary>
        private ComputerInfoResult GetFallbackObject(string hostname, string reason)
        {
            return new ComputerInfoResult
            {
                ComputerName = hostname,
                CsModel = "N/A",
                CsName = "N/A",
                CsProcessors = "N/A",
                CsUserName = "N/A",
                OsName = "N/A",
                OSDisplayVersion = "N/A",
                BiosCaption = "N/A",
                Status = reason,
                IsOnline = false
            };
        }
    }
}
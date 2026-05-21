using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AdminInfoTools.Models;

namespace AdminInfoTools.Services
{
    public class TemporaryPermissionTracker
    {
        private readonly NtfsManagementService _ntfsService;
        private readonly ConcurrentBag<TemporaryPermissionTask> _trackedTasks;
        private readonly CancellationTokenSource _cts;

        public TemporaryPermissionTracker(NtfsManagementService ntfsService)
        {
            _ntfsService = ntfsService;
            _trackedTasks = new ConcurrentBag<TemporaryPermissionTask>();
            _cts = new CancellationTokenSource();
            
            // Start the background monitoring loop
            Task.Run(() => MonitorExpirationsAsync(_cts.Token));
        }

        public void TrackPermission(TemporaryPermissionTask task)
        {
            _trackedTasks.Add(task);
        }

        private async Task MonitorExpirationsAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                var now = DateTime.Now;
                var expiredTasks = _trackedTasks.Where(t => t.ExpirationTime <= now && !t.IsRevoked).ToList();

                foreach (var task in expiredTasks)
                {
                    try
                    {
                        _ntfsService.RemovePermission(task.TargetPath, task.Identity, task.Rights, task.AccessType);
                        task.IsRevoked = true;
                    }
                    catch (Exception ex)
                    {
                        // Log failure. In a production scenario, we could retry later.
                        System.Diagnostics.Debug.WriteLine($"Failed to revoke temporary permission: {ex.Message}");
                    }
                }

                // Re-evaluate every 30 seconds
                await Task.Delay(TimeSpan.FromSeconds(30), token);
            }
        }
    }
}
using Microsoft.AspNetCore.SignalR;
using System;
using System.Threading.Tasks;

namespace IndustrialDataLogger.Hubs
{
    public class MonitoringHub : Hub
    {
        public override async Task OnConnectedAsync()
        {
            await base.OnConnectedAsync();
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await base.OnDisconnectedAsync(exception);
        }
    }
}

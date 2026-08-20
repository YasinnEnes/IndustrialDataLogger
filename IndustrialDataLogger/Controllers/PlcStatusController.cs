using IndustrialDataLogger.Services;
using Microsoft.AspNetCore.Mvc;

namespace IndustrialDataLogger.Controllers
{
    [ApiController]
    [Route("api/plc")]
    public class PlcStatusController : ControllerBase
    {
        private readonly IPlcConnectionManager _connectionManager;

        public PlcStatusController(IPlcConnectionManager connectionManager)
        {
            _connectionManager = connectionManager;
        }

        [HttpGet("status")]
        public IActionResult GetStatus()
        {
            var isConnected = _connectionManager.CurrentState == Enums.PlcConnectionState.Connected;
            return Ok(new
            {
                isConnected = isConnected,
                state = _connectionManager.CurrentState.ToString()
            });
        }
    }
}
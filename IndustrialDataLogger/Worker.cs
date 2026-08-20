using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using IndustrialDataLogger.Data;
using IndustrialDataLogger.Enums;
using IndustrialDataLogger.Hubs;
using IndustrialDataLogger.Models;
using IndustrialDataLogger.Models.Entities;
using IndustrialDataLogger.Services;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace IndustrialDataLogger
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly IHubContext<MonitoringHub> _hubContext;
        private bool? _lastMachineStatus = null;

        public Worker(
            ILogger<Worker> logger,
            IServiceProvider serviceProvider,
            IHubContext<MonitoringHub> hubContext)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _hubContext = hubContext;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Industrial Data Logger arka plan servisi başlatıldı (EF Core Logging & SignalR Real-Time Push & Digital Twin State).");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var plcService = scope.ServiceProvider.GetRequiredService<IPlcService>();
                    var connectionManager = scope.ServiceProvider.GetRequiredService<IPlcConnectionManager>();
                    var alarmService = scope.ServiceProvider.GetRequiredService<IAlarmService>();
                    var digitalTwinService = scope.ServiceProvider.GetRequiredService<IDigitalTwinService>();
                    var eventLogService = scope.ServiceProvider.GetRequiredService<IEventLogService>();

                    // GÜN 4: PLC bağlantı durumunu alarm motoruna bildir
                    await alarmService.ProcessPlcStatusAsync(connectionManager.CurrentState, stoppingToken);

                    // GÜN 3: PLC durumunu anlık olarak SignalR istemcilerine yayınla
                    await _hubContext.Clients.All.SendAsync("ReceivePlcStatus", new
                    {
                        isConnected = connectionManager.IsConnected,
                        state = connectionManager.CurrentState.ToString()
                    }, stoppingToken);

                    SensorData? data = null;

                    if (connectionManager.IsConnected)
                    {
                        data = await plcService.ReadSensorDataAsync(stoppingToken);

                        if (data != null)
                        {
                            _logger.LogInformation($"Okunan Değerler -> Sıcaklık: {data.Temperature}°C | Basınç: {data.Pressure} bar | Durum: {(data.MachineStatus ? "Çalışıyor" : "Durdu")}");

                            // Makine durumu geçiş kontrolü
                            if (_lastMachineStatus.HasValue && _lastMachineStatus.Value != data.MachineStatus)
                            {
                                if (data.MachineStatus)
                                {
                                    await eventLogService.LogEventAsync("MACHINE_STARTED", "Üretim makinesi devreye girdi ve çalışmaya başladı.", AlarmSeverity.Info, "Telemetry", stoppingToken);
                                }
                                else
                                {
                                    await eventLogService.LogEventAsync("MACHINE_STOPPED", "Üretim makinesi durduruldu veya bekleme moduna geçti.", AlarmSeverity.Warning, "Telemetry", stoppingToken);
                                }
                            }
                            _lastMachineStatus = data.MachineStatus;

                            // GÜN 4: Sensör verilerini kural motorundan (Alarm Engine) geçir
                            await alarmService.ProcessSensorReadingAsync(data, stoppingToken);

                            // GÜN 3: SignalR üzerinden sensör telemetrisini yayınla
                            await _hubContext.Clients.All.SendAsync("ReceiveSensorData", new
                            {
                                timestamp = data.Timestamp,
                                temperature = Math.Round(data.Temperature, 2),
                                pressure = Math.Round(data.Pressure, 2),
                                machineStatus = data.MachineStatus,
                                errorCode = data.ErrorCode
                            }, stoppingToken);

                            // GÜN 2: EF Core ile PostgreSQL veritabanına loglama
                            try
                            {
                                var dbContext = scope.ServiceProvider.GetService<IndustrialDbContext>();
                                if (dbContext != null)
                                {
                                    var logEntity = new SensorDataLog
                                    {
                                        Timestamp = DateTime.UtcNow,
                                        Temperature = data.Temperature,
                                        Pressure = data.Pressure,
                                        MachineStatus = data.MachineStatus,
                                        ErrorCode = data.ErrorCode
                                    };

                                    await dbContext.SensorDataLogs.AddAsync(logEntity, stoppingToken);
                                    await dbContext.SaveChangesAsync(stoppingToken);
                                }
                            }
                            catch (Exception dbEx)
                            {
                                _logger.LogWarning("Veritabanı kaydı sırasında hata: {Message}", dbEx.Message);
                            }
                        }
                    }

                    // GÜN 2 & GÜN 5 (Sprint 2.4): Digital Twin Durumunu ve Sağlık Skorunu Güncelle & SignalR ile Yayınla
                    var twinState = await digitalTwinService.UpdateStateAsync(data, connectionManager.CurrentState, stoppingToken);
                    await _hubContext.Clients.All.SendAsync("ReceiveDigitalTwinState", twinState, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError("Worker döngü hatası: {Message}", ex.Message);
                }

                await Task.Delay(2000, stoppingToken);
            }
        }
    }
}
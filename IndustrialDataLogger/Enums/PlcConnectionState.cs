namespace IndustrialDataLogger.Enums
{
    public enum PlcConnectionState
    {
        /// <summary>
        /// PLC ile bağlantı tamamen kesilmiş durumda. Yeniden bağlanma bekleniyor veya durduruldu.
        /// </summary>
        Disconnected,

        /// <summary>
        /// PLC'ye ilk kez bağlantı kurma süreci aktif olarak devam ediyor.
        /// </summary>
        Connecting,

        /// <summary>
        /// PLC ile bağlantı başarılı ve veri okuma/yazma işlemleri yapılabilir durumda.
        /// </summary>
        Connected,

        /// <summary>
        /// PLC ile bağlantının kapatılma süreci devam ediyor.
        /// </summary>
        Disconnecting,

        /// <summary>
        /// Daha önceden kurulmuş bağlantı kopmuş; sistem otomatik olarak tekrar bağlanmaya çalışıyor.
        /// </summary>
        Reconnecting
    }
}
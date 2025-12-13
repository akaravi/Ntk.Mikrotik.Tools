using System;

namespace Ntk.Mikrotik.Tools.Models
{
    public class FrequencyScanResult
    {
        public DateTime ScanTime { get; set; }
        public double Frequency { get; set; }
        public double? AntennaPower { get; set; }
        public double? DownloadSpeed { get; set; }
        public double? UploadSpeed { get; set; }
        public double? SignalToNoiseRatio { get; set; }
        public double? SignalStrength { get; set; }
        public double? NoiseFloor { get; set; }
        public double? CCQ { get; set; }
        public double? TxRate { get; set; }
        public double? RxRate { get; set; }
        public string? Band { get; set; }
        public string? ChannelWidth { get; set; }
        public string? WirelessProtocol { get; set; }
        public string? Status { get; set; }
        public string? ErrorMessage { get; set; }
        
        // Local Antenna (this router) - already collected
        // Remote Antenna (connected antenna) information
        public string? RemoteMacAddress { get; set; }
        public string? RemoteIdentity { get; set; }
        public double? RemoteSignalStrength { get; set; }
        public double? RemoteSignalToNoiseRatio { get; set; }
        public double? RemoteTxRate { get; set; }
        public double? RemoteRxRate { get; set; }
        public double? RemoteCCQ { get; set; }
        public double? RemoteTxSignalStrength { get; set; }
        public double? RemoteRxSignalStrength { get; set; }
    }
}


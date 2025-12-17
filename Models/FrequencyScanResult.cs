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
        public string? RemoteRadioName { get; set; }
        public double? RemoteSignalStrength { get; set; }
        public double? RemoteSignalToNoiseRatio { get; set; }
        public double? RemoteTxRate { get; set; }
        public double? RemoteRxRate { get; set; }
        public double? RemoteCCQ { get; set; }
        public double? RemoteTxCCQ { get; set; }
        public double? RemoteRxCCQ { get; set; }
        public double? RemoteTxSignalStrength { get; set; }
        public double? RemoteRxSignalStrength { get; set; }
        public double? RemoteSignalStrengthCh0 { get; set; }
        public double? RemoteSignalStrengthCh1 { get; set; }
        public double? RemoteTxSignalStrengthCh0 { get; set; }
        public double? RemoteTxSignalStrengthCh1 { get; set; }
        public double? RemotePThroughput { get; set; }
        public long? RemotePacketsRx { get; set; }
        public long? RemotePacketsTx { get; set; }
        public long? RemoteBytesRx { get; set; }
        public long? RemoteBytesTx { get; set; }
        public long? RemoteFramesRx { get; set; }
        public long? RemoteFramesTx { get; set; }
        public long? RemoteFrameBytesRx { get; set; }
        public long? RemoteFrameBytesTx { get; set; }
        public long? RemoteHwFramesRx { get; set; }
        public long? RemoteHwFramesTx { get; set; }
        public long? RemoteHwFrameBytesRx { get; set; }
        public long? RemoteHwFrameBytesTx { get; set; }
        public long? RemoteTxFramesTimedOut { get; set; }
        public string? RemoteUptime { get; set; }
        public string? RemoteLastActivity { get; set; }
        public bool? RemoteNstreme { get; set; }
        public bool? RemoteNstremePlus { get; set; }
        public string? RemoteFramingMode { get; set; }
        public string? RemoteRouterOsVersion { get; set; }
        public string? RemoteLastIp { get; set; }
        public bool? Remote8021xPortEnabled { get; set; }
        public string? RemoteAuthenticationType { get; set; }
        public string? RemoteEncryption { get; set; }
        public string? RemoteGroupEncryption { get; set; }
        public bool? RemoteManagementProtection { get; set; }
        public bool? RemoteCompression { get; set; }
        public bool? RemoteWmmEnabled { get; set; }
        public string? RemoteTxRateSet { get; set; }
        
        // Ping Test Results
        public bool? PingSuccess { get; set; }
        public long? PingTime { get; set; } // in milliseconds
        public int? PingPacketsSent { get; set; }
        public int? PingPacketsReceived { get; set; }
        public int? PingPacketsLost { get; set; }
        public double? PingLossPercentage { get; set; }
        public long? PingMinTime { get; set; }
        public long? PingMaxTime { get; set; }
        public long? PingAverageTime { get; set; }
        public string? PingTestIpAddress { get; set; }
    }
}


namespace Ntk.Mikrotik.Tools.Models
{
    public class ScanSettings
    {
        public string RouterIpAddress { get; set; } = "192.168.88.1";
        public int SshPort { get; set; } = 22;
        public string Username { get; set; } = "admin";
        public string Password { get; set; } = "";
        public double StartFrequency { get; set; } = 2400;
        public double EndFrequency { get; set; } = 2500;
        public double FrequencyStep { get; set; } = 1;
        public int StabilizationTimeMinutes { get; set; } = 2;
        public string InterfaceName { get; set; } = "wlan1";
        
        // Multiple wireless-protocol and channel-width for testing combinations
        public string WirelessProtocols { get; set; } = ""; // Comma or newline separated
        public string ChannelWidths { get; set; } = ""; // Comma or newline separated
        
        // RouterOS Commands (can be customized)
        public string CommandGetFrequency { get; set; } = "/interface wireless print where name=\"{interface}\" value-name=frequency";
        public string CommandSetFrequency { get; set; } = "/interface wireless set \"{interface}\" frequency={frequency}";
        public string CommandGetInterfaceInfo { get; set; } = "/interface wireless print detail where name=\"{interface}\"";
        public string CommandGetRegistrationTable { get; set; } = "/interface wireless registration-table print detail where interface=\"{interface}\"";
        public string CommandMonitorInterface { get; set; } = "/interface wireless monitor \"{interface}\" once";
    }
}


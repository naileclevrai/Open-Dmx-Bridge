namespace OpenDMXBridge.Models;

    public sealed record NetworkAdapterInfo(string Id, string Name, string IpAddress, bool IsUp)
    {
        public string DisplayName => string.IsNullOrWhiteSpace(IpAddress) ? Name : $"{Name} ({IpAddress})";
    }

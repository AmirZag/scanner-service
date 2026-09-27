namespace ScannerService.Domain.Common;

/// <summary>
/// A manually configured eSCL (driverless network) scanner for deployments where mDNS discovery cannot
/// reach the device (UDP 5353 blocked by firewall, VLAN segmentation, WiFi client isolation).
/// <see cref="Address"/> is the normalized eSCL root URL (e.g. "http://192.168.1.50:8080/eSCL") and
/// doubles as the stable device id: NAPS2 connects to a device whose id is an absolute http(s) URL
/// directly, without any discovery round-trip.
/// </summary>
public sealed record EsclManualDevice(string Name, string Address);

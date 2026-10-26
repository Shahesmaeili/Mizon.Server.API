namespace Mizon.Server;

public class RequestMetadata
{
    public Guid? ClientPublicId { get; set; }
    public int? ClientId { get; set; }
    public Guid? SessionPublicId { get; set; }
    public Version? OsVersion { get; set; }
    public string? DeviceIdentifier { get; set; }
    public string? DeviceModel { get; set; }
    public string? DeviceName { get; set; }
    public string? DeviceUsername { get; set; }
    public Guid? PlatformVersion { get; set; }
}

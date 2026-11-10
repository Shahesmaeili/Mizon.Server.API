namespace Mizon.Server.API.Caching;

public sealed class MizonResponseCacheOptions
{
    public bool Enabled { get; set; } = true;
    public long SizeLimitBytes { get; set; } = 64 * 1024 * 1024;
    public long MaximumBodySizeBytes { get; set; } = 2 * 1024 * 1024;
    public int MaximumRequestBodyBytes { get; set; } = 64 * 1024;
}

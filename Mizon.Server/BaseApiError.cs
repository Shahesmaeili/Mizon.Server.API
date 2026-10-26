namespace Mizon.Server;


/// <summary>
/// Represents a base error response from the API.
/// </summary>
public class BaseApiError
{
    /// <summary>
    /// The error code returned by the API.
    /// </summary>
    public int Code { get; set; }

    /// <summary>
    /// A human-readable Title describing the error.
    /// </summary>
    public string Title { get; set; } = null!;

    /// <summary>
    /// A human-readable Details describing the error.
    /// </summary>
    public string Details { get; set; } = null!;

    /// <summary>
    /// Optional text for a close button.
    /// </summary>
    public string? CloseButtonText { get; set; }
}

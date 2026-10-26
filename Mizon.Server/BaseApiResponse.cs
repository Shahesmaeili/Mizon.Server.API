namespace Mizon.Server;

public interface IBaseApiResponse { }


/// <summary>
/// BaseApiResponse is a generic class representing the response from an API call.
/// It can either contain the successful response data or an error.
/// </summary>
/// <typeparam name="TApiResponse">The type of the successful API response data, which must implement the <see cref="IApiResponse"/> interface.</typeparam>
public class BaseApiResponse<TApiResponse> : IBaseApiResponse
    where TApiResponse : IApiResponse
{
    /// <summary>
    /// The successful API response data, or null if an error occurred.
    /// </summary>
    public TApiResponse? Content { get; set; }


    /// <summary>
    /// Indicates whether the operation was successful.
    /// This property returns `true` if the operation completed without any errors,
    /// </summary>
    public bool IsSuccess { get => Error == null; }


    /// <summary>
    /// The error that occurred during the API call, or null if the call was successful.
    /// </summary>
    public BaseApiError? Error { get; set; }
}

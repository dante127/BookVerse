namespace BookVerse.Application.Common.Models;

public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Message { get; set; }
    public List<ErrorDetail> Errors { get; set; } = [];

    public static ApiResponse<T> Ok(T data, string? message = null) => new()
    {
        Success = true,
        Data = data,
        Message = message,
        Errors = []
    };

    public static ApiResponse<T> Fail(string message, List<ErrorDetail>? errors = null) => new()
    {
        Success = false,
        Data = default,
        Message = message,
        Errors = errors ?? []
    };

    public static ApiResponse<T> Fail(string message, string field, string error) => new()
    {
        Success = false,
        Data = default,
        Message = message,
        Errors = [new ErrorDetail(field, error)]
    };
}

public class ApiResponse : ApiResponse<object>
{
    public static ApiResponse Ok(string? message = null) => new()
    {
        Success = true,
        Data = null,
        Message = message,
        Errors = []
    };

    public static new ApiResponse Fail(string message, List<ErrorDetail>? errors = null) => new()
    {
        Success = false,
        Data = null,
        Message = message,
        Errors = errors ?? []
    };
}

public record ErrorDetail(string Field, string Message);

public class PagedResult<T>
{
    public IReadOnlyList<T> Items { get; init; } = [];
    public int Page { get; init; }
    public int PageSize { get; init; }
    public int TotalCount { get; init; }
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / (PageSize > 0 ? PageSize : 1));
    public bool HasNextPage => Page < TotalPages;
    public bool HasPreviousPage => Page > 1;

    public PagedResult() { }

    public PagedResult(IReadOnlyList<T> items, int totalCount, int page, int pageSize)
    {
        Items = items;
        TotalCount = totalCount;
        Page = page;
        PageSize = pageSize;
    }
}

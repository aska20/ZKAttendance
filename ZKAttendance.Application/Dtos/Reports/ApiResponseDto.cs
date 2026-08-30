namespace ZKAttendance.Application.Dtos.Common
{
    /// <summary>
    /// Generic DTO for API responses
    /// Can be used by any API controller
    /// </summary>
    /// <typeparam name="T">The type of the returned payload</typeparam>
    public class ApiResponseDto<T>
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public T? Data { get; set; }

        /// <summary>
        /// Build a success response
        /// </summary>
        public static ApiResponseDto<T> SuccessResponse(T data, string message = "Operation completed successfully")
        {
            return new ApiResponseDto<T>
            {
                Success = true,
                Message = message,
                Data = data
            };
        }

        /// <summary>
        /// Build a failure response
        /// </summary>
        public static ApiResponseDto<T> ErrorResponse(string message, T? data = default)
        {
            return new ApiResponseDto<T>
            {
                Success = false,
                Message = message,
                Data = data
            };
        }
    }
}

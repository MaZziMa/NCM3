using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace NCM3.Middleware
{
    public class GlobalExceptionHandlerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

        public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            _logger.LogError(exception, $"Lỗi xảy ra: {exception.Message}");
            
            context.Response.ContentType = "application/json";
            
            var statusCode = HttpStatusCode.InternalServerError;
            var message = "Đã xảy ra lỗi không mong muốn. Vui lòng thử lại sau hoặc liên hệ quản trị viên.";

            // Có thể xử lý các loại lỗi cụ thể ở đây
            if (exception is UnauthorizedAccessException)
            {
                statusCode = HttpStatusCode.Unauthorized;
                message = "Bạn không có quyền thực hiện hành động này.";
            }
            else if (exception is ArgumentException || exception is ArgumentNullException)
            {
                statusCode = HttpStatusCode.BadRequest;
                message = exception.Message;
            }
            else if (exception is TimeoutException)
            {
                statusCode = HttpStatusCode.RequestTimeout;
                message = "Yêu cầu đã hết thời gian chờ. Vui lòng thử lại sau.";
            }
            
            context.Response.StatusCode = (int)statusCode;
            
            // Khi yêu cầu là API, trả về JSON
            if (context.Request.Headers["Accept"].ToString().Contains("application/json"))
            {
                var result = JsonSerializer.Serialize(new { error = message });
                return context.Response.WriteAsync(result);
            }
            
            // Khi yêu cầu là từ trình duyệt, chuyển hướng đến trang lỗi
            context.Response.Redirect($"/Home/Error?message={WebUtility.UrlEncode(message)}");
            return Task.CompletedTask;
        }
    }
}

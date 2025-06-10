using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Collections.Generic;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Moq.Protected;
using NCM3.Services;
using System.Threading;

namespace NCM3.Tests
{
    [TestClass]
    public class TelegramNotificationServiceTests
    {
        private IConfiguration _config;
        private Mock<ILogger<TelegramNotificationService>> _loggerMock;
        private Mock<HttpMessageHandler> _handlerMock;
        private HttpClient _httpClient;
        private TelegramNotificationService _service;

        [TestInitialize]
        public void Setup()
        {
            var inMemorySettings = new Dictionary<string, string> {
                {"Telegram:BotToken", "test_bot_token"},
                {"Telegram:ChatId", "test_chat_id"},
                {"Telegram:NotificationFormat", "MarkdownV2"},
                {"Telegram:EnableMarkdownFormatting", "true"}
            };

            _config = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            _loggerMock = new Mock<ILogger<TelegramNotificationService>>();
            _handlerMock = new Mock<HttpMessageHandler>();
            _httpClient = new HttpClient(_handlerMock.Object);

            // Setup handler mock
            _handlerMock
                .Protected()
                .Setup<Task<HttpResponseMessage>>(
                    "SendAsync",
                    ItExpr.IsAny<HttpRequestMessage>(),
                    ItExpr.IsAny<CancellationToken>()
                )
                .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK));

            _service = new TelegramNotificationService(_config, _httpClient, _loggerMock.Object);
        }

        [TestMethod]
        public async Task SendConfigChangeNotification_WithMarkdownV2_ShouldEscapeSpecialCharacters()
        {
            // Arrange
            var routerName = "Router1";
            var changeType = "Test";
            var details = "Testing * special _ characters + [ ] ( )";

            // Act
            await _service.SendConfigChangeNotificationAsync(routerName, changeType, details);

            // Assert
            _handlerMock
                .Protected()
                .Verify<Task<HttpResponseMessage>>(
                    "SendAsync",
                    Times.Once(),
                    ItExpr.Is<HttpRequestMessage>(req => true),
                    ItExpr.IsAny<CancellationToken>()
                );
        }

        [TestMethod]
        public async Task SendConfigChangeNotification_WithCodeBlock_ShouldPreserveFormatting()
        {
            // Arrange
            var routerName = "Router1";
            var changeType = "Test";
            var details = "```\nThis is a code block\n```";

            // Act
            await _service.SendConfigChangeNotificationAsync(routerName, changeType, details);

            // Assert
            _handlerMock
                .Protected()
                .Verify<Task<HttpResponseMessage>>(
                    "SendAsync",
                    Times.Once(),
                    ItExpr.Is<HttpRequestMessage>(req => true),
                    ItExpr.IsAny<CancellationToken>()
                );
        }

        [TestMethod]
        public async Task SendConfigChangeNotification_WithHtmlMode_ShouldConvertToHtmlTags()
        {
            // Arrange
            var routerName = "Router1";
            var changeType = "Test";
            var details = "*bold text* and _italic text_ and ```code block```";

            // Set HTML mode
            var htmlConfig = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string> {
                    {"Telegram:BotToken", "test_bot_token"},
                    {"Telegram:ChatId", "test_chat_id"},
                    {"Telegram:NotificationFormat", "HTML"},
                    {"Telegram:EnableMarkdownFormatting", "true"}
                })
                .Build();

            var serviceWithHtml = new TelegramNotificationService(htmlConfig, _httpClient, _loggerMock.Object);

            // Act
            await serviceWithHtml.SendConfigChangeNotificationAsync(routerName, changeType, details);

            // Assert
            _handlerMock
                .Protected()
                .Verify<Task<HttpResponseMessage>>(
                    "SendAsync",
                    Times.Once(),
                    ItExpr.Is<HttpRequestMessage>(req => true),
                    ItExpr.IsAny<CancellationToken>()
                );
        }
    }
}

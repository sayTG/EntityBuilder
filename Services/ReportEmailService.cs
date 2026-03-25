using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EntityBuilder.Configuration;
using EntityBuilder.Interfaces;
using EntityBuilder.Models;
using EntityBuilder.Utilities;
using Microsoft.Extensions.Options;

namespace EntityBuilder.Services;

public class ReportEmailService : IReportEmailService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly MessagingSettings _messagingSettings;
    private readonly CryptographySettings _cryptographySettings;

    public ReportEmailService(
        IHttpClientFactory httpClientFactory,
        IOptions<MessagingSettings> messagingSettings,
        IOptions<CryptographySettings> cryptographySettings)
    {
        _httpClientFactory = httpClientFactory;
        _messagingSettings = messagingSettings.Value;
        _cryptographySettings = cryptographySettings.Value;
    }

    public async Task<ApiResponse<object>> SendReportAsync(ReportEmailRequest request)
    {
        if (string.IsNullOrEmpty(_cryptographySettings.Key))
            return new ApiResponse<object> { Code = 0, ShortDescription = "Encryption key not configured." };

        var encryptedSql = Cryptography.AesEncryptionManager.Encrypt(request.Sql, _cryptographySettings.Key);

        var payload = new
        {
            receipientsEmailAddresses = new[] { request.RecipientEmail },
            subject = request.Subject,
            emailTemplateName = "Report.html",
            fullName = request.DisplayName,
            attachmentFileURL = "",
            placeHolder = encryptedSql,
            dapperTemplateValues = request.DapperTemplateValues
        };

        var client = _httpClientFactory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", request.Token);

        var json = JsonSerializer.Serialize(payload);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var response = await client.PostAsync(
            $"{_messagingSettings.BaseUrl}/Messaging/send-report-email", content);

        var responseBody = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            return new ApiResponse<object>
            {
                Code = 0,
                ShortDescription = $"Failed to send report. Status: {(int)response.StatusCode}. {responseBody}"
            };
        }

        return new ApiResponse<object>
        {
            Code = 1,
            ShortDescription = "Report sent successfully."
        };
    }
}

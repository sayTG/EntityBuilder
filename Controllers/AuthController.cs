using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EntityBuilder.Configuration;
using EntityBuilder.Models;
using EntityBuilder.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace EntityBuilder.Controllers;

[AllowAnonymous]
public class AuthController : Controller
{
    private readonly AuthSettings _authSettings;
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public AuthController(IOptions<AuthSettings> authSettings, IHttpClientFactory httpClientFactory)
    {
        _authSettings = authSettings.Value;
        _httpClientFactory = httpClientFactory;
    }

    [HttpGet]
    public IActionResult Login(string? returnUrl = null)
    {
        if (User.Identity?.IsAuthenticated == true)
            return RedirectToAction("Index", "EntityBuilder");

        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        try
        {
            var client = _httpClientFactory.CreateClient();
            var payload = new { username = model.Username, password = model.Password, clientId = _authSettings.ClientId, loginClient = "EntityBuilder" };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{_authSettings.BaseUrl}/Accounts/Login", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            var result = JsonSerializer.Deserialize<ApiResponse<LoginData>>(responseBody, JsonOptions);
            if (result is null || result.Code != 1 || result.Data is null)
            {
                model.ErrorMessage = result?.ShortDescription ?? "Login failed.";
                return View(model);
            }

            var data = result.Data;
            var email = data.Email ?? data.UserName;

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, data.UserName),
                new(ClaimTypes.Email, email),
                new("DisplayName", $"{data.FirstName} {data.LastName}".Trim()),
                new("AccessToken", data.Token)
            };

            if (data.RolesDTOs.Count == 0)
                throw new Exception("Could not get valid roles");

            foreach (var role in data.RolesDTOs)
            {
                if (!string.IsNullOrEmpty(role.Name))
                    claims.Add(new Claim(ClaimTypes.Role, role.Name));
            }

            if (_authSettings.AllowedRoles.Count > 0 &&
                !claims.Any(c => c.Type == ClaimTypes.Role && _authSettings.AllowedRoles.Contains(c.Value)))
            {
                model.ErrorMessage = $"Access denied. Required role(s): {string.Join(", ", _authSettings.AllowedRoles)}";
                return View(model);
            }

            DateTimeOffset? expiresAt = null;
            if (DateTimeOffset.TryParse(data.TokenExpiration, out var parsed))
                expiresAt = parsed;

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties
                {
                    IsPersistent = true,
                    ExpiresUtc = expiresAt
                });

            var returnUrl = model.ReturnUrl;
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return RedirectToAction("Index", "EntityBuilder");
        }
        catch (HttpRequestException)
        {
            model.ErrorMessage = "Unable to connect to the authentication server. Please try again later.";
            return View(model);
        }
        catch (Exception ex)
        {
            model.ErrorMessage = ex.Message ?? "An unexpected error occurred during login.";
            return View(model);
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }
}

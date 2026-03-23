using System.Security.Claims;
using System.Text;
using System.Text.Json;
using EntityBuilder.Configuration;
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
            var payload = new
            {
                username = model.Username,
                password = model.Password,
                clientId = _authSettings.ClientId
            };

            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await client.PostAsync($"{_authSettings.BaseUrl}/Accounts/Login", content);
            var responseBody = await response.Content.ReadAsStringAsync();

            var result = JsonSerializer.Deserialize<JsonElement>(responseBody);

            var code = result.GetProperty("code").GetInt32();
            if (code != 1)
            {
                var desc = result.TryGetProperty("shortDescription", out var d) ? d.GetString() : "Login failed.";
                model.ErrorMessage = desc ?? "Login failed.";
                return View(model);
            }

            var data = result.GetProperty("data");
            var firstName = data.GetProperty("firstName").GetString() ?? "";
            var lastName = data.GetProperty("lastName").GetString() ?? "";
            var userName = data.GetProperty("userName").GetString() ?? model.Username;

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, userName),
                new("DisplayName", $"{firstName} {lastName}".Trim())
            };

            // Add roles
            if (data.TryGetProperty("rolesDTOs", out var roles))
            {
                foreach (var role in roles.EnumerateArray())
                {
                    var roleName = role.GetProperty("name").GetString();
                    if (!string.IsNullOrEmpty(roleName))
                        claims.Add(new Claim(ClaimTypes.Role, roleName));
                }
            }
            else
            {
                throw new Exception("Could not get valid roles");
            }

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = true });

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

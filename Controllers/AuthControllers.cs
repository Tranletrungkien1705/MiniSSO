using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MiniSSO.Data;
using MiniSSO.Services;

namespace MiniSSO.Controllers;

public class AccountController(AuthService auth) : Controller
{
    [HttpGet]
    public IActionResult Login(string? returnUrl = null) { ViewBag.ReturnUrl = returnUrl; return View(); }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(string email, string password, string? returnUrl = null)
    {
        var user = await auth.ValidateUserAsync(email ?? "", password ?? "");
        if (user == null) { ModelState.AddModelError("", "Email hoặc mật khẩu không đúng."); ViewBag.ReturnUrl = returnUrl; return View(); }
        var identity = new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim("email", user.Email), new Claim("name", user.FullName)
        }, CookieAuthenticationDefaults.AuthenticationScheme);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(identity));
        return LocalRedirect(!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl) ? returnUrl : "/");
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        return RedirectToAction("Login");
    }
}

/// <summary>Luồng authorization_code + PKCE (OIDC): /oauth/authorize → login → consent → redirect kèm code.</summary>
public class OAuthController(AuthService auth, AppDbContext db) : Controller
{
    [HttpGet("/oauth/authorize")]
    public async Task<IActionResult> Authorize(string response_type, string client_id, string redirect_uri,
        string? scope, string? state, string? code_challenge, string? code_challenge_method)
    {
        if (response_type != "code") return BadRequest(new { error = "unsupported_response_type" });
        var client = await auth.GetClientAsync(client_id);
        if (client == null || !client.Grants.Contains("authorization_code")) return BadRequest(new { error = "invalid_client" });
        if (!client.Redirects.Contains(redirect_uri)) return BadRequest(new { error = "invalid_redirect_uri" });
        if (client.RequirePkce && string.IsNullOrEmpty(code_challenge)) return BadRequest(new { error = "code_challenge required (PKCE)" });

        if (User.Identity?.IsAuthenticated != true)
            return RedirectToAction("Login", "Account", new { returnUrl = Request.Path + Request.QueryString });

        ViewBag.Client = client;
        ViewBag.Scope = scope ?? string.Join(' ', client.Scopes);
        ViewBag.Params = new { response_type, client_id, redirect_uri, state, code_challenge, code_challenge_method };
        return View("Consent");
    }

    [HttpPost("/oauth/authorize"), ValidateAntiForgeryToken]
    public async Task<IActionResult> Consent(string decision, string client_id, string redirect_uri,
        string? scope, string? state, string? code_challenge, string? code_challenge_method)
    {
        if (User.Identity?.IsAuthenticated != true) return RedirectToAction("Login", "Account");
        var sep = redirect_uri.Contains('?') ? '&' : '?';
        if (decision != "allow")
            return Redirect($"{redirect_uri}{sep}error=access_denied" + (state != null ? $"&state={Uri.EscapeDataString(state)}" : ""));

        var uid = Guid.Parse(User.FindFirstValue(System.Security.Claims.ClaimTypes.NameIdentifier)!);
        var code = await auth.CreateAuthCodeAsync(client_id, uid, redirect_uri, scope ?? "openid", code_challenge, code_challenge_method);
        var url = $"{redirect_uri}{sep}code={code}" + (state != null ? $"&state={Uri.EscapeDataString(state)}" : "");
        return Redirect(url);
    }
}

public class HomeController : Controller
{
    // SPA React (admin) ở "/". Login/authorize/consent + OAuth minimal-API endpoints giữ nguyên.
    public IActionResult Index() => Redirect("/index.html");
}

public class LegacyController(AppDbContext db) : Controller
{
    public async Task<IActionResult> Index()
    {
        ViewBag.Users = await db.Users.CountAsync();
        ViewBag.Clients = await db.Clients.CountAsync();
        ViewBag.Tokens = await db.RefreshTokens.CountAsync(t => !t.Revoked);
        ViewBag.Issuer = $"{Request.Scheme}://{Request.Host}";
        return View("~/Views/Home/Index.cshtml");
    }
}

using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;

namespace MCIT.AI.Web.Chat;

internal static class ChatBrowserIdentity
{
    public const string ClaimType = "mcit-chat-browser";
    private const string Scheme = CookieAuthenticationDefaults.AuthenticationScheme;

    public static void AddChatBrowserIdentity(this IServiceCollection services, bool development)
    {
        services.AddAuthentication(Scheme).AddCookie(options =>
        {
            options.Cookie.Name = "MCIT.AI.Chat";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy = development ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromDays(90);
        });
        services.AddCascadingAuthenticationState();
    }

    public static void UseChatBrowserIdentity(this WebApplication app)
    {
        app.UseAuthentication();
        app.Use(async (context, next) =>
        {
            if (HttpMethods.IsGet(context.Request.Method) &&
                context.Request.Path.Equals("/chatbot", StringComparison.OrdinalIgnoreCase) &&
                context.User.FindFirst(ClaimType) is null)
            {
                string key = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                ClaimsPrincipal user = new(new ClaimsIdentity([new Claim(ClaimType, key)], Scheme));
                await context.SignInAsync(Scheme, user, new AuthenticationProperties { IsPersistent = true });
                context.User = user;
            }

            await next(context);
        });
    }
}

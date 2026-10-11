using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Lumiere.Filters;

/// <summary>
/// Пускает в админку только после входа (флаг в сессии).
/// Ставится на весь контроллер; действия входа/выхода помечены
/// <see cref="AllowAdminAnonymousAttribute"/>, чтобы их можно было открыть без авторизации.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public class AdminOnlyAttribute : Attribute, IAuthorizationFilter
{
    /// <summary>Ключ в сессии: "1" — админ авторизован.</summary>
    public const string SessionKey = "admin_auth";

    public void OnAuthorization(AuthorizationFilterContext context)
    {
        // Вход/выход доступны всем — проверяем маркер на действии/контроллере.
        if (context.ActionDescriptor.EndpointMetadata.Any(m => m is AllowAdminAnonymousAttribute))
        {
            return;
        }

        var authorized = context.HttpContext.Session.GetString(SessionKey) == "1";
        if (authorized)
        {
            return;
        }

        context.Result = new RedirectToActionResult("Login", "Admin", null);
    }
}

/// <summary>Помечает действия админки, доступные без входа (Login / Logout).</summary>
[AttributeUsage(AttributeTargets.Method)]
public class AllowAdminAnonymousAttribute : Attribute
{
}

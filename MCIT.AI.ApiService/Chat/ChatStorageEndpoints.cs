using System.Net.Http.Headers;
using System.Security.Cryptography;
using MCIT.AI.Application.Chat;
using Microsoft.AspNetCore.Mvc;

namespace MCIT.AI.ApiService.Chat;

internal static class ChatStorageEndpoints
{
    public static void MapChatStorage(this IEndpointRouteBuilder endpoints)
    {
        RouteGroupBuilder group = endpoints.MapGroup("/api/chat");
        group.AddEndpointFilter(async (context, next) =>
        {
            if (!TryGetOwner(context.HttpContext, out _))
            {
                return Results.Unauthorized();
            }

            if (context.HttpContext.RequestServices.GetService<IChatStore>() is null)
            {
                return Results.StatusCode(StatusCodes.Status503ServiceUnavailable);
            }

            context.HttpContext.Response.Headers.CacheControl = "no-store";
            return await next(context);
        });

        group.MapGet("/history", async (HttpContext context, CancellationToken token) =>
            Results.Ok(await Store(context).ListAsync(Owner(context), token)));

        group.MapGet("/history/{id:guid}", async (Guid id, HttpContext context, CancellationToken token) =>
            await Store(context).ReadAsync(Owner(context), id, token) is { } messages
                ? Results.Ok(messages) : Results.NotFound());

        group.MapDelete("/history/{id:guid}", async (Guid id, HttpContext context, CancellationToken token) =>
            await Store(context).DeleteAsync(Owner(context), id, token) ? Results.NoContent() : Results.NotFound());

        group.MapGet("/memory", async (HttpContext context, CancellationToken token) =>
            Results.Ok(await Store(context).ListMemoryAsync(Owner(context), token)));

        group.MapPost("/memory", async (MemoryRequest request, HttpContext context, CancellationToken token) =>
        {
            string? content = request.Content?.Trim();
            if (string.IsNullOrEmpty(content) || content.Length > 2000)
            {
                return Results.BadRequest("A memory note must contain 1–2000 characters.");
            }

            try
            {
                return Results.Ok(await Store(context).AddMemoryAsync(Owner(context), content, token));
            }
            catch (InvalidOperationException exception)
            {
                return Results.Conflict(exception.Message);
            }
        }).WithMetadata(new RequestSizeLimitAttribute(16_000));

        group.MapDelete("/memory/{id:guid}", async (Guid id, HttpContext context, CancellationToken token) =>
            await Store(context).DeleteMemoryAsync(Owner(context), id, token) ? Results.NoContent() : Results.NotFound());
    }

    internal static bool TryGetOwner(HttpContext context, out Guid owner)
    {
        owner = default;
        if (!AuthenticationHeaderValue.TryParse(context.Request.Headers.Authorization, out AuthenticationHeaderValue? auth) ||
            !auth.Scheme.Equals("ChatOwner", StringComparison.OrdinalIgnoreCase) || auth.Parameter is not { Length: 64 } key)
        {
            return false;
        }

        Span<byte> credential = stackalloc byte[32];
        if (Convert.FromHexString(key.AsSpan(), credential, out _, out int count) != System.Buffers.OperationStatus.Done || count != 32)
        {
            return false;
        }

        owner = new Guid(SHA256.HashData(credential).AsSpan(0, 16));
        return true;
    }

    private static IChatStore Store(HttpContext context) => context.RequestServices.GetRequiredService<IChatStore>();

    private static Guid Owner(HttpContext context)
    {
        TryGetOwner(context, out Guid owner);
        return owner;
    }

    private sealed record MemoryRequest(string? Content);
}

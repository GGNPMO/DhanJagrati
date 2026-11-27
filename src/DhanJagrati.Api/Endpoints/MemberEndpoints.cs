using DhanJagrati.Api.Infrastructure;
using DhanJagrati.Data.Members;

namespace DhanJagrati.Api.Endpoints;

/// <summary>LOAN MASTER > Customer (Member Master Information).</summary>
public static class MemberEndpoints
{
    private static readonly string[] EditRoles = ["ADMIN", "MANAGER", "OPERATOR"];
    private static readonly string[] ManagerRoles = ["ADMIN", "MANAGER"];

    public static void MapMemberEndpoints(this IEndpointRouteBuilder app)
    {
        var g = app.MapGroup("/api/members").WithTags("Members").RequireAuthorization().AddEndpointFilter<PasswordChangedFilter>();

        g.MapGet("", (string? search, HttpContext http, MemberRepository repo, CancellationToken ct) =>
            repo.SearchAsync(http.Session().BranchId, search, ct));

        g.MapGet("/{id:int}", async (int id, HttpContext http, MemberRepository repo, CancellationToken ct) =>
            await repo.GetAsync(http.Session().BranchId, id, ct) is { } m ? Results.Ok(m) : Results.NotFound());

        g.MapGet("/lookup", async (string memberNo, MemberRepository repo, CancellationToken ct) =>
            await repo.FindByNumberAsync(memberNo, ct) is { } m ? Results.Ok(m) : Results.NotFound());

        g.MapGet("/navigate", async (Navigate direction, string? from, HttpContext http, MemberRepository repo, CancellationToken ct) =>
            await repo.NavigateAsync(http.Session().BranchId, direction, from, ct) is { } id ? Results.Ok(new { id }) : Results.NotFound());

        g.MapPost("", async (MemberInput input, HttpContext http, MemberRepository repo, BusinessClock clock, CancellationToken ct) =>
        {
            var s = http.Session();
            input.RegistrationDate ??= s.BusinessDate;
            var id = await repo.CreateAsync(s.BranchId, s.UserId, input, clock.Today, ct);
            return Results.Created($"/api/members/{id}", new { id });
        }).AddEndpointFilter(new RoleFilter(EditRoles));

        g.MapPut("/{id:int}", async (int id, MemberInput input, HttpContext http, MemberRepository repo, BusinessClock clock, CancellationToken ct) =>
        {
            var s = http.Session();
            return await repo.UpdateAsync(s.BranchId, s.UserId, id, input, clock.Today, ct) ? Results.NoContent() : Results.NotFound();
        }).AddEndpointFilter(new RoleFilter(EditRoles));

        g.MapDelete("/{id:int}", async (int id, HttpContext http, MemberRepository repo, FileStore files, CancellationToken ct) =>
        {
            var branchId = http.Session().BranchId;
            var photo = await repo.GetFilePathAsync(branchId, id, signature: false, ct);
            var signature = await repo.GetFilePathAsync(branchId, id, signature: true, ct);
            if (!await repo.DeleteAsync(branchId, id, ct)) return Results.NotFound();
            files.Delete(photo);
            files.Delete(signature);
            return Results.NoContent();
        }).AddEndpointFilter(new RoleFilter(ManagerRoles));

        // ---- Photo / signature ("Photo/Sign Verify") ----
        g.MapGet("/{id:int}/image/{kind}", async (int id, string kind, HttpContext http, MemberRepository repo, FileStore files, CancellationToken ct) =>
        {
            if (ParseKind(kind) is not { } signature) return Results.NotFound();
            var stored = files.Open(await repo.GetFilePathAsync(http.Session().BranchId, id, signature, ct));
            return stored is { } f ? Results.File(f.FullPath, f.ContentType) : Results.NotFound();
        });

        g.MapPost("/{id:int}/image/{kind}", async (int id, string kind, IFormFile file, HttpContext http,
            MemberRepository repo, FileStore files, CancellationToken ct) =>
        {
            if (ParseKind(kind) is not { } signature) return Results.NotFound();
            var s = http.Session();
            if (await repo.GetAsync(s.BranchId, id, ct) is null) return Results.NotFound();

            var path = await files.SaveImageAsync(file, $"members/{id}", signature ? "signature" : "photo", ct);
            var (found, previous) = await repo.SetFilePathAsync(s.BranchId, id, signature, path, s.UserId, ct);
            if (!found) { files.Delete(path); return Results.NotFound(); }
            files.Delete(previous);
            return Results.NoContent();
        }).AddEndpointFilter(new RoleFilter(EditRoles)).DisableAntiforgery();

        g.MapPost("/{id:int}/verify-photo", async (int id, VerifyRequest body, HttpContext http, MemberRepository repo, CancellationToken ct) =>
        {
            var s = http.Session();
            return await repo.SetPhotoVerifiedAsync(s.BranchId, id, body.Verified, s.UserId, ct)
                ? Results.NoContent()
                : Results.Problem(title: "Upload both photo and signature before verifying.", statusCode: 400);
        }).AddEndpointFilter(new RoleFilter(ManagerRoles));
    }

    public sealed record VerifyRequest(bool Verified);

    private static bool? ParseKind(string kind) => kind.ToLowerInvariant() switch
    {
        "photo" => false,
        "signature" => true,
        _ => null
    };
}

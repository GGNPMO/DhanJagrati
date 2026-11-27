using System.Text;
using DhanJagrati.Data.Security;
using Microsoft.AspNetCore.Identity;

namespace DhanJagrati.Api.Infrastructure;

/// <summary>Marker type for <see cref="PasswordHasher{TUser}"/>.</summary>
public sealed class AppUserAccount;

public static class PasswordPolicy
{
    public const string Description = "At least 8 characters with a letter and a digit.";

    public static bool IsStrong(string? password) =>
        password is { Length: >= 8 and <= 128 } && password.Any(char.IsLetter) && password.Any(char.IsDigit);
}

/// <summary>
/// One-time setup run by the person deploying, on the server:
///   dotnet DhanJagrati.Api.dll create-admin [--user SYSTEM] [--reset]
/// Prompts for a temporary password (never stored in config or source), hashes it into
/// sec.AppUser and forces a change at first login. Without --reset it only works while the
/// user has no password, so it cannot silently take over an account. --reset is the
/// recovery path when the administrator is locked out or has lost the password.
/// </summary>
public static class CreateAdminCommand
{
    public const string Name = "create-admin";

    public static async Task<int> RunAsync(IServiceProvider services, string[] args)
    {
        var userName = ArgValue(args, "--user") ?? "SYSTEM";
        var reset = args.Contains("--reset", StringComparer.OrdinalIgnoreCase);

        string? password;
        if (Console.IsInputRedirected)
        {
            // Piped input (e.g. from Windows PowerShell) can start with a UTF-8 byte-order mark.
            password = Console.ReadLine()?.TrimStart('﻿').TrimEnd('\r', '\n');
        }
        else
        {
            Console.Write($"Temporary password for {userName}: ");
            password = ReadMasked();
            Console.Write("Confirm password: ");
            if (ReadMasked() != password)
            {
                Console.Error.WriteLine("Passwords do not match.");
                return 1;
            }
        }

        if (!PasswordPolicy.IsStrong(password))
        {
            Console.Error.WriteLine($"Password rejected: {PasswordPolicy.Description}");
            return 1;
        }

        using var scope = services.CreateScope();
        var repo = scope.ServiceProvider.GetRequiredService<SecurityRepository>();
        var hasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUserAccount>>();

        var user = await repo.FindUserAsync(userName, CancellationToken.None);
        if (user is null)
        {
            Console.Error.WriteLine($"User '{userName}' does not exist in sec.AppUser.");
            return 1;
        }

        var updated = await repo.SetTemporaryPasswordAsync(userName, hasher.HashPassword(new AppUserAccount(), password!), reset, CancellationToken.None);
        if (updated == 0)
        {
            Console.Error.WriteLine($"'{userName}' already has a password. Use --reset to replace it.");
            return 1;
        }

        await repo.AuditAsync(userName, "PASSWORD_CHANGE", null, "create-admin", CancellationToken.None);
        Console.WriteLine($"Password set for {userName} ({user.RoleCode}). It must be changed at first login.");
        return 0;
    }

    private static string? ArgValue(string[] args, string name)
    {
        var index = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static string ReadMasked()
    {
        var text = new StringBuilder();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter) break;
            if (key.Key == ConsoleKey.Backspace) { if (text.Length > 0) text.Length--; continue; }
            if (!char.IsControl(key.KeyChar)) text.Append(key.KeyChar);
        }
        Console.WriteLine();
        return text.ToString();
    }
}

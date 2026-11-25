using System.IO;
using System.IO.Pipes;
using System.Security.Principal;
using WireGuardBar.Core;

namespace WireGuardBar;

public sealed class HelperFailure(string message) : Exception(message);

/// Talks to the wgbar-helper service over its named pipe. The pipe's ACL decides who may call it;
/// this side checks the pipe is owned by SYSTEM/Administrators before sending anything, so a program
/// that grabbed the pipe name first never sees a config.
public static class HelperClient
{
    private const string NotInstalled = "The helper service isn't running. Run install.ps1 as administrator.";

    public static async Task<string> RunAsync(string command, string? name = null, string? argument = null, string? input = null)
    {
        await using var pipe = new NamedPipeClientStream(".", Protocol.PipeName, PipeDirection.InOut,
            PipeOptions.Asynchronous, TokenImpersonationLevel.Identification);
        try { await pipe.ConnectAsync(3000); }
        catch (TimeoutException) { throw new HelperFailure(NotInstalled); }
        catch (UnauthorizedAccessException)
        {
            throw new HelperFailure("The helper service was installed for a different Windows account. Run install.ps1 again from this account.");
        }
        VerifyServer(pipe);

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        await Protocol.WriteAsync(pipe, new HelperRequest(command, name, argument, input), timeout.Token);
        HelperResponse response;
        try { response = await Protocol.ReadAsync<HelperResponse>(pipe, timeout.Token); }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            throw new HelperFailure("The helper service closed the connection. If it's older than the app, run install.ps1 again.");
        }
        if (response.Ok) return response.Output;
        if (response.Error?.StartsWith("usage:") == true)
            throw new HelperFailure("The installed helper is older than the app. Run install.ps1 again.");
        throw new HelperFailure(response.Error ?? "The helper failed.");
    }

    private static void VerifyServer(NamedPipeClientStream pipe)
    {
        var owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
        if (owner is not null && (owner.IsWellKnown(WellKnownSidType.LocalSystemSid) || owner.IsWellKnown(WellKnownSidType.BuiltinAdministratorsSid)))
            return;
        throw new HelperFailure("Something other than the WireGuard Bar helper is answering on its pipe, so nothing was sent to it.");
    }
}

using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using WireGuardBar.Core;

namespace WireGuardBar.Helper;

/// Serves one request per connection on \\.\pipe\wgbar-helper. Only SYSTEM and the one user
/// account given at install time can open the pipe (the Windows counterpart of the macOS sudoers
/// rule); network logons are denied outright.
sealed class PipeServer(SecurityIdentifier user, Commands commands)
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(10);

    public async Task RunAsync(CancellationToken ct)
    {
        var security = Security(user);
        var first = true;
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                // FirstPipeInstance: fail instead of joining a pipe someone else created first.
                // No instance cap: busy connections (an `up` can take a minute) must never make the
                // next Create fail and take the service down.
                var options = PipeOptions.Asynchronous | (first ? PipeOptions.FirstPipeInstance : 0);
                pipe = NamedPipeServerStreamAcl.Create(Protocol.PipeName, PipeDirection.InOut,
                    NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte, options, 0, 0, security);
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                // someone else holds the name (squatting) or a transient failure: keep trying, don't exit
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                continue;
            }
            first = false;
            try { await pipe.WaitForConnectionAsync(ct); }
            catch { await pipe.DisposeAsync(); throw; }
            _ = ServeAsync(pipe, ct);
        }
    }

    private async Task ServeAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        await using var _ = pipe;
        try
        {
            HelperRequest request;
            using (var read = CancellationTokenSource.CreateLinkedTokenSource(ct))
            {
                read.CancelAfter(RequestTimeout);   // a client can't hold an instance open forever
                request = await Protocol.ReadAsync<HelperRequest>(pipe, read.Token);
            }
            var response = await commands.HandleAsync(request, ct);
            await Protocol.WriteAsync(pipe, response, ct);
        }
        catch (Exception)
        {
            // malformed request, client gone or shutting down: just drop the connection
        }
    }

    private static PipeSecurity Security(SecurityIdentifier user)
    {
        var security = new PipeSecurity();
        security.SetOwner(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid, null),
            PipeAccessRights.FullControl, AccessControlType.Deny));
        security.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null),
            PipeAccessRights.FullControl, AccessControlType.Allow));
        security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.ReadWrite, AccessControlType.Allow));
        return security;
    }
}

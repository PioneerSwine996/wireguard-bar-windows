using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32.SafeHandles;
using Windows.Security.Credentials.UI;

namespace WireGuardBar;

/// Confirms the person at the keyboard is this Windows user before a config (with its private key)
/// is shown: Windows Hello when it's set up, otherwise the Windows credential prompt.
/// Like Touch ID on macOS this is an in-app check, not a barrier for other programs (see README).
public static class Auth
{
    /// true = verified, false = cancelled; throws HelperFailure when verification fails.
    public static async Task<bool> VerifyAsync(Window owner, string reason)
    {
        var hwnd = new WindowInteropHelper(owner).Handle;
        if (await UserConsentVerifier.CheckAvailabilityAsync() == UserConsentVerifierAvailability.Available)
        {
            // the ForWindow variant parents the Hello dialog to our window instead of hiding behind it
            var result = await UserConsentVerifierInterop.RequestVerificationForWindowAsync(hwnd, reason);
            return result switch
            {
                UserConsentVerificationResult.Verified => true,
                UserConsentVerificationResult.Canceled => false,
                _ => throw new HelperFailure($"Windows Hello couldn't verify you ({result})."),
            };
        }
        return CredentialPrompt(hwnd, reason);
    }

    private static bool CredentialPrompt(IntPtr hwnd, string reason)
    {
        var info = new CredUiInfo
        {
            cbSize = Marshal.SizeOf<CredUiInfo>(),
            hwndParent = hwnd,
            pszCaptionText = "WireGuard Bar",
            pszMessageText = reason,
        };
        uint package = 0;
        var save = false;
        var error = CredUIPromptForWindowsCredentials(ref info, 0, ref package, IntPtr.Zero, 0, out var buffer, out var size, ref save, CreduiwinGeneric);
        if (error == ErrorCancelled) return false;
        if (error != 0) throw new HelperFailure($"The credential prompt failed (error {error}).");

        var user = new StringBuilder(256);
        var domain = new StringBuilder(256);
        var password = new StringBuilder(256);
        int userLen = user.Capacity, domainLen = domain.Capacity, passwordLen = password.Capacity;
        try
        {
            if (!CredUnPackAuthenticationBuffer(0, buffer, size, user, ref userLen, domain, ref domainLen, password, ref passwordLen))
                throw new HelperFailure("Couldn't read the credentials you entered.");
            var name = user.ToString();
            string? dom = domain.Length > 0 ? domain.ToString() : null;
            if (dom is null && name.IndexOf('\\') is > 0 and var slash) { dom = name[..slash]; name = name[(slash + 1)..]; }

            if (!LogonUser(name, dom, password.ToString(), LogonInteractive, LogonProviderDefault, out var token))
                throw new HelperFailure("That password isn't right.");
            using (token)
            using (var identity = new WindowsIdentity(token.DangerousGetHandle()))
            using (var current = WindowsIdentity.GetCurrent())
            {
                if (identity.User != current.User) throw new HelperFailure("Those are another account's credentials. Use your own.");
            }
            return true;
        }
        finally
        {
            password.Clear();   // ponytail: StringBuilder memory isn't zeroed; the native buffer below is
            Marshal.Copy(new byte[size], 0, buffer, (int)size);
            CoTaskMemFree(buffer);
        }
    }

    private const int CreduiwinGeneric = 0x1, ErrorCancelled = 1223, LogonInteractive = 2, LogonProviderDefault = 0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CredUiInfo
    {
        public int cbSize;
        public IntPtr hwndParent;
        public string pszMessageText;
        public string pszCaptionText;
        public IntPtr hbmBanner;
    }

    [DllImport("credui.dll", CharSet = CharSet.Unicode)]
    private static extern int CredUIPromptForWindowsCredentials(ref CredUiInfo info, int authError, ref uint authPackage,
        IntPtr inBuffer, uint inSize, out IntPtr outBuffer, out uint outSize, ref bool save, int flags);

    [DllImport("credui.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredUnPackAuthenticationBuffer(int flags, IntPtr buffer, uint size, StringBuilder user, ref int userLen,
        StringBuilder domain, ref int domainLen, StringBuilder password, ref int passwordLen);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool LogonUser(string user, string? domain, string password, int logonType, int provider, out SafeAccessTokenHandle token);

    [DllImport("ole32.dll")]
    private static extern void CoTaskMemFree(IntPtr p);
}

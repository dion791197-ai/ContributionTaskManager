using System.Runtime.InteropServices;
using System.Text;

namespace GitHubGoal.Core.Services;

public interface ICredentialService
{
    string? Read(string target);

    void Write(string target, string userName, string secret);

    void Delete(string target);
}

/// <summary>
/// Stores the GitHub access token in Windows Credential Manager.
///
/// Uses the advapi32 credential API rather than WinRT's PasswordVault: PasswordVault
/// requires package identity and throws in an unpackaged app. Credentials are written
/// with CRED_PERSIST_LOCAL_MACHINE so they survive restarts, and are encrypted at rest
/// by Windows under the user's profile.
/// </summary>
public sealed class CredentialService : ICredentialService
{
    /// <summary>Credential Manager key for the GitHub access token.</summary>
    public const string GitHubTokenTarget = "GitHubGoal:AccessToken";

    private const int CRED_TYPE_GENERIC = 1;
    private const int CRED_PERSIST_LOCAL_MACHINE = 2;
    private const int ERROR_NOT_FOUND = 1168;

    public string? Read(string target)
    {
        if (!CredRead(target, CRED_TYPE_GENERIC, 0, out var handle))
        {
            var error = Marshal.GetLastWin32Error();
            if (error == ERROR_NOT_FOUND)
            {
                return null;
            }

            throw new InvalidOperationException($"Reading the saved credential failed (Win32 error {error}).");
        }

        try
        {
            var credential = Marshal.PtrToStructure<CREDENTIAL>(handle);
            if (credential.CredentialBlobSize == 0 || credential.CredentialBlob == IntPtr.Zero)
            {
                return null;
            }

            // The blob is UTF-16 and is NOT null-terminated; length comes from the struct.
            return Marshal.PtrToStringUni(credential.CredentialBlob, (int)credential.CredentialBlobSize / 2);
        }
        finally
        {
            CredFree(handle);
        }
    }

    public void Write(string target, string userName, string secret)
    {
        var blob = Encoding.Unicode.GetBytes(secret);

        // 512 bytes is the documented CredWrite limit for the credential blob.
        if (blob.Length > 512 * 5)
        {
            throw new ArgumentException("Secret is too large for Credential Manager.", nameof(secret));
        }

        var blobHandle = Marshal.AllocHGlobal(blob.Length);
        try
        {
            Marshal.Copy(blob, 0, blobHandle, blob.Length);

            var credential = new CREDENTIAL
            {
                Type = CRED_TYPE_GENERIC,
                TargetName = target,
                CredentialBlob = blobHandle,
                CredentialBlobSize = (uint)blob.Length,
                Persist = CRED_PERSIST_LOCAL_MACHINE,
                UserName = string.IsNullOrEmpty(userName) ? "github" : userName,
            };

            if (!CredWrite(ref credential, 0))
            {
                var error = Marshal.GetLastWin32Error();
                throw new InvalidOperationException($"Saving the credential failed (Win32 error {error}).");
            }
        }
        finally
        {
            // Zero the copy before releasing so the secret does not linger in freed memory.
            for (var i = 0; i < blob.Length; i++)
            {
                Marshal.WriteByte(blobHandle, i, 0);
            }

            Marshal.FreeHGlobal(blobHandle);
            Array.Clear(blob);
        }
    }

    public void Delete(string target)
    {
        if (!CredDelete(target, CRED_TYPE_GENERIC, 0))
        {
            var error = Marshal.GetLastWin32Error();
            if (error != ERROR_NOT_FOUND)
            {
                throw new InvalidOperationException($"Removing the credential failed (Win32 error {error}).");
            }
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct CREDENTIAL
    {
        public uint Flags;
        public uint Type;
        [MarshalAs(UnmanagedType.LPWStr)] public string TargetName;
        [MarshalAs(UnmanagedType.LPWStr)] public string? Comment;
        public long LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        [MarshalAs(UnmanagedType.LPWStr)] public string? TargetAlias;
        [MarshalAs(UnmanagedType.LPWStr)] public string UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredWrite(ref CREDENTIAL credential, uint flags);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredFree")]
    private static extern void CredFree(IntPtr buffer);
}

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace Seabreeze
{
    // One generic password per installation. Secrets never pass through a shell, PlayerPrefs or the save folder.
    internal sealed class CloudRefreshStore
    {
#if UNITY_EDITOR_OSX || (UNITY_STANDALONE_OSX && !UNITY_EDITOR)
        const string Security = "/System/Library/Frameworks/Security.framework/Security";
        const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        const int ItemNotFound = -25300;
        static readonly byte[] Service = Encoding.UTF8.GetBytes("com.litingyi.project-z.game-session");
        readonly byte[] account;

        public CloudRefreshStore(string installation) { account = Encoding.UTF8.GetBytes(installation); }

        public string Read()
        {
            int status = SecKeychainFindGenericPassword(IntPtr.Zero, Service.Length, Service, account.Length, account,
                out int length, out IntPtr data, out IntPtr item);
            if (status == ItemNotFound) return null;
            Check(status);
            try { return Marshal.PtrToStringAnsi(data, length); }
            finally { SecKeychainItemFreeContent(IntPtr.Zero, data); CFRelease(item); }
        }

        public void Write(string credential)
        {
            var bytes = Encoding.ASCII.GetBytes(credential);
            try
            {
                int status = Find(out var item);
                if (status == ItemNotFound)
                {
                    Check(SecKeychainAddGenericPassword(IntPtr.Zero, Service.Length, Service, account.Length, account,
                        bytes.Length, bytes, IntPtr.Zero));
                    return;
                }
                Check(status);
                try { Check(SecKeychainItemModifyAttributesAndData(item, IntPtr.Zero, bytes.Length, bytes)); }
                finally { CFRelease(item); }
            }
            finally { Array.Clear(bytes, 0, bytes.Length); }
        }

        public void Clear()
        {
            int status = Find(out var item);
            if (status == ItemNotFound) return;
            Check(status);
            try { Check(SecKeychainItemDelete(item)); }
            finally { CFRelease(item); }
        }

        int Find(out IntPtr item) => FindItem(IntPtr.Zero, Service.Length, Service, account.Length, account,
            IntPtr.Zero, IntPtr.Zero, out item);
        static void Check(int status)
        {
            if (status != 0) throw new InvalidOperationException("无法访问 macOS 钥匙串，请解锁钥匙串并允许 Project-Z 访问。");
        }

        [DllImport(Security)] static extern int SecKeychainFindGenericPassword(IntPtr keychain, int serviceLength,
            byte[] service, int accountLength, byte[] account, out int length, out IntPtr data, out IntPtr item);
        [DllImport(Security, EntryPoint = "SecKeychainFindGenericPassword")] static extern int FindItem(IntPtr keychain,
            int serviceLength, byte[] service, int accountLength, byte[] account, IntPtr length, IntPtr data, out IntPtr item);
        [DllImport(Security)] static extern int SecKeychainAddGenericPassword(IntPtr keychain, int serviceLength,
            byte[] service, int accountLength, byte[] account, int length, byte[] data, IntPtr item);
        [DllImport(Security)] static extern int SecKeychainItemModifyAttributesAndData(IntPtr item, IntPtr attributes,
            int length, byte[] data);
        [DllImport(Security)] static extern int SecKeychainItemDelete(IntPtr item);
        [DllImport(Security)] static extern int SecKeychainItemFreeContent(IntPtr attributes, IntPtr data);
        [DllImport(CoreFoundation)] static extern void CFRelease(IntPtr value);
#else
        public CloudRefreshStore(string installation) => throw new PlatformNotSupportedException();
        public string Read() => throw new PlatformNotSupportedException();
        public void Write(string credential) => throw new PlatformNotSupportedException();
        public void Clear() => throw new PlatformNotSupportedException();
#endif
    }
}

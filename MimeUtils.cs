using System;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.InteropServices;

namespace DiscordYoutubeDL
{
    public static class MimeUtils
    {
        [DllImport("urlmon.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = false)]
        static extern int FindMimeFromData(IntPtr pBC,
            [MarshalAs(UnmanagedType.LPWStr)] string pwzUrl,
            [MarshalAs(UnmanagedType.LPArray, ArraySubType=UnmanagedType.I1, SizeParamIndex=3)] 
            byte[] pBuffer,
            int cbSize,
            [MarshalAs(UnmanagedType.LPWStr)] string pwzMimeProposed,
            int dwMimeFlags,
            out IntPtr ppwzMimeOut,
            int dwReserved);

        public static async Task<string> getMimeFromStream(Stream stream)
        {
            byte[] buffer = new byte[256];
            if (stream.Length >= 256)
                await stream.ReadAsync(buffer, 0, 256);
            else
                await stream.ReadAsync(buffer, 0, (int)stream.Length);
            try
            {
                System.IntPtr mimetype;
                FindMimeFromData(IntPtr.Zero, null, buffer, 256, null, 0, out mimetype, 0);
                string mime = Marshal.PtrToStringUni(mimetype);
                Marshal.FreeCoTaskMem(mimetype);
                return mime;
            }
            catch (Exception)
            {
                return "unknown/unknown";
            }
        }

        public static string getMimeFromFile(string filename)
        {
            if (!File.Exists(filename))
                throw new FileNotFoundException(filename + " not found");

            byte[] buffer = new byte[256];
            using (FileStream fs = new FileStream(filename, FileMode.Open))
            {
                if (fs.Length >= 256)
                    fs.Read(buffer, 0, 256);
                else
                    fs.Read(buffer, 0, (int)fs.Length);
            }
            try
            {
                System.IntPtr mimetype;
                FindMimeFromData(IntPtr.Zero, null, buffer, 256, null, 0, out mimetype, 0);
                string mime = Marshal.PtrToStringUni(mimetype);
                Marshal.FreeCoTaskMem(mimetype);
                return mime;
            }
            catch (Exception)
            {
                return "unknown/unknown";
            }
        }

        /*
        public static string GetDefaultExtension(string mimeType)
        {
            return MimeTypes.MimeTypeMap.GetExtension(mimeType);
        }
        */
    }
}
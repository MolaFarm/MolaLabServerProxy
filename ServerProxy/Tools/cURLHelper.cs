using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using CurlThin;
using CurlThin.Enums;
using CurlThin.SafeHandles;

namespace ServerProxy.Tools;

public class cURLHelper : IDisposable
{
    private static readonly bool IsCreated = false;

    private readonly CURLcode CurlGlobal;

    public cURLHelper()
    {
        if (IsCreated) throw new NotSupportedException("Helper already initialize once");

        CurlGlobal = CurlNative.Init();
    }

    public void Dispose()
    {
        if (CurlGlobal == CURLcode.OK) CurlNative.Cleanup();
    }

    public CurlResult HttpGet(SafeEasyHandle handle)
    {
        var stream = new MemoryStream();
        var result = new CurlResult();
        CurlNative.Easy.SetOpt(handle, CURLoption.WRITEFUNCTION, (data, size, nmemb, user) =>
        {
            var length = (int)size * (int)nmemb;
            var buffer = new byte[length];
            Marshal.Copy(data, buffer, 0, length);
            stream.Write(buffer, 0, length);
            return (UIntPtr)length;
        });

        result.response = CurlNative.Easy.Perform(handle);
        result.data = Encoding.UTF8.GetString(stream.ToArray());

        handle.Dispose();

        return result;
    }

    public struct CurlResult
    {
        public CURLcode response;
        public string data;
    }
}
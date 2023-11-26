using Protocal.Enum.Socks;

namespace Protocal.Request.Socks;

public class HandShakeRequest
{
    public int Version { get; set; }
    public List<AuthMethod> AuthMethods { get; set; }

    public static HandShakeRequest FromBytes(byte[] bytes)
    {
        var version = (ushort)bytes[0];
        var authMethods = new List<AuthMethod>();
        for (var i = 0; i < bytes[1]; i++) authMethods.Add((AuthMethod)bytes[i + 2]);

        return new HandShakeRequest
        {
            Version = version,
            AuthMethods = authMethods
        };
    }

    public byte[] ToBytes()
    {
        var bytes = new byte[AuthMethods.Count + 2];
        bytes[0] = (byte)Version;
        bytes[1] = (byte)AuthMethods.Count;
        for (var i = 0; i < AuthMethods.Count; i++) bytes[i + 2] = (byte)AuthMethods[i];
        return bytes;
    }

    public byte[] GetAcceptBytes(int version, List<AuthMethod> supportedAuthMethods)
    {
        AuthMethod? selectedAuthMethod = null;
        if (Version == version)
            foreach (var method in AuthMethods)
            foreach (var supportedMethod in supportedAuthMethods)
                if (supportedMethod.Equals(method))
                    selectedAuthMethod = supportedMethod;

        var buffer = new byte[2];
        buffer[0] = (byte)version;
        buffer[1] = (byte)(selectedAuthMethod ?? AuthMethod.NotSupported);
        return buffer;
    }
}
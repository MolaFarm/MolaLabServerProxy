using Protocol.Enum.Quic;

namespace Protocol.Request.Quic;

public class HeartBeat
{
    public int Version { get; set; }
    public Actor Actor { get; set; }
    public bool IsAlive { get; set; }

    public static HeartBeat FromBytes(byte[] bytes)
    {
        return new HeartBeat
        {
            Version = bytes[0],
            Actor = (Actor)bytes[1],
            IsAlive = bytes[2] != 0
        };
    }

    public byte[] ToBytes()
    {
        byte[] bytes = { (byte)Version, (byte)Actor, (byte)(IsAlive ? 1 : 0) };
        return bytes;
    }
}
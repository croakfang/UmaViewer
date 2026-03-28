using System.IO;

public class UmaAssetBundleStream : FileStream
{
    private const int headerSize = 256;
    private readonly byte[] keys;

    public UmaAssetBundleStream(string filename, byte[] keys)
        : base(filename, FileMode.Open, FileAccess.Read, FileShare.Read)
    {
        this.keys = keys;
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        long startPos = Position;
        int res = base.Read(buffer, offset, count);
        if (res <= 0) return res;

        long skip = startPos < headerSize ? headerSize - startPos : 0;
        int start = (int)skip;
        if (start < 0) start = 0;
        if (start > res) return res;

        int keyLen = keys.Length;

        for (int i = start; i < res; i++)
        {
            int bufIndex = offset + i;
            long streamIndex = startPos + i;
            buffer[bufIndex] ^= keys[streamIndex % keyLen];
        }

        return res;
    }
}





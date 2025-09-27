using System;
using System.IO;

public static class AssetBundleDecryptor
{
    // Ĭ��ֵ����֮ǰ�ṩ�ģ�
    private static readonly byte[] DefaultBaseKeys = new byte[]
    {
        0x53, 0x2B, 0x46, 0x31, 0xE4, 0xA7, 0xB9, 0x47, 0x3E, 0x7C, 0xFB
    };
    private const long DefaultKey = -7673907454518172050L;

    /// <summary>
    /// ʹ��Ĭ�� baseKeys + key �����ļ������ؽ��ܺ�� byte[]��һ���Զ�ȡȫ�����ڴ棩��
    /// ǰ 256 �ֽڱ���ԭ�������� XOR������ƫ�� 256 ��ʼ��ÿ���ֽ��� data[i] ^= keys[i % keys.Length]��
    /// </summary>
    /// <param name="inputFilePath">Ҫ���ܵ������ļ�·��</param>
    /// <returns>���ܺ���ֽ����飨��ֱ�Ӵ��� AssetBundle.LoadFromMemory��</returns>
    public static byte[] DecryptFileToBytes(string inputFilePath, long key)
    {
        return DecryptFileToBytes(inputFilePath, DefaultBaseKeys, key);
    }

    /// <summary>
    /// ͨ�ýӿڣ�ʹ��ָ���� baseKeys �� key �����ļ������ؽ��ܺ�� byte[]��
    /// </summary>
    /// <param name="inputFilePath">�����ļ�·����������ڣ�</param>
    /// <param name="baseKeys">baseKeys ���飨ÿ��Ԫ����һ�� byte��������Ϊÿ�� baseKeys Ԫ������ 8 �ֽڵ�һ�Σ�</param>
    /// <param name="key">int64 key��֧�ָ�������ת�� 8 �ֽ�С�� two's-complement ���� baseKeys ����Թ��� keys ƽ̹���飩</param>
    /// <returns>���ܺ���ֽ�����</returns>
    public static byte[] DecryptFileToBytes(string inputFilePath, byte[] baseKeys, long key)
    {
        if (string.IsNullOrEmpty(inputFilePath))
            throw new ArgumentNullException(nameof(inputFilePath));
        if (!File.Exists(inputFilePath))
            throw new FileNotFoundException("Input file not found", inputFilePath);
        if (baseKeys == null || baseKeys.Length == 0)
            throw new ArgumentException("baseKeys must not be null or empty", nameof(baseKeys));

        // ��ȡ�����ļ����ڴ棨�û�Ҫ�󲻷ֿ飩
        byte[] data = File.ReadAllBytes(inputFilePath);

        // ���� keyBytes��8 �ֽ�С�ˣ���ȷ����С����
        byte[] keyBytes = BitConverter.GetBytes(key);
        if (!BitConverter.IsLittleEndian)
            Array.Reverse(keyBytes);

        // ����ƽ̹ keys: �� baseKeys �е�ÿ���ֽ����� 8 ���ֽ�: base ^ keyBytes[j]
        int baseLen = baseKeys.Length;
        int keysLen = baseLen * 8;
        byte[] keys = new byte[keysLen];
        for (int i = 0; i < baseLen; ++i)
        {
            byte b = baseKeys[i];
            int baseOffset = i << 3; // i * 8
            for (int j = 0; j < 8; ++j)
            {
                keys[baseOffset + j] = (byte)(b ^ keyBytes[j]);
            }
        }

        // ����ļ����� <= 256����û���κ��ֽڱ� XOR��ֱ�ӷ���ԭ����
        if (data.Length <= 256)
            return data;

        // ��ƫ�� 256 ��ʼ����ÿ���ֽڰ� keys ѭ�������
        for (int i = 256; i < data.Length; ++i)
        {
            data[i] ^= keys[i % keysLen];
        }

        return data;
    }
}

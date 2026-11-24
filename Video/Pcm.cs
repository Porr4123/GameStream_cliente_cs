namespace GameStream.Client.Video;

/// <summary>Contas de PCM do som (sem dependência do Android, para poder ser testado em qualquer JVM/.NET).</summary>
public static class Pcm
{
    /// <summary>Maior amplitude (valor absoluto, 0 a 32768) de um buffer PCM 16 bits little-endian. 0 = silêncio total.</summary>
    public static int Peak16(byte[] pcm, int sizeBytes)
    {
        int peak = 0;
        for (int i = 0; i + 1 < sizeBytes; i += 2)
        {
            int v = (short)(pcm[i] | (pcm[i + 1] << 8));
            if (v < 0) v = -v;
            if (v > peak) peak = v;
        }
        return peak;
    }

    /// <summary>Converte PCM float (-1..1, little-endian, 4 bytes por amostra) em 16 bits. Devolve quantas amostras escreveu.</summary>
    public static int FloatToPcm16(byte[] pcmFloat, int sizeBytes, short[] destination)
    {
        int count = Math.Min(sizeBytes / 4, destination.Length);
        for (int i = 0; i < count; i++)
        {
            float v = BitConverter.ToSingle(pcmFloat, i * 4);
            destination[i] = (short)Math.Clamp((int)Math.Round(v * 32767f), -32768, 32767);
        }
        return count;
    }

    /// <summary>Maior amplitude de amostras de 16 bits já convertidas.</summary>
    public static int Peak16(short[] samples, int count)
    {
        int peak = 0;
        for (int i = 0; i < count; i++)
        {
            int v = samples[i];
            if (v < 0) v = -v;
            if (v > peak) peak = v;
        }
        return peak;
    }
}

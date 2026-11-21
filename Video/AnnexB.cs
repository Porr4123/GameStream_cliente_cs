namespace GameStream.Client.Video;

/// <summary>Utilidades de H.264 em Annex B (sem dependência do Android).</summary>
public static class AnnexB
{
    /// <summary>SPS e PPS, cada um com start code de 4 bytes (formato de csd-0/csd-1 do MediaCodec).</summary>
    public sealed record Params(byte[]? Sps, byte[]? Pps);

    /// <summary>Procura SPS (NAL 7) e PPS (NAL 8) em <paramref name="data"/>[offset..offset+length].</summary>
    public static Params ExtractParams(byte[] data, int offset, int length)
    {
        byte[]? sps = null;
        byte[]? pps = null;
        int end = offset + length;

        var starts = NalStarts(data, offset, end);
        for (int n = 0; n < starts.Count; n++)
        {
            int s = starts[n];
            int e = n + 1 < starts.Count ? starts[n + 1] - 3 : end;
            while (e > s && data[e - 1] == 0) e--; // zeros do start code de 4 bytes seguinte
            if (e <= s) continue;

            int type = data[s] & 0x1F;
            if (type == 7 && sps == null) sps = WithStartCode(data, s, e);
            else if (type == 8 && pps == null) pps = WithStartCode(data, s, e);
        }
        return new Params(sps, pps);
    }

    /// <summary>Posição do primeiro byte (cabeçalho) de cada NAL, logo depois de cada start code 00 00 01.</summary>
    public static List<int> NalStarts(byte[] d, int from, int end)
    {
        var result = new List<int>();
        int i = from;
        while (i + 3 < end)
        {
            if (d[i] == 0 && d[i + 1] == 0 && d[i + 2] == 1)
            {
                result.Add(i + 3);
                i += 3;
            }
            else
            {
                i++;
            }
        }
        return result;
    }

    private static byte[] WithStartCode(byte[] d, int from, int to)
    {
        var result = new byte[4 + to - from];
        result[3] = 1;
        Buffer.BlockCopy(d, from, result, 4, to - from);
        return result;
    }
}

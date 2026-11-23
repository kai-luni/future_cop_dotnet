namespace FutureCop.Assets;

/// <summary>PlayStation SPU ADPCM frames, 16 bytes to 28 signed PCM samples.</summary>
public static class PsAdpcm
{
    private static readonly int[] Coeff1 = { 0, 60, 115, 98, 122 };
    private static readonly int[] Coeff2 = { 0, 0, -52, -55, -60 };

    public static short[] Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length % 16 != 0) throw new InvalidDataException("ADPCM data is not frame aligned.");
        var output = new short[data.Length / 16 * 28];
        int s1 = 0, s2 = 0, dst = 0;
        for (int p = 0; p < data.Length; p += 16)
        {
            int shift = data[p] & 15, filter = data[p] >> 4;
            if (filter >= Coeff1.Length || shift > 12) throw new InvalidDataException("Invalid ADPCM predictor or shift.");
            for (int i = 0; i < 28; i++)
            {
                int nibble = (data[p + 2 + i / 2] >> ((i & 1) * 4)) & 15;
                int sample = ((short)(nibble << 12) >> shift) + ((s1 * Coeff1[filter] + s2 * Coeff2[filter] + 32) >> 6);
                sample = Math.Clamp(sample, short.MinValue, short.MaxValue);
                output[dst++] = (short)sample;
                s2 = s1; s1 = sample;
            }
        }
        return output;
    }
}

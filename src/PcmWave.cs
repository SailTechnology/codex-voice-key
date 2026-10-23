using System;
using System.IO;

namespace CodexVoiceKey
{
    internal static class PcmWave
    {
        internal static byte[] Resample(byte[] source, int sampleRate, int channels)
        {
            if (sampleRate < 1 || channels < 1) throw new ArgumentException("Invalid PCM format.");
            int stride = channels * 2, frames = source.Length / stride;
            int count = (int)Math.Round(frames * 16000.0 / sampleRate);
            byte[] output = new byte[count * 2];
            for (int i = 0; i < count; i++)
            {
                double position = i * sampleRate / 16000.0;
                int a = Math.Min((int)position, frames - 1), b = Math.Min(a + 1, frames - 1);
                double mixA = 0, mixB = 0;
                for (int channel = 0; channel < channels; channel++)
                {
                    mixA += BitConverter.ToInt16(source, a * stride + channel * 2);
                    mixB += BitConverter.ToInt16(source, b * stride + channel * 2);
                }
                double t = position - a;
                short sample = (short)Math.Round((mixA * (1 - t) + mixB * t) / channels);
                output[i * 2] = (byte)(sample & 0xff);
                output[i * 2 + 1] = (byte)((sample >> 8) & 0xff);
            }
            return output;
        }

        internal static void Write(string path, byte[] source, int sampleRate, int channels)
        {
            byte[] pcm = Resample(source, sampleRate, channels);
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
            using (BinaryWriter writer = new BinaryWriter(File.Create(path)))
            {
                writer.Write(new byte[] { 82, 73, 70, 70 }); writer.Write(36 + pcm.Length);
                writer.Write(new byte[] { 87, 65, 86, 69, 102, 109, 116, 32 }); writer.Write(16);
                writer.Write((short)1); writer.Write((short)1); writer.Write(16000); writer.Write(32000);
                writer.Write((short)2); writer.Write((short)16);
                writer.Write(new byte[] { 100, 97, 116, 97 }); writer.Write(pcm.Length); writer.Write(pcm);
            }
        }
    }
}

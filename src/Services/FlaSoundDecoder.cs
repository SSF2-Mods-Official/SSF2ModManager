using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using NLayer;

namespace SSF2ModManager.Services;

/// <summary>
/// Decodes JPEXS FLA sound bins (FLV-wrapped MP3/PCM, or raw MP3) to PCM16 WAV
/// without calling ffmpeg.
/// </summary>
public static class FlaSoundDecoder
{
    public static bool TryDecodeToPcm16Wav(byte[] raw, out byte[] wav)
    {
        wav = Array.Empty<byte>();
        if (raw == null || raw.Length < 4)
            return false;

        var kind = JpexsCliService.SniffSoundMagic(raw);
        if (kind == JpexsCliService.SoundMediaKind.RiffWav &&
            JpexsCliService.TryReadWavFormat(raw, out var info) &&
            info.IsPcm16)
        {
            wav = raw;
            return true;
        }

        if (kind == JpexsCliService.SoundMediaKind.Flv)
        {
            if (!TryExtractFlvAudio(raw, out var payload, out var codec, out var rate, out var channels, out var bits))
                return false;
            if (codec is 0 or 3)
                return TryWrapPcm(payload, rate, Math.Max(1, channels), bits <= 0 ? 16 : bits, out wav);
            if (codec == 2)
                return TryDecodeMp3(payload, out wav);
            return false;
        }

        if (kind == JpexsCliService.SoundMediaKind.Mp3)
            return TryDecodeMp3(raw, out wav);

        return false;
    }

    internal static bool TryExtractFlvAudio(
        byte[] flv,
        out byte[] payload,
        out int codec,
        out int sampleRate,
        out int channels,
        out int bits)
    {
        payload = Array.Empty<byte>();
        codec = 0;
        sampleRate = 0;
        channels = 1;
        bits = 16;
        if (flv.Length < 13 || flv[0] != (byte)'F' || flv[1] != (byte)'L' || flv[2] != (byte)'V')
            return false;

        var pos = 9;
        var chunks = new List<byte[]>();
        while (pos + 15 <= flv.Length)
        {
            pos += 4;
            if (pos + 11 > flv.Length)
                break;
            var type = flv[pos];
            var size = (flv[pos + 1] << 16) | (flv[pos + 2] << 8) | flv[pos + 3];
            var dataStart = pos + 11;
            var dataEnd = dataStart + size;
            if (size < 0 || dataEnd > flv.Length)
                break;
            if (type == 8 && size >= 1)
            {
                var flags = flv[dataStart];
                codec = flags >> 4;
                sampleRate = RateFromFlvFlags(flags);
                bits = ((flags >> 1) & 1) == 1 ? 16 : 8;
                channels = (flags & 1) == 1 ? 2 : 1;
                var header = 1;
                if (codec == 10 && size >= 2)
                    header = 2;
                if (dataStart + header < dataEnd)
                {
                    var slice = new byte[dataEnd - dataStart - header];
                    Buffer.BlockCopy(flv, dataStart + header, slice, 0, slice.Length);
                    chunks.Add(slice);
                }
            }
            pos = dataEnd;
        }

        if (chunks.Count == 0)
            return false;
        var total = 0;
        foreach (var c in chunks)
            total += c.Length;
        payload = new byte[total];
        var o = 0;
        foreach (var c in chunks)
        {
            Buffer.BlockCopy(c, 0, payload, o, c.Length);
            o += c.Length;
        }
        return payload.Length > 0;
    }

    private static int RateFromFlvFlags(int flags) =>
        ((flags >> 2) & 3) switch
        {
            0 => 5500,
            1 => 11025,
            2 => 22050,
            _ => 44100
        };

    private static bool TryWrapPcm(byte[] pcm, int sampleRate, int channels, int bits, out byte[] wav)
    {
        wav = Array.Empty<byte>();
        if (pcm.Length == 0 || sampleRate <= 0 || channels <= 0)
            return false;
        if (bits != 8 && bits != 16)
            bits = 16;
        wav = WriteWav(pcm, sampleRate, channels, bits);
        return true;
    }

    private static bool TryDecodeMp3(byte[] mp3, out byte[] wav)
    {
        wav = Array.Empty<byte>();
        try
        {
            using var stream = new MemoryStream(mp3, writable: false);
            using var mpeg = new MpegFile(stream);
            if (mpeg.SampleRate <= 0 || mpeg.Channels <= 0)
                return false;
            var buf = new float[8192];
            var samples = new List<float>(8192);
            int n;
            while ((n = mpeg.ReadSamples(buf, 0, buf.Length)) > 0)
            {
                for (var i = 0; i < n; i++)
                    samples.Add(buf[i]);
            }
            if (samples.Count == 0)
                return false;
            var pcm = new byte[samples.Count * 2];
            for (var i = 0; i < samples.Count; i++)
            {
                var s = (int)Math.Round(Math.Clamp(samples[i], -1f, 1f) * 32767f);
                BinaryPrimitives.WriteInt16LittleEndian(pcm.AsSpan(i * 2), (short)s);
            }
            wav = WriteWav(pcm, mpeg.SampleRate, mpeg.Channels, 16);
            return true;
        }
        catch
        {
            wav = Array.Empty<byte>();
            return false;
        }
    }

    internal static byte[] WriteWav(byte[] pcm, int sampleRate, int channels, int bits)
    {
        var block = Math.Max(1, channels * (bits / 8));
        var bytes = new byte[44 + pcm.Length];
        System.Text.Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        BitConverter.GetBytes(36 + pcm.Length).CopyTo(bytes, 4);
        System.Text.Encoding.ASCII.GetBytes("WAVE").CopyTo(bytes, 8);
        System.Text.Encoding.ASCII.GetBytes("fmt ").CopyTo(bytes, 12);
        BitConverter.GetBytes(16).CopyTo(bytes, 16);
        BitConverter.GetBytes((ushort)1).CopyTo(bytes, 20);
        BitConverter.GetBytes((ushort)channels).CopyTo(bytes, 22);
        BitConverter.GetBytes(sampleRate).CopyTo(bytes, 24);
        BitConverter.GetBytes(sampleRate * block).CopyTo(bytes, 28);
        BitConverter.GetBytes((ushort)block).CopyTo(bytes, 32);
        BitConverter.GetBytes((ushort)bits).CopyTo(bytes, 34);
        System.Text.Encoding.ASCII.GetBytes("data").CopyTo(bytes, 36);
        BitConverter.GetBytes(pcm.Length).CopyTo(bytes, 40);
        Buffer.BlockCopy(pcm, 0, bytes, 44, pcm.Length);
        return bytes;
    }
}

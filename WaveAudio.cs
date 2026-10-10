using System;
using System.IO;
using System.Text;
using System.Threading;

// PCM 16/24/32 and IEEE float32 WAV, including WAVE_FORMAT_EXTENSIBLE.
// Converts a copy for Whisper; the reference used for voice fitting stays original.
static class WaveAudio {
    internal static void ForWhisper(string input, string output, CancellationToken token) {
        using (var source = new BinaryReader(File.OpenRead(input))) {
            if (new string(source.ReadChars(4)) != "RIFF") throw new InvalidDataException("Нужен RIFF WAV.");
            source.ReadUInt32(); if (new string(source.ReadChars(4)) != "WAVE") throw new InvalidDataException("Неверный WAV.");
            ushort format = 0, channels = 0, bits = 0, align = 0; int rate = 0;
            long dataOffset = 0, dataSize = 0;
            while (source.BaseStream.Position + 8 <= source.BaseStream.Length) {
                string chunk = new string(source.ReadChars(4)); uint length = source.ReadUInt32();
                long next = source.BaseStream.Position + length + (length % 2);
                if (next > source.BaseStream.Length + 1) throw new InvalidDataException("Обрезанный WAV.");
                if (chunk == "fmt ") {
                    if (length < 16) throw new InvalidDataException("Неверный fmt WAV.");
                    format = source.ReadUInt16(); channels = source.ReadUInt16(); rate = source.ReadInt32();
                    source.ReadUInt32(); align = source.ReadUInt16(); bits = source.ReadUInt16();
                    if (format == 65534 && length >= 40) { source.ReadUInt16(); source.ReadUInt16(); source.ReadUInt32(); format = source.ReadUInt16(); }
                } else if (chunk == "data") { dataOffset = source.BaseStream.Position; dataSize = length; }
                source.BaseStream.Position = Math.Min(next, source.BaseStream.Length);
            }
            if (channels == 0 || channels > 32 || rate < 8000 || rate > 384000 || align != channels * (bits / 8) || dataOffset == 0 || dataSize == 0 || dataSize % align != 0)
                throw new InvalidDataException("Некорректные параметры WAV.");
            if (!(format == 1 && (bits == 16 || bits == 24 || bits == 32)) && !(format == 3 && bits == 32))
                throw new InvalidDataException("Поддерживаются WAV PCM 16/24/32 и float32.");
            long frames = dataSize / align, count = (long)Math.Floor(frames * 16000.0 / rate);
            if (count < 1600 || count > (UInt32.MaxValue - 36) / 2) throw new InvalidDataException("Запись слишком короткая или слишком длинная.");
            Directory.CreateDirectory(Path.GetDirectoryName(output));
            source.BaseStream.Position = dataOffset;
            using (var target = new BinaryWriter(File.Create(output))) {
                target.Write(Encoding.ASCII.GetBytes("RIFF")); target.Write((uint)(36 + count * 2)); target.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
                target.Write(16); target.Write((ushort)1); target.Write((ushort)1); target.Write(16000); target.Write(32000); target.Write((ushort)2); target.Write((ushort)16);
                target.Write(Encoding.ASCII.GetBytes("data")); target.Write((uint)(count * 2));
                // Integrate each input sample into its destination interval. This averages
                // channels and limits aliasing when downsampling, without boosting noise.
                double sum = 0, weight = 0; long written = 0;
                for (long i = 0; i < frames && written < count; i++) {
                    if ((i & 4095) == 0) token.ThrowIfCancellationRequested();
                    double value = 0;
                    for (int c = 0; c < channels; c++) {
                        double sample;
                        if (format == 3) sample = source.ReadSingle();
                        else if (bits == 16) sample = source.ReadInt16() / 32768.0;
                        else if (bits == 32) sample = source.ReadInt32() / 2147483648.0;
                        else { int n = source.ReadByte() | source.ReadByte() << 8 | source.ReadByte() << 16; if ((n & 8388608) != 0) n |= unchecked((int)0xff000000); sample = n / 8388608.0; }
                        if (Double.IsNaN(sample) || Double.IsInfinity(sample)) throw new InvalidDataException("WAV содержит некорректные отсчёты.");
                        value += sample / channels;
                    }
                    double a = i * 16000.0 / rate, b = (i + 1) * 16000.0 / rate;
                    while (a < b && written < count) {
                        double edge = Math.Min(b, written + 1.0), part = edge - a;
                        if (part <= 0) break;
                        sum += value * part; weight += part; a = edge;
                        if (a >= written + 1.0 - 1e-9) {
                            double v = weight > 0 ? sum / weight : 0;
                            target.Write((short)Math.Round(Math.Max(-1, Math.Min(32767.0 / 32768, v)) * 32768));
                            sum = weight = 0; written++;
                        }
                    }
                }
                if (written != count) throw new InvalidDataException("Не удалось преобразовать WAV целиком.");
            }
        }
    }
}

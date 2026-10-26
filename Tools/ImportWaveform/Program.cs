using System;
using System.IO;
using System.Linq;
using Mizon.Database;
using Mizon.Database.Entities;
using Microsoft.EntityFrameworkCore;
using NAudio.Wave;

namespace ImportWaveform;

internal static class Program
{
    private const string InputPath = @"C:\Users\Ali\Desktop\New folder (2)\Mahsun - Dinle Live Version (320).mp3";
    private const int TargetMusicFileId = 2;

    private static int Main(string[] args)
    {
        Console.WriteLine("Import waveform tool starting...");

        if (!File.Exists(InputPath))
        {
            Console.WriteLine($"File not found: {InputPath}");
            return 1;
        }

        var optionsBuilder = new DbContextOptionsBuilder<MizonDatabaseContext>();
        optionsBuilder.UseSqlServer("Server=localhost;Database=Mizon;Trusted_Connection=True;TrustServerCertificate=True");

        using var db = new MizonDatabaseContext(optionsBuilder.Options);

        try
        {
            // Remove existing waveforms for this music file id
            var existing = db.MusicFileWaveforms.Where(x => x.MusicFileId == TargetMusicFileId);
            db.MusicFileWaveforms.RemoveRange(existing);
            db.SaveChanges();

            using var reader = new AudioFileReader(InputPath);

            int sampleRate = reader.WaveFormat.SampleRate;
            int channels = reader.WaveFormat.Channels;

            // frame duration in ms
            const int frameMs = 50; // 50 ms per waveform sample
            int samplesPerFrame = (int)((sampleRate * frameMs) / 1000.0) * channels;

            float[] buffer = new float[samplesPerFrame];
            long offsetMs = 0;
            float prevRms = 0f;

            while (true)
            {
                int read = reader.Read(buffer, 0, buffer.Length);
                if (read == 0) break;

                // compute min, max, rms, peak
                float min = float.MaxValue;
                float max = float.MinValue;
                double sumSq = 0;
                float peak = 0f;

                for (int i = 0; i < read; i++)
                {
                    float s = buffer[i];
                    if (s < min) min = s;
                    if (s > max) max = s;
                    sumSq += s * s;
                    peak = Math.Max(peak, Math.Abs(s));
                }

                float rms = read > 0 ? (float)Math.Sqrt(sumSq / read) : 0f;
                float transient = Math.Abs(rms - prevRms);
                prevRms = rms;

                short minShort = (short)Math.Clamp((int)(min * short.MaxValue), short.MinValue, short.MaxValue);
                short maxShort = (short)Math.Clamp((int)(max * short.MaxValue), short.MinValue, short.MaxValue);
                short peakShort = (short)Math.Clamp((int)(peak * short.MaxValue), short.MinValue, short.MaxValue);

                var wf = new MusicFileWaveform
                {
                    MusicFileId = TargetMusicFileId,
                    OffsetInMilliseconds = (int)offsetMs,
                    MinimumAmplitude = minShort,
                    MaximumAmplitude = maxShort,
                    RMS = rms,
                    Peak = peakShort,
                    Transient = transient,
                    IsClipping = peak >= 0.999f
                };

                db.MusicFileWaveforms.Add(wf);

                // advance offset
                offsetMs += frameMs;
            }

            db.SaveChanges();

            Console.WriteLine("Waveform import completed successfully.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error: " + ex.Message);
            return 2;
        }
    }
}

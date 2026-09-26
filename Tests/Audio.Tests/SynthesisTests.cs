using System;
using System.Collections.Generic;
using System.IO;
using GrandStrategy.Game.Audio.Synthesis;
using Xunit;

namespace GrandStrategy.Game.Audio.Tests
{
    public class SynthesisTests
    {
        public static IEnumerable<object[]> Moods()
        {
            foreach (MusicMood mood in Enum.GetValues(typeof(MusicMood)))
                if (mood != MusicMood.None)
                    yield return new object[] { mood };
        }

        public static IEnumerable<object[]> Effects()
        {
            foreach (Sfx sfx in Enum.GetValues(typeof(Sfx)))
                yield return new object[] { sfx };
        }

        [Theory]
        [MemberData(nameof(Moods))]
        public void EveryMoodHasAPlayableLoop(MusicMood mood)
        {
            var buffer = MusicComposer.Render(mood);
            Assert.Equal(2, buffer.Channels);
            Assert.InRange(buffer.Seconds, 15, 150);
            AssertHealthy(buffer, minRms: 0.03);
            WriteWavIfRequested(buffer, "music_" + mood);
        }

        [Theory]
        [MemberData(nameof(Effects))]
        public void EverySoundEffectRenders(Sfx sfx)
        {
            var buffer = SfxLibrary.Render(sfx);
            Assert.InRange(buffer.Seconds, 0.03, 6);
            AssertHealthy(buffer, minRms: 0.005);
            WriteWavIfRequested(buffer, "sfx_" + AudioIds.FileName(sfx));
        }

        [Fact]
        public void LoopsJoinWithoutAClick()
        {
            // The last and first frames of a loop should be close, or looping would click.
            var buffer = MusicComposer.Render(MusicMood.Peace);
            var s = buffer.Samples;
            float jump = Math.Abs(s[0] - s[s.Length - 2]);
            Assert.True(jump < 0.2f, $"Loop seam jumps by {jump}");
        }

        [Fact]
        public void SfxFileNamesAreSnakeCase()
        {
            Assert.Equal("ui_click", AudioIds.FileName(Sfx.UiClick));
            Assert.Equal("capital_battle_start", AudioIds.FileName(Sfx.CapitalBattleStart));
        }

        static void AssertHealthy(AudioBuffer buffer, double minRms)
        {
            double sum = 0;
            float peak = 0;
            foreach (var x in buffer.Samples)
            {
                Assert.False(float.IsNaN(x) || float.IsInfinity(x), "Sample is not a finite number");
                peak = Math.Max(peak, Math.Abs(x));
                sum += x * x;
            }
            double rms = Math.Sqrt(sum / buffer.Samples.Length);
            Assert.InRange(peak, 0.1f, 0.95f);
            Assert.True(rms >= minRms, $"Too quiet: RMS {rms:F4}");
        }

        /// <summary>Set GS_AUDIO_DUMP=/some/dir to write every render as a .wav file for listening.</summary>
        static void WriteWavIfRequested(AudioBuffer buffer, string name)
        {
            var dir = Environment.GetEnvironmentVariable("GS_AUDIO_DUMP");
            if (string.IsNullOrEmpty(dir))
                return;
            Directory.CreateDirectory(dir);
            using var w = new BinaryWriter(File.Create(Path.Combine(dir, name + ".wav")));
            int bytes = buffer.Samples.Length * 2;
            w.Write("RIFF".ToCharArray()); w.Write(36 + bytes); w.Write("WAVE".ToCharArray());
            w.Write("fmt ".ToCharArray()); w.Write(16); w.Write((short)1); w.Write((short)buffer.Channels);
            w.Write(buffer.SampleRate); w.Write(buffer.SampleRate * buffer.Channels * 2);
            w.Write((short)(buffer.Channels * 2)); w.Write((short)16);
            w.Write("data".ToCharArray()); w.Write(bytes);
            foreach (var x in buffer.Samples)
                w.Write((short)Math.Round(Math.Max(-1, Math.Min(1, x)) * 32767));
        }
    }
}

using System;
using System.Collections.Generic;

namespace GrandStrategy.Game.Audio.Synthesis
{
    /// <summary>
    /// Composes and renders the placeholder soundtrack: one seamless loop per mood.
    /// Real music files placed in Resources/Audio/Music/&lt;Mood&gt; replace these automatically.
    /// </summary>
    public static class MusicComposer
    {
        public const int DefaultSampleRate = 32000;

        static readonly int[] Major = { 0, 2, 4, 5, 7, 9, 11 };
        static readonly int[] Minor = { 0, 2, 3, 5, 7, 8, 10 };

        public static AudioBuffer Render(MusicMood mood, int sampleRate = DefaultSampleRate)
        {
            switch (mood)
            {
                case MusicMood.Menu: return Anthem(sampleRate);
                case MusicMood.Peace: return Peace(sampleRate);
                case MusicMood.Tension: return Tension(sampleRate);
                case MusicMood.War: return War(sampleRate);
                case MusicMood.CapitalBattle: return CapitalBattle(sampleRate);
                case MusicMood.Victory: return Victory(sampleRate);
                case MusicMood.Defeat: return Defeat(sampleRate);
                default: throw new ArgumentOutOfRangeException(nameof(mood), mood, "No music for this mood.");
            }
        }

        // ------------------------------------------------------------------ song helpers

        sealed class Song
        {
            public readonly double Bpm;
            public readonly int Tonic;   // MIDI note of the key's tonic (octave 4)
            public readonly int[] Scale;
            public readonly int[] Progression; // scale degree (0-based) per bar
            public readonly Mix Mix;
            public readonly Random Rng;
            public readonly double LoopSeconds;

            public Song(int sampleRate, double bpm, int tonic, int[] scale, int[] progression, int seed)
            {
                Bpm = bpm;
                Tonic = tonic;
                Scale = scale;
                Progression = progression;
                Rng = new Random(seed);
                LoopSeconds = Bars * BarSeconds;
                Mix = new Mix(sampleRate, LoopSeconds + 5.0); // room for release and reverb tails
            }

            public int Bars => Progression.Length;
            public double Beat => 60.0 / Bpm;
            public double BarSeconds => Beat * 4;
            public double T(int bar, double beat) => (bar * 4 + beat) * Beat;
            bool IsMinor => Scale == Minor;

            /// <summary>MIDI note for a scale degree (can be negative or above 7) relative to the tonic.</summary>
            public int Note(int degree, int octaveShift = 0)
            {
                int oct = (int)Math.Floor(degree / 7.0);
                int idx = degree - oct * 7;
                return Tonic + Scale[idx] + 12 * (oct + octaveShift);
            }

            /// <summary>Triad on a scale degree. In minor keys the V chord is major (harmonic minor).</summary>
            public int[] Chord(int degree, int octaveShift = 0)
            {
                var notes = new[] { Note(degree, octaveShift), Note(degree + 2, octaveShift), Note(degree + 4, octaveShift) };
                if (IsMinor && ((degree % 7) + 7) % 7 == 4)
                    notes[1] += 1; // raised leading tone
                return notes;
            }

            public int Root(int bar, int octaveShift = 0) => Note(Progression[bar], octaveShift);

            public AudioBuffer Render(float wet, double room, double damp) => Mix.Render(wet, room, damp, LoopSeconds);
        }

        /// <summary>Stepwise melody that lands on chord tones on strong beats.</summary>
        static void Melody(Song s, int fromBar, int toBar, int octaveShift, Action<double, double, int> note, double restChance = 0.1)
        {
            int[][] rhythms =
            {
                new[] { 2, 1, 1 }, new[] { 1, 1, 2 }, new[] { 3, 1 }, new[] { 1, 1, 1, 1 },
                new[] { 2, 2 }, new[] { 1, 2, 1 },
            };
            int degree = s.Progression[fromBar] + 7 * octaveShift + 2;
            for (int bar = fromBar; bar < toBar; bar++)
            {
                bool phraseEnd = (bar - fromBar) % 4 == 3;
                var rhythm = phraseEnd ? new[] { 4 } : rhythms[s.Rng.Next(rhythms.Length)];
                double beat = 0;
                int chordRoot = s.Progression[bar];
                for (int k = 0; k < rhythm.Length; k++)
                {
                    if (k == 0 || phraseEnd)
                    {
                        // Snap to the nearest chord tone.
                        int best = degree, bestDist = int.MaxValue;
                        for (int c = -14; c <= 14; c++)
                        {
                            int cand = chordRoot + c;
                            int rel = ((cand - chordRoot) % 7 + 7) % 7;
                            if (rel != 0 && rel != 2 && rel != 4) continue;
                            int dist = Math.Abs(cand - degree);
                            if (dist < bestDist) { bestDist = dist; best = cand; }
                        }
                        degree = best;
                    }
                    else
                    {
                        degree += s.Rng.Next(2) == 0 ? -1 : 1;
                        if (s.Rng.NextDouble() < 0.2) degree += s.Rng.Next(2) == 0 ? -1 : 1;
                    }
                    // Keep the tune in a comfortable range around the chosen octave.
                    int low = 7 * octaveShift - 2, high = 7 * octaveShift + 9;
                    if (degree < low) degree += 2;
                    if (degree > high) degree -= 2;

                    if (!(s.Rng.NextDouble() < restChance && !phraseEnd && k > 0))
                        note(s.T(bar, beat), rhythm[k] * s.Beat, s.Note(degree));
                    beat += rhythm[k];
                }
            }
        }

        static void PadChords(Song s, double velocity, double brightness, int octaveShift = -1, double attack = 0.6)
        {
            for (int bar = 0; bar < s.Bars; bar++)
            {
                var chord = s.Chord(s.Progression[bar], octaveShift);
                float[] pans = { -0.45f, 0.0f, 0.45f };
                for (int k = 0; k < chord.Length; k++)
                    Instruments.StringPad(s.Mix.Voice(s.T(bar, 0), pans[k], 0.45f), s.BarSeconds, chord[k], velocity, brightness, attack);
                Instruments.StringPad(s.Mix.Voice(s.T(bar, 0), 0f, 0.35f), s.BarSeconds, s.Root(bar, octaveShift - 1), velocity * 0.8, brightness * 0.7, attack);
            }
        }

        // ------------------------------------------------------------------ moods

        /// <summary>Menu / nation select: a proud, slow anthem in D major.</summary>
        static AudioBuffer Anthem(int sr)
        {
            var s = new Song(sr, 76, 62, Major, new[] { 0, 4, 5, 3, 0, 4, 3, 4, 5, 3, 0, 4, 5, 3, 4, 0 }, 11);
            PadChords(s, 0.30, 0.9);
            for (int bar = 0; bar < s.Bars; bar++)
            {
                Instruments.Bass(s.Mix.Voice(s.T(bar, 0), 0f, 0.1f), s.Beat * 2, s.Root(bar, -2), 0.45);
                Instruments.Bass(s.Mix.Voice(s.T(bar, 2), 0f, 0.1f), s.Beat * 2, s.Root(bar, -2), 0.40);
                int timpaniNote = s.Progression[bar] == 4 ? s.Note(4, -2) : s.Note(0, -2);
                Instruments.Timpani(s.Mix.Voice(s.T(bar, 0), -0.1f, 0.3f), timpaniNote, 0.5);
                if (bar >= 8)
                {
                    var chord = s.Chord(s.Progression[bar], -1);
                    foreach (var n in chord)
                    {
                        Instruments.Brass(s.Mix.Voice(s.T(bar, 0), 0.2f, 0.4f), s.Beat * 1.8, n, 0.20);
                        Instruments.Brass(s.Mix.Voice(s.T(bar, 2), 0.2f, 0.4f), s.Beat * 1.8, n, 0.17);
                    }
                }
                if (bar == 7 || bar == 15)
                    for (int k = 0; k < 8; k++)
                        Instruments.Timpani(s.Mix.Voice(s.T(bar, 2 + k * 0.25), -0.1f, 0.3f), s.Note(0, -2), 0.2 + 0.05 * k);
            }
            Instruments.CymbalSwell(s.Mix.Voice(s.T(7, 0), 0.3f, 0.5f), s.BarSeconds, 0.6);
            Melody(s, 0, s.Bars, 1, (t, d, n) => Instruments.Bell(s.Mix.Voice(t, 0.25f, 0.5f), n, 0.22, 2.5));
            Melody(s, 8, s.Bars, 0, (t, d, n) => Instruments.Brass(s.Mix.Voice(t, -0.2f, 0.4f), d * 0.95, n, 0.22));
            return s.Render(0.9f, 0.85, 0.35);
        }

        /// <summary>Peacetime on the world map: calm harp and strings in F major.</summary>
        static AudioBuffer Peace(int sr)
        {
            var s = new Song(sr, 66, 65, Major, new[] { 0, 2, 3, 0, 5, 3, 1, 4, 0, 2, 3, 0, 5, 3, 4, 0, 3, 4, 2, 5, 1, 4, 0, 0 }, 23);
            PadChords(s, 0.22, 0.6, -1, 1.2);
            int[] pattern = { 0, 1, 2, 3, 4, 3, 2, 1 };
            for (int bar = 0; bar < s.Bars; bar++)
            {
                Instruments.Bass(s.Mix.Voice(s.T(bar, 0), 0f, 0.15f), s.BarSeconds * 0.95, s.Root(bar, -2), 0.35);
                int root = s.Progression[bar];
                for (int k = 0; k < 8; k++)
                {
                    int step = pattern[k];
                    int degree = root + (step % 3) * 2 + 7 * (step / 3);
                    float pan = k % 2 == 0 ? -0.3f : 0.3f;
                    Instruments.Pluck(s.Mix.Voice(s.T(bar, k * 0.5), pan, 0.5f), s.Note(degree), 0.20 - 0.02 * (k % 2), 1.2);
                }
            }
            Melody(s, 4, 8, 1, (t, d, n) => Instruments.Bell(s.Mix.Voice(t, 0.35f, 0.6f), n, 0.13, 3.0), 0.3);
            Melody(s, 12, 16, 1, (t, d, n) => Instruments.Bell(s.Mix.Voice(t, -0.35f, 0.6f), n, 0.13, 3.0), 0.3);
            Melody(s, 16, 24, 0, (t, d, n) => Instruments.StringPad(s.Mix.Voice(t, 0.1f, 0.5f), d, n, 0.16, 1.1, 0.25), 0.2);
            return s.Render(0.85f, 0.88, 0.4);
        }

        /// <summary>Rising tension (crises, alerts): pulsing low strings over a drone in A minor.</summary>
        static AudioBuffer Tension(int sr)
        {
            var s = new Song(sr, 70, 57, Minor, new[] { 0, 5, 3, 4, 0, 5, 6, 4, 0, 3, 5, 4, 0, 5, 3, 4, 5, 6, 0, 4, 3, 5, 4, 4 }, 37);
            for (int bar = 0; bar < s.Bars; bar += 4)
            {
                Instruments.StringPad(s.Mix.Voice(s.T(bar, 0), -0.2f, 0.4f), s.BarSeconds * 4, s.Note(0, -2), 0.25, 0.5, 1.5);
                Instruments.StringPad(s.Mix.Voice(s.T(bar, 0), 0.2f, 0.4f), s.BarSeconds * 4, s.Note(4, -2), 0.18, 0.5, 1.5);
            }
            for (int bar = 0; bar < s.Bars; bar++)
            {
                var chord = s.Chord(s.Progression[bar], 0);
                foreach (var n in chord)
                    Instruments.Choir(s.Mix.Voice(s.T(bar, 0), 0f, 0.6f), s.BarSeconds, n, 0.12);
                for (int k = 0; k < 8; k++)
                    Instruments.StringStaccato(s.Mix.Voice(s.T(bar, k * 0.5), -0.15f, 0.25f), s.Beat * 0.3, s.Root(bar, -1), k % 2 == 0 ? 0.26 : 0.18);
                if (bar % 2 == 0)
                    Instruments.Timpani(s.Mix.Voice(s.T(bar, 0), 0f, 0.4f), s.Note(0, -2), 0.35);
            }
            Melody(s, 8, 16, 1, (t, d, n) => Instruments.Bell(s.Mix.Voice(t, 0.4f, 0.7f), n, 0.10, 3.0), 0.45);
            Melody(s, 16, 24, 0, (t, d, n) => Instruments.Brass(s.Mix.Voice(t, -0.3f, 0.5f), d, n - 12, 0.16), 0.3);
            return s.Render(0.9f, 0.9, 0.45);
        }

        /// <summary>At war: taiko, driving string ostinato and brass in D minor.</summary>
        static AudioBuffer War(int sr)
        {
            var s = new Song(sr, 96, 62, Minor, new[] { 0, 5, 2, 6, 0, 5, 3, 4, 0, 5, 2, 6, 3, 5, 4, 4, 5, 3, 0, 4, 5, 6, 4, 4 }, 53);
            PadChords(s, 0.16, 0.8);
            int[] ostinato = { 0, 0, 7, 0, 12, 0, 7, 0, 0, 0, 7, 0, 12, 10, 7, 3 };
            for (int bar = 0; bar < s.Bars; bar++)
            {
                double[] hits = bar % 4 == 3 ? new[] { 0, 1.5, 2, 3, 3.25, 3.5, 3.75 } : new[] { 0, 1.5, 2, 3 };
                foreach (var h in hits)
                    Instruments.Taiko(s.Mix.Voice(s.T(bar, h), 0f, 0.25f), h == 0 ? 0.95 : 0.7, h == 0 ? 1.0 : 1.15);
                Instruments.Snare(s.Mix.Voice(s.T(bar, 1), 0.3f, 0.3f), 0.22);
                Instruments.Snare(s.Mix.Voice(s.T(bar, 3), 0.3f, 0.3f), 0.22);

                int root = s.Root(bar, -1);
                for (int k = 0; k < 16; k++)
                    Instruments.StringStaccato(s.Mix.Voice(s.T(bar, k * 0.25), -0.25f, 0.2f), s.Beat * 0.2, root + ostinato[k], k % 4 == 0 ? 0.24 : 0.17);

                for (int k = 0; k < 4; k++)
                    Instruments.Bass(s.Mix.Voice(s.T(bar, k), 0f, 0.05f), s.Beat * 0.8, s.Root(bar, -2), 0.38);

                foreach (var n in s.Chord(s.Progression[bar], -1))
                {
                    Instruments.Brass(s.Mix.Voice(s.T(bar, 0), 0.25f, 0.35f), s.Beat * 0.7, n, 0.18);
                    Instruments.Brass(s.Mix.Voice(s.T(bar, 2.5), 0.25f, 0.35f), s.Beat * 0.45, n, 0.14);
                }
            }
            Instruments.CymbalSwell(s.Mix.Voice(s.T(7, 0), 0.2f, 0.4f), s.BarSeconds, 0.6);
            Instruments.CymbalSwell(s.Mix.Voice(s.T(15, 0), -0.2f, 0.4f), s.BarSeconds, 0.6);
            Melody(s, 8, 24, 0, (t, d, n) => Instruments.Brass(s.Mix.Voice(t, -0.15f, 0.4f), d * 0.9, n, 0.24), 0.1);
            return s.Render(0.7f, 0.8, 0.4);
        }

        /// <summary>The Capital Battle: fast, heavy drums, choir and brass in C minor.</summary>
        static AudioBuffer CapitalBattle(int sr)
        {
            var s = new Song(sr, 118, 60, Minor, new[] { 0, 3, 6, 2, 5, 3, 4, 4, 0, 3, 6, 2, 5, 6, 4, 4 }, 71);
            int[] ostinato = { 0, 12, 7, 12, 0, 12, 7, 12, 0, 12, 7, 12, 3, 12, 7, 10 };
            for (int bar = 0; bar < s.Bars; bar++)
            {
                for (int b = 0; b < 4; b++)
                    Instruments.Taiko(s.Mix.Voice(s.T(bar, b), 0f, 0.2f), b == 0 ? 1.0 : 0.75, b == 0 ? 0.9 : 1.1);
                if (bar % 2 == 1)
                    foreach (var h in new[] { 2.5, 3.5, 3.75 })
                        Instruments.Taiko(s.Mix.Voice(s.T(bar, h), 0.1f, 0.2f), 0.6, 1.3);
                if (bar % 4 == 3)
                    for (int k = 0; k < 8; k++)
                        Instruments.Snare(s.Mix.Voice(s.T(bar, 2 + k * 0.25), 0.3f, 0.3f), 0.15 + 0.04 * k);
                else
                    Instruments.Snare(s.Mix.Voice(s.T(bar, 1), 0.3f, 0.3f), 0.2);

                int root = s.Root(bar, -1);
                for (int k = 0; k < 16; k++)
                    Instruments.StringStaccato(s.Mix.Voice(s.T(bar, k * 0.25), -0.3f, 0.2f), s.Beat * 0.18, root + ostinato[k], k % 4 == 0 ? 0.25 : 0.18);

                foreach (var n in s.Chord(s.Progression[bar], 0))
                    Instruments.Choir(s.Mix.Voice(s.T(bar, 0), 0.1f, 0.6f), s.BarSeconds, n, 0.16);
                for (int k = 0; k < 2; k++)
                    Instruments.Bass(s.Mix.Voice(s.T(bar, k * 2), 0f, 0.05f), s.Beat * 1.8, s.Root(bar, -2), 0.42);
                if (bar % 4 == 3)
                    Instruments.CymbalSwell(s.Mix.Voice(s.T(bar, 0), 0f, 0.5f), s.BarSeconds, 0.7);
            }
            Melody(s, 0, s.Bars, 0, (t, d, n) => Instruments.Brass(s.Mix.Voice(t, 0.2f, 0.45f), d * 0.92, n, 0.26), 0.05);
            return s.Render(0.75f, 0.85, 0.35);
        }

        /// <summary>Victory fanfare in C major.</summary>
        static AudioBuffer Victory(int sr)
        {
            var s = new Song(sr, 100, 60, Major, new[] { 0, 3, 4, 0, 5, 3, 4, 0 }, 89);
            PadChords(s, 0.26, 1.0, 0, 0.3);
            for (int bar = 0; bar < s.Bars; bar++)
            {
                Instruments.Timpani(s.Mix.Voice(s.T(bar, 0), 0f, 0.3f), s.Note(0, -2), 0.55);
                Instruments.Timpani(s.Mix.Voice(s.T(bar, 2), 0f, 0.3f), s.Note(4, -3), 0.45);
                Instruments.Bass(s.Mix.Voice(s.T(bar, 0), 0f, 0.1f), s.BarSeconds * 0.9, s.Root(bar, -2), 0.4);
                foreach (var n in s.Chord(s.Progression[bar], -1))
                    Instruments.Brass(s.Mix.Voice(s.T(bar, 0), -0.25f, 0.4f), s.Beat * 1.5, n, 0.18);
            }
            Melody(s, 0, s.Bars, 0, (t, d, n) => Instruments.Brass(s.Mix.Voice(t, 0.2f, 0.4f), d * 0.9, n, 0.30), 0.0);
            Melody(s, 4, s.Bars, 2, (t, d, n) => Instruments.Bell(s.Mix.Voice(t, 0.4f, 0.5f), n, 0.12, 2.0), 0.4);
            return s.Render(0.8f, 0.85, 0.3);
        }

        /// <summary>Defeat lament in C minor.</summary>
        static AudioBuffer Defeat(int sr)
        {
            var s = new Song(sr, 56, 60, Minor, new[] { 0, 3, 5, 4, 0, 5, 3, 4 }, 97);
            PadChords(s, 0.26, 0.45, -1, 1.5);
            for (int bar = 0; bar < s.Bars; bar++)
            {
                Instruments.Bass(s.Mix.Voice(s.T(bar, 0), 0f, 0.2f), s.BarSeconds * 0.95, s.Root(bar, -2), 0.35);
                foreach (var n in s.Chord(s.Progression[bar], -2))
                    Instruments.Brass(s.Mix.Voice(s.T(bar, 0), 0.1f, 0.5f), s.BarSeconds * 0.9, n, 0.10);
                if (bar % 2 == 0)
                    Instruments.Timpani(s.Mix.Voice(s.T(bar, 0), 0f, 0.5f), s.Note(0, -2), 0.25);
            }
            Melody(s, 0, s.Bars, 0, (t, d, n) => Instruments.Bell(s.Mix.Voice(t, 0.3f, 0.7f), n, 0.10, 3.5), 0.35);
            return s.Render(0.95f, 0.92, 0.5);
        }
    }
}

using System;

namespace GrandStrategy.Game.Audio.Synthesis
{
    /// <summary>
    /// Synthesized instruments. Each call renders one note into a <see cref="Voice"/>.
    /// Times are in seconds, pitches in MIDI note numbers, velocity 0..1.
    /// </summary>
    public static class Instruments
    {
        /// <summary>Warm sustained string/synth pad: detuned saws through a soft low-pass.</summary>
        public static void StringPad(Voice v, double hold, double midi, double velocity, double brightness = 1.0, double attack = 0.6)
        {
            const double release = 1.2;
            int sr = v.SampleRate;
            double f = Dsp.MidiToHz(midi);
            double[] detune = { -0.13, -0.04, 0.05, 0.12 };
            var phases = new double[detune.Length];
            var steps = new double[detune.Length];
            var rng = new Random((int)(midi * 131));
            for (int k = 0; k < phases.Length; k++)
            {
                phases[k] = rng.NextDouble();
                steps[k] = f * Math.Pow(2, detune[k] / 12) / sr;
            }
            var filter = new Svf();
            double cutoff = Math.Min(3200, f * 3.5) * brightness + 250;
            int n = v.Available(hold + release);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / sr;
                double env = Dsp.Adsr(t, hold, attack, 0.5, 0.85, release);
                double s = 0;
                for (int k = 0; k < steps.Length; k++)
                {
                    s += Dsp.Saw(phases[k], steps[k]);
                    phases[k] += steps[k];
                    if (phases[k] >= 1) phases[k] -= 1;
                }
                double lfo = 1 + 0.12 * Math.Sin(Dsp.TwoPi * 0.23 * t);
                s = filter.Low(s * 0.25, cutoff * lfo, 0.6, sr);
                v.Write(i, s * env * velocity);
            }
        }

        /// <summary>Airy "aah" choir pad: saws through two vowel formants.</summary>
        public static void Choir(Voice v, double hold, double midi, double velocity)
        {
            const double release = 1.4;
            int sr = v.SampleRate;
            double f = Dsp.MidiToHz(midi);
            double p1 = 0, p2 = 0.37;
            var f1 = new Svf();
            var f2 = new Svf();
            var lp = new Svf();
            int n = v.Available(hold + release);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / sr;
                double env = Dsp.Adsr(t, hold, 0.9, 0.4, 0.9, release);
                double vib = 1 + 0.004 * Math.Sin(Dsp.TwoPi * 5.1 * t) * Math.Min(1, t / 0.8);
                double fa = f * vib * 1.003, fb = f * vib * 0.997;
                double s = Dsp.Saw(p1, fa / sr) + Dsp.Saw(p2, fb / sr);
                p1 += fa / sr; if (p1 >= 1) p1 -= 1;
                p2 += fb / sr; if (p2 >= 1) p2 -= 1;
                double vowel = f1.Band(s, 700, 4, sr) + 0.7 * f2.Band(s, 1150, 5, sr);
                vowel = lp.Low(vowel, 2600, 0.7, sr);
                v.Write(i, vowel * env * velocity * 0.6);
            }
        }

        /// <summary>Brass section: saws with an opening filter envelope and delayed vibrato.</summary>
        public static void Brass(Voice v, double hold, double midi, double velocity)
        {
            const double release = 0.25;
            int sr = v.SampleRate;
            double f = Dsp.MidiToHz(midi);
            double[] ratio = { Math.Pow(2, -0.06 / 12), 1.0, Math.Pow(2, 0.07 / 12) };
            var phases = new double[] { 0.1, 0.5, 0.8 };
            var filter = new Svf();
            int n = v.Available(hold + release);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / sr;
                double env = Dsp.Adsr(t, hold, 0.045, 0.25, 0.75, release);
                double fenv = Dsp.Adsr(t, hold, 0.07, 0.35, 0.55, release);
                double vib = 1 + 0.0045 * Math.Sin(Dsp.TwoPi * 5.4 * t) * Math.Max(0, Math.Min(1, (t - 0.25) / 0.4));
                double s = 0;
                for (int k = 0; k < ratio.Length; k++)
                {
                    double dt = f * vib * ratio[k] / sr;
                    s += Dsp.Saw(phases[k], dt);
                    phases[k] += dt;
                    if (phases[k] >= 1) phases[k] -= 1;
                }
                double cutoff = f * 1.2 + (Math.Min(5200, f * 7) - f * 1.2) * fenv * (0.6 + 0.4 * velocity);
                s = filter.Low(s / 3, cutoff, 0.9, sr);
                v.Write(i, s * env * velocity);
            }
        }

        /// <summary>Short bowed string note for ostinatos.</summary>
        public static void StringStaccato(Voice v, double hold, double midi, double velocity)
        {
            const double release = 0.08;
            int sr = v.SampleRate;
            double f = Dsp.MidiToHz(midi);
            double p1 = 0.2, p2 = 0.6;
            var filter = new Svf();
            int n = v.Available(hold + release);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / sr;
                double env = Dsp.Adsr(t, hold, 0.006, 0.12, 0.4, release);
                double fa = f * 1.002, fb = f * 0.998;
                double s = Dsp.Saw(p1, fa / sr) + Dsp.Saw(p2, fb / sr);
                p1 += fa / sr; if (p1 >= 1) p1 -= 1;
                p2 += fb / sr; if (p2 >= 1) p2 -= 1;
                s = filter.Low(s * 0.5, Math.Min(3800, f * 6) * (0.5 + 0.5 * env), 0.7, sr);
                v.Write(i, s * env * velocity);
            }
        }

        /// <summary>Harp-like pluck built from decaying harmonics.</summary>
        public static void Pluck(Voice v, double midi, double velocity, double decay = 1.0)
        {
            int sr = v.SampleRate;
            double f = Dsp.MidiToHz(midi);
            const int maxHarmonics = 8;
            // Each harmonic is a rotating phasor (cheap sine) with its own exponential decay.
            var cos = new double[maxHarmonics];
            var sin = new double[maxHarmonics];
            var x = new double[maxHarmonics];
            var y = new double[maxHarmonics];
            var amp = new double[maxHarmonics];
            var fall = new double[maxHarmonics];
            int count = 0;
            for (int k = 1; k <= maxHarmonics; k++)
            {
                double fk = f * k * (1 + 0.0004 * k * k);
                if (fk > sr * 0.45) break;
                double w = Dsp.TwoPi * fk / sr;
                cos[count] = Math.Cos(w);
                sin[count] = Math.Sin(w);
                x[count] = 1;
                y[count] = 0;
                amp[count] = 1 / Math.Pow(k, 1.4);
                fall[count] = Math.Exp(-(1.1 + 0.9 * k) / decay / sr);
                count++;
            }
            var noise = new Noise((uint)(midi * 7919));
            double click = 0.3, clickFall = Math.Exp(-350.0 / sr);
            int n = v.Available(2.8 * decay);
            for (int i = 0; i < n; i++)
            {
                double s = 0;
                for (int k = 0; k < count; k++)
                {
                    double nx = x[k] * cos[k] - y[k] * sin[k];
                    y[k] = x[k] * sin[k] + y[k] * cos[k];
                    x[k] = nx;
                    s += y[k] * amp[k];
                    amp[k] *= fall[k];
                }
                s += noise.Next() * click;
                click *= clickFall;
                double attack = Math.Min(1, i / (0.003 * sr));
                v.Write(i, s * attack * velocity * 0.55);
            }
        }

        /// <summary>FM bell / celesta.</summary>
        public static void Bell(Voice v, double midi, double velocity, double length = 3.0)
        {
            int sr = v.SampleRate;
            double f = Dsp.MidiToHz(midi);
            int n = v.Available(length);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / sr;
                double index = 2.2 * Math.Exp(-t * 3.5);
                double mod = Math.Sin(Dsp.TwoPi * f * 3.5 * t) * index;
                double body = Math.Sin(Dsp.TwoPi * f * t + mod) * Math.Exp(-t * 1.3);
                double shimmer = Math.Sin(Dsp.TwoPi * f * 2.76 * t) * Math.Exp(-t * 4.0) * 0.25;
                double attack = Math.Min(1, t / 0.002);
                v.Write(i, (body + shimmer) * attack * velocity * 0.6);
            }
        }

        /// <summary>Round sub bass.</summary>
        public static void Bass(Voice v, double hold, double midi, double velocity)
        {
            const double release = 0.35;
            int sr = v.SampleRate;
            double f = Dsp.MidiToHz(midi);
            int n = v.Available(hold + release);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / sr;
                double env = Dsp.Adsr(t, hold, 0.02, 0.3, 0.8, release);
                double s = Math.Sin(Dsp.TwoPi * f * t) + 0.3 * Math.Sin(Dsp.TwoPi * 2 * f * t) + 0.08 * Math.Sin(Dsp.TwoPi * 3 * f * t);
                v.Write(i, s * env * velocity * 0.6);
            }
        }

        /// <summary>Orchestral timpani hit.</summary>
        public static void Timpani(Voice v, double midi, double velocity)
        {
            int sr = v.SampleRate;
            double f0 = Dsp.MidiToHz(midi);
            var noise = new Noise((uint)(midi * 104729 + velocity * 1000));
            var lp = new Svf();
            double phase = 0;
            int n = v.Available(2.2);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / sr;
                double f = f0 * (1 + 0.12 * Math.Exp(-t * 25));
                phase += f / sr;
                double tone = Math.Sin(Dsp.TwoPi * phase) + 0.35 * Math.Sin(Dsp.TwoPi * phase * 1.5) * Math.Exp(-t * 4);
                double thump = lp.Low(noise.Next(), 350, 0.7, sr) * Math.Exp(-t * 35);
                double env = Math.Exp(-t * 2.4) * Math.Min(1, t / 0.002);
                v.Write(i, (tone * env + thump * 1.5) * velocity * 0.7);
            }
        }

        /// <summary>Big taiko drum.</summary>
        public static void Taiko(Voice v, double velocity, double pitch = 1.0)
        {
            int sr = v.SampleRate;
            var noise = new Noise((uint)(velocity * 99991 + pitch * 7));
            var lp = new Svf();
            double phase = 0;
            int n = v.Available(1.4);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / sr;
                double f = 62 * pitch * (1 + 0.9 * Math.Exp(-t * 28));
                phase += f / sr;
                double body = Math.Sin(Dsp.TwoPi * phase) * Math.Exp(-t * 5.5);
                double skin = lp.Low(noise.Next(), 900, 0.8, sr) * Math.Exp(-t * 22) * 0.9;
                double attack = Math.Min(1, t / 0.001);
                v.Write(i, (body + skin) * attack * velocity);
            }
        }

        /// <summary>Snare / field drum.</summary>
        public static void Snare(Voice v, double velocity)
        {
            int sr = v.SampleRate;
            var noise = new Noise((uint)(velocity * 65521 + 3));
            var bp = new Svf();
            int n = v.Available(0.35);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / sr;
                double rattle = bp.Band(noise.Next(), 2400, 0.7, sr) * Math.Exp(-t * 17);
                double tone = Math.Sin(Dsp.TwoPi * 190 * t) * Math.Exp(-t * 30) * 0.5;
                v.Write(i, (rattle + tone) * velocity * 0.8);
            }
        }

        /// <summary>Cymbal swell that builds into a downbeat.</summary>
        public static void CymbalSwell(Voice v, double duration, double velocity)
        {
            int sr = v.SampleRate;
            var noise = new Noise(12345);
            var hp = new Svf();
            int n = v.Available(duration + 0.6);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / sr;
                double env = t < duration ? Math.Pow(t / duration, 2.2) : Math.Exp(-(t - duration) * 7);
                double s = hp.High(noise.Next(), 5500, 0.7, sr);
                v.Write(i, s * env * velocity * 0.2);
            }
        }

        /// <summary>Short UI blip: sine with fast decay and optional pitch glide.</summary>
        public static void Blip(Voice v, double startHz, double endHz, double length, double velocity, double decay = 30)
        {
            int sr = v.SampleRate;
            double phase = 0;
            int n = v.Available(length);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / sr;
                double k = Math.Min(1, t / length);
                double f = startHz + (endHz - startHz) * k;
                phase += f / sr;
                double env = Math.Exp(-t * decay) * Math.Min(1, t / 0.002);
                v.Write(i, Math.Sin(Dsp.TwoPi * phase) * env * velocity);
            }
        }

        /// <summary>Very short noise tick (mechanical click transient).</summary>
        public static void Tick(Voice v, double velocity, double cutoff = 4000)
        {
            int sr = v.SampleRate;
            var noise = new Noise(777);
            var bp = new Svf();
            int n = v.Available(0.03);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / sr;
                v.Write(i, bp.Band(noise.Next(), cutoff, 1.2, sr) * Math.Exp(-t * 300) * velocity);
            }
        }

        /// <summary>Filtered saw sweep (used for pause / resume).</summary>
        public static void Sweep(Voice v, double startHz, double endHz, double length, double velocity)
        {
            int sr = v.SampleRate;
            double phase = 0;
            var lp = new Svf();
            int n = v.Available(length + 0.05);
            for (int i = 0; i < n; i++)
            {
                double t = (double)i / sr;
                double k = Math.Min(1, t / length);
                double f = startHz * Math.Pow(endHz / startHz, k);
                double dt = f / sr;
                double s = Dsp.Saw(phase, dt);
                phase += dt;
                if (phase >= 1) phase -= 1;
                double env = Math.Sin(Math.PI * Math.Min(1, t / length)) * Math.Exp(-t * 3);
                v.Write(i, lp.Low(s, f * 3, 0.8, sr) * env * velocity * 0.5);
            }
        }
    }
}

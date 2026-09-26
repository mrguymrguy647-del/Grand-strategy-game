using System;

namespace GrandStrategy.Game.Audio.Synthesis
{
    // Plain C# signal processing used to synthesize placeholder music and sound effects.
    // Nothing here touches UnityEngine, so it can run on a worker thread and in unit tests.

    /// <summary>Interleaved PCM audio.</summary>
    public sealed class AudioBuffer
    {
        public AudioBuffer(int sampleRate, int channels, float[] samples)
        {
            SampleRate = sampleRate;
            Channels = channels;
            Samples = samples;
        }

        public int SampleRate { get; }
        public int Channels { get; }
        public float[] Samples { get; }
        public int Frames => Samples.Length / Channels;
        public double Seconds => (double)Frames / SampleRate;
    }

    /// <summary>A stereo mix bus with a reverb send.</summary>
    public sealed class Mix
    {
        public readonly int SampleRate;
        public readonly int Frames;
        public readonly float[] L;
        public readonly float[] R;
        public readonly float[] SendL;
        public readonly float[] SendR;

        public Mix(int sampleRate, double seconds)
        {
            SampleRate = sampleRate;
            Frames = Math.Max(1, (int)Math.Ceiling(seconds * sampleRate));
            L = new float[Frames];
            R = new float[Frames];
            SendL = new float[Frames];
            SendR = new float[Frames];
        }

        public double Seconds => (double)Frames / SampleRate;

        /// <summary>Per-note writer with fixed pan and reverb send.</summary>
        public Voice Voice(double start, float pan, float send) => new Voice(this, start, pan, send);

        /// <summary>Runs the reverb over the send bus, masters, and returns interleaved stereo.</summary>
        public AudioBuffer Render(float reverbWet, double roomSize, double damping, double loopSeconds = 0, float peak = 0.9f)
        {
            var reverb = new Reverb(SampleRate, roomSize, damping);
            for (int i = 0; i < Frames; i++)
            {
                reverb.Process(SendL[i], SendR[i], out float wl, out float wr);
                L[i] += wl * reverbWet;
                R[i] += wr * reverbWet;
            }

            int frames = Frames;
            if (loopSeconds > 0)
            {
                // Fold everything after the loop point (reverb and release tails) back onto the
                // start, so the clip loops without a gap or click.
                frames = Math.Min(Frames, (int)Math.Round(loopSeconds * SampleRate));
                for (int i = frames; i < Frames; i++)
                {
                    int j = (i - frames) % frames;
                    L[j] += L[i];
                    R[j] += R[i];
                }
            }

            float max = 1e-6f;
            for (int i = 0; i < frames; i++)
                max = Math.Max(max, Math.Max(Math.Abs(L[i]), Math.Abs(R[i])));

            // Normalise, then a gentle soft clip so loud moments never distort harshly.
            double gain = 1.0 / max;
            const double drive = 1.15;
            double norm = peak / Math.Tanh(drive);
            var output = new float[frames * 2];
            for (int i = 0; i < frames; i++)
            {
                output[2 * i] = (float)(Math.Tanh(L[i] * gain * drive) * norm);
                output[2 * i + 1] = (float)(Math.Tanh(R[i] * gain * drive) * norm);
            }
            return new AudioBuffer(SampleRate, 2, output);
        }
    }

    /// <summary>Writes one note into a mix at a fixed pan position.</summary>
    public readonly struct Voice
    {
        readonly Mix _mix;
        readonly int _start;
        readonly float _gl;
        readonly float _gr;
        readonly float _send;

        public Voice(Mix mix, double start, float pan, float send)
        {
            _mix = mix;
            _start = (int)Math.Round(start * mix.SampleRate);
            double angle = (Math.Max(-1, Math.Min(1, pan)) + 1) * Math.PI / 4;
            _gl = (float)Math.Cos(angle);
            _gr = (float)Math.Sin(angle);
            _send = send;
        }

        public int SampleRate => _mix.SampleRate;

        /// <summary>Number of frames available from the note start to the end of the mix.</summary>
        public int Available(double seconds) =>
            Math.Max(0, Math.Min((int)Math.Ceiling(seconds * _mix.SampleRate), _mix.Frames - _start));

        public void Write(int frame, double sample)
        {
            int i = _start + frame;
            if ((uint)i >= (uint)_mix.Frames)
                return;
            float s = (float)sample;
            _mix.L[i] += s * _gl;
            _mix.R[i] += s * _gr;
            _mix.SendL[i] += s * _gl * _send;
            _mix.SendR[i] += s * _gr * _send;
        }
    }

    public static class Dsp
    {
        public const double TwoPi = Math.PI * 2;

        public static double MidiToHz(double midi) => 440.0 * Math.Pow(2, (midi - 69) / 12.0);

        /// <summary>Band-limited step correction for saw/square oscillators.</summary>
        public static double PolyBlep(double t, double dt)
        {
            if (t < dt)
            {
                t /= dt;
                return t + t - t * t - 1;
            }
            if (t > 1 - dt)
            {
                t = (t - 1) / dt;
                return t * t + t + t + 1;
            }
            return 0;
        }

        /// <summary>Anti-aliased sawtooth for a phase in [0, 1).</summary>
        public static double Saw(double phase, double dt) => 2 * phase - 1 - PolyBlep(phase, dt);

        /// <summary>ADSR envelope level at time t for a note held for <paramref name="hold"/> seconds.</summary>
        public static double Adsr(double t, double hold, double a, double d, double s, double r)
        {
            if (t < 0)
                return 0;
            if (t <= hold)
                return AdsrHeld(t, a, d, s);
            double rt = (t - hold) / Math.Max(1e-4, r);
            if (rt >= 1)
                return 0;
            double k = 1 - rt;
            return AdsrHeld(hold, a, d, s) * k * k;
        }

        static double AdsrHeld(double t, double a, double d, double s)
        {
            if (t < a)
                return t / Math.Max(1e-4, a);
            if (t < a + d)
                return 1 - (1 - s) * (t - a) / Math.Max(1e-4, d);
            return s;
        }
    }

    /// <summary>Fast deterministic white noise.</summary>
    public struct Noise
    {
        uint _state;

        public Noise(uint seed) => _state = seed == 0 ? 0x9E3779B9u : seed;

        public double Next()
        {
            _state ^= _state << 13;
            _state ^= _state >> 17;
            _state ^= _state << 5;
            return _state / (double)uint.MaxValue * 2 - 1;
        }
    }

    /// <summary>Topology-preserving state variable filter (Simper). Stable when modulated.</summary>
    public struct Svf
    {
        double _ic1;
        double _ic2;

        public double Low(double input, double cutoff, double q, int sampleRate)
        {
            Process(input, cutoff, q, sampleRate, out _, out double low);
            return low;
        }

        public double Band(double input, double cutoff, double q, int sampleRate)
        {
            Process(input, cutoff, q, sampleRate, out double band, out _);
            return band;
        }

        public double High(double input, double cutoff, double q, int sampleRate)
        {
            Process(input, cutoff, q, sampleRate, out double band, out double low);
            return input - band / Math.Max(0.05, q) - low;
        }

        void Process(double v0, double cutoff, double q, int sampleRate, out double band, out double low)
        {
            cutoff = Math.Max(20, Math.Min(cutoff, sampleRate * 0.45));
            double g = Math.Tan(Math.PI * cutoff / sampleRate);
            double k = 1 / Math.Max(0.05, q);
            double a1 = 1 / (1 + g * (g + k));
            double a2 = g * a1;
            double a3 = g * a2;
            double v3 = v0 - _ic2;
            double v1 = a1 * _ic1 + a2 * v3;
            double v2 = _ic2 + a2 * _ic1 + a3 * v3;
            _ic1 = 2 * v1 - _ic1;
            _ic2 = 2 * v2 - _ic2;
            band = v1;
            low = v2;
        }
    }

    /// <summary>Freeverb-style stereo reverb.</summary>
    public sealed class Reverb
    {
        static readonly int[] CombTuning = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
        static readonly int[] AllpassTuning = { 556, 441, 341, 225 };
        const int StereoSpread = 23;

        readonly Comb[] _combL;
        readonly Comb[] _combR;
        readonly Allpass[] _apL;
        readonly Allpass[] _apR;

        public Reverb(int sampleRate, double roomSize, double damping)
        {
            double scale = sampleRate / 44100.0;
            double feedback = 0.28 + 0.7 * Math.Max(0, Math.Min(1, roomSize));
            _combL = new Comb[CombTuning.Length];
            _combR = new Comb[CombTuning.Length];
            for (int i = 0; i < CombTuning.Length; i++)
            {
                _combL[i] = new Comb((int)(CombTuning[i] * scale), feedback, damping);
                _combR[i] = new Comb((int)((CombTuning[i] + StereoSpread) * scale), feedback, damping);
            }
            _apL = new Allpass[AllpassTuning.Length];
            _apR = new Allpass[AllpassTuning.Length];
            for (int i = 0; i < AllpassTuning.Length; i++)
            {
                _apL[i] = new Allpass((int)(AllpassTuning[i] * scale));
                _apR[i] = new Allpass((int)((AllpassTuning[i] + StereoSpread) * scale));
            }
        }

        public void Process(float inL, float inR, out float outL, out float outR)
        {
            double input = (inL + inR) * 0.015;
            double l = 0, r = 0;
            for (int i = 0; i < _combL.Length; i++)
            {
                l += _combL[i].Process(input);
                r += _combR[i].Process(input);
            }
            for (int i = 0; i < _apL.Length; i++)
            {
                l = _apL[i].Process(l);
                r = _apR[i].Process(r);
            }
            outL = (float)l;
            outR = (float)r;
        }

        sealed class Comb
        {
            readonly double[] _buffer;
            readonly double _feedback;
            readonly double _damp;
            double _store;
            int _index;

            public Comb(int size, double feedback, double damp)
            {
                _buffer = new double[Math.Max(1, size)];
                _feedback = feedback;
                _damp = damp;
            }

            public double Process(double input)
            {
                double output = _buffer[_index];
                _store = output * (1 - _damp) + _store * _damp;
                _buffer[_index] = input + _store * _feedback;
                if (++_index >= _buffer.Length) _index = 0;
                return output;
            }
        }

        sealed class Allpass
        {
            readonly double[] _buffer;
            int _index;

            public Allpass(int size) => _buffer = new double[Math.Max(1, size)];

            public double Process(double input)
            {
                double buffered = _buffer[_index];
                _buffer[_index] = input + buffered * 0.5;
                if (++_index >= _buffer.Length) _index = 0;
                return buffered - input;
            }
        }
    }
}

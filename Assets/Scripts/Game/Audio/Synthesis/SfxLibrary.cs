using System;

namespace GrandStrategy.Game.Audio.Synthesis
{
    /// <summary>
    /// Synthesizes every sound effect. Real files placed in Resources/Audio/Sfx replace these.
    /// </summary>
    public static class SfxLibrary
    {
        public const int DefaultSampleRate = 44100;

        public static AudioBuffer Render(Sfx sfx, int sampleRate = DefaultSampleRate)
        {
            switch (sfx)
            {
                case Sfx.UiClick:
                {
                    var m = new Mix(sampleRate, 0.12);
                    Instruments.Tick(m.Voice(0, 0, 0), 0.5, 3500);
                    Instruments.Blip(m.Voice(0, 0, 0), 1500, 1350, 0.08, 0.5, 70);
                    Instruments.Blip(m.Voice(0, 0, 0), 750, 700, 0.08, 0.3, 55);
                    return m.Render(0f, 0.3, 0.5, 0, 0.55f);
                }
                case Sfx.UiHover:
                {
                    var m = new Mix(sampleRate, 0.06);
                    Instruments.Blip(m.Voice(0, 0, 0), 2400, 2500, 0.05, 0.3, 110);
                    return m.Render(0f, 0.3, 0.5, 0, 0.2f);
                }
                case Sfx.ProvinceSelect:
                {
                    var m = new Mix(sampleRate, 0.6);
                    Instruments.Tick(m.Voice(0, 0, 0.2f), 0.6, 1800);
                    Instruments.Blip(m.Voice(0, 0, 0.3f), 620, 470, 0.09, 0.7, 20);
                    Instruments.Pluck(m.Voice(0.01, 0, 0.3f), 79, 0.35, 0.25);
                    return m.Render(0.4f, 0.4, 0.5, 0, 0.6f);
                }
                case Sfx.SpeedUp:
                case Sfx.SpeedDown:
                {
                    bool up = sfx == Sfx.SpeedUp;
                    var m = new Mix(sampleRate, 0.3);
                    Instruments.Blip(m.Voice(0, 0, 0.2f), up ? 660 : 990, up ? 680 : 970, 0.07, 0.5, 35);
                    Instruments.Blip(m.Voice(0.075, 0, 0.2f), up ? 990 : 660, up ? 1010 : 640, 0.09, 0.5, 30);
                    return m.Render(0.3f, 0.3, 0.5, 0, 0.5f);
                }
                case Sfx.Pause:
                case Sfx.Resume:
                {
                    bool pause = sfx == Sfx.Pause;
                    var m = new Mix(sampleRate, 0.5);
                    Instruments.Sweep(m.Voice(0, 0, 0.2f), pause ? 700 : 240, pause ? 240 : 700, 0.28, 0.9);
                    Instruments.Tick(m.Voice(0, 0, 0), 0.4, 2500);
                    return m.Render(0.3f, 0.3, 0.5, 0, 0.55f);
                }
                case Sfx.NewMonth:
                {
                    var m = new Mix(sampleRate, 2.0);
                    Instruments.Bell(m.Voice(0, 0.2f, 0.6f), 84, 0.5, 1.6);
                    return m.Render(0.6f, 0.6, 0.4, 0, 0.35f);
                }
                case Sfx.NewYear:
                {
                    var m = new Mix(sampleRate, 3.5);
                    int[] notes = { 72, 76, 79, 84 };
                    for (int i = 0; i < notes.Length; i++)
                        Instruments.Bell(m.Voice(i * 0.07, -0.3f + 0.2f * i, 0.6f), notes[i], 0.4, 3.0);
                    Instruments.Bell(m.Voice(0, 0, 0.5f), 60, 0.4, 3.0);
                    return m.Render(0.7f, 0.75, 0.4, 0, 0.6f);
                }
                case Sfx.NationChosen:
                {
                    var m = new Mix(sampleRate, 4.0);
                    int[] arp = { 62, 66, 69, 74 };
                    Instruments.Timpani(m.Voice(0, 0, 0.3f), 38, 0.8);
                    for (int i = 0; i < arp.Length; i++)
                        Instruments.Brass(m.Voice(i * 0.14, -0.2f + 0.13f * i, 0.4f), i == arp.Length - 1 ? 1.4 : 0.13, arp[i], 0.5);
                    foreach (var n in new[] { 50, 57, 62, 66 })
                        Instruments.Brass(m.Voice(0.42, 0.1f, 0.4f), 1.4, n, 0.3);
                    foreach (var n in new[] { 62, 66, 69 })
                        Instruments.StringPad(m.Voice(0.42, 0f, 0.5f), 1.4, n, 0.3, 1.0, 0.2);
                    Instruments.Timpani(m.Voice(0.42, 0, 0.3f), 38, 0.9);
                    return m.Render(0.8f, 0.8, 0.35, 0, 0.8f);
                }
                case Sfx.Notification:
                {
                    var m = new Mix(sampleRate, 1.8);
                    Instruments.Bell(m.Voice(0, -0.2f, 0.5f), 88, 0.4, 1.4);
                    Instruments.Bell(m.Voice(0.13, 0.2f, 0.5f), 83, 0.4, 1.4);
                    return m.Render(0.5f, 0.55, 0.4, 0, 0.45f);
                }
                case Sfx.WarDeclared:
                {
                    var m = new Mix(sampleRate, 3.5);
                    Instruments.Taiko(m.Voice(0, 0, 0.3f), 1.0, 0.95);
                    Instruments.Taiko(m.Voice(0.28, 0, 0.3f), 0.9, 1.05);
                    foreach (var n in new[] { 38, 45, 50, 53 })
                        Instruments.Brass(m.Voice(0.28, 0.1f, 0.4f), 1.1, n, 0.45);
                    for (int i = 0; i < 12; i++)
                        Instruments.Snare(m.Voice(1.2 + i * 0.07, 0.3f, 0.3f), 0.1 + 0.04 * i);
                    Instruments.Taiko(m.Voice(2.05, 0, 0.3f), 1.0, 0.9);
                    Instruments.Timpani(m.Voice(2.05, 0, 0.3f), 38, 0.8);
                    return m.Render(0.7f, 0.8, 0.4, 0, 0.9f);
                }
                case Sfx.CapitalBattleStart:
                {
                    var m = new Mix(sampleRate, 5.0);
                    Instruments.Brass(m.Voice(0, -0.2f, 0.5f), 0.45, 57, 0.5);
                    Instruments.Brass(m.Voice(0.5, -0.2f, 0.5f), 0.45, 64, 0.5);
                    Instruments.Brass(m.Voice(1.0, -0.2f, 0.5f), 1.6, 69, 0.55);
                    Instruments.Brass(m.Voice(1.0, 0.2f, 0.5f), 1.6, 57, 0.4);
                    for (int i = 0; i < 20; i++)
                        Instruments.Snare(m.Voice(1.0 + i * 0.06, 0.3f, 0.3f), 0.08 + 0.03 * i);
                    Instruments.CymbalSwell(m.Voice(1.0, 0, 0.5f), 1.25, 0.9);
                    Instruments.Taiko(m.Voice(2.25, 0, 0.3f), 1.0, 0.85);
                    Instruments.Timpani(m.Voice(2.25, 0, 0.3f), 33, 1.0);
                    foreach (var n in new[] { 45, 52, 57, 60 })
                        Instruments.Choir(m.Voice(2.25, 0f, 0.6f), 1.5, n, 0.35);
                    return m.Render(0.8f, 0.85, 0.35, 0, 0.9f);
                }
                case Sfx.Victory:
                {
                    var m = new Mix(sampleRate, 4.5);
                    int[] fanfare = { 60, 64, 67, 72 };
                    for (int i = 0; i < fanfare.Length; i++)
                        Instruments.Brass(m.Voice(i * 0.16, 0.1f, 0.4f), i == fanfare.Length - 1 ? 1.8 : 0.14, fanfare[i], 0.5);
                    foreach (var n in new[] { 48, 55, 64, 67 })
                        Instruments.Brass(m.Voice(0.48, -0.1f, 0.4f), 1.8, n, 0.3);
                    Instruments.Timpani(m.Voice(0, 0, 0.3f), 36, 0.7);
                    Instruments.Timpani(m.Voice(0.48, 0, 0.3f), 36, 0.9);
                    foreach (var n in new[] { 84, 88, 91 })
                        Instruments.Bell(m.Voice(0.5, 0.3f, 0.5f), n, 0.25, 2.5);
                    return m.Render(0.8f, 0.8, 0.35, 0, 0.85f);
                }
                case Sfx.Defeat:
                {
                    var m = new Mix(sampleRate, 5.0);
                    int[] fall = { 67, 63, 60 };
                    for (int i = 0; i < fall.Length; i++)
                        Instruments.Brass(m.Voice(i * 0.45, 0f, 0.5f), i == fall.Length - 1 ? 1.8 : 0.4, fall[i] - 12, 0.45);
                    foreach (var n in new[] { 48, 51, 55 })
                        Instruments.StringPad(m.Voice(0.9, 0f, 0.6f), 1.8, n, 0.35, 0.5, 0.3);
                    Instruments.Timpani(m.Voice(0.9, 0, 0.4f), 36, 0.6);
                    return m.Render(0.9f, 0.9, 0.5, 0, 0.8f);
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(sfx), sfx, null);
            }
        }
    }
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using GrandStrategy.Game.Audio.Synthesis;
using UnityEngine;

namespace GrandStrategy.Game.Audio
{
    /// <summary>
    /// Plays mood-based music with cross-fades, and sound effects.
    ///
    /// Every sound has a synthesized placeholder generated on a worker thread at startup.
    /// Real audio replaces it automatically when present:
    ///   Resources/Audio/Music/&lt;Mood&gt;/*   (any number of tracks, played as a shuffled playlist)
    ///   Resources/Audio/Sfx/&lt;sfx_name&gt;     (e.g. ui_click.wav, see AudioIds.FileName)
    /// </summary>
    public sealed class AudioManager : MonoBehaviour
    {
        const string MusicVolumeKey = "gs.audio.music";
        const string SfxVolumeKey = "gs.audio.sfx";
        const string MutedKey = "gs.audio.muted";
        const float CrossfadeSeconds = 2.5f;
        const float MusicHeadroom = 0.6f;
        const float MinRepeatSeconds = 0.04f;

        AudioSource _musicA;
        AudioSource _musicB;
        AudioSource _current;
        AudioSource _previous;
        AudioSource _sfxSource;
        float _fade = 1f;

        float _musicVolume = 0.7f;
        float _sfxVolume = 0.8f;
        bool _muted;

        MusicMood _mood = MusicMood.None;
        MusicMood _playingMood = MusicMood.None;
        int _playlistIndex;

        readonly Dictionary<Sfx, AudioClip> _sfxClips = new Dictionary<Sfx, AudioClip>();
        readonly Dictionary<Sfx, Task<AudioBuffer>> _sfxPending = new Dictionary<Sfx, Task<AudioBuffer>>();
        readonly Dictionary<Sfx, float> _lastPlayed = new Dictionary<Sfx, float>();
        readonly Dictionary<MusicMood, AudioClip> _generatedMusic = new Dictionary<MusicMood, AudioClip>();
        readonly Dictionary<MusicMood, Task<AudioBuffer>> _musicPending = new Dictionary<MusicMood, Task<AudioBuffer>>();
        readonly Dictionary<MusicMood, AudioClip[]> _customMusic = new Dictionary<MusicMood, AudioClip[]>();

        public MusicMood Mood => _mood;

        public float MusicVolume
        {
            get => _musicVolume;
            set
            {
                _musicVolume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(MusicVolumeKey, _musicVolume);
            }
        }

        public float SfxVolume
        {
            get => _sfxVolume;
            set
            {
                _sfxVolume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat(SfxVolumeKey, _sfxVolume);
            }
        }

        public bool Muted
        {
            get => _muted;
            set
            {
                _muted = value;
                PlayerPrefs.SetInt(MutedKey, value ? 1 : 0);
            }
        }

        void Awake()
        {
            _musicVolume = PlayerPrefs.GetFloat(MusicVolumeKey, _musicVolume);
            _sfxVolume = PlayerPrefs.GetFloat(SfxVolumeKey, _sfxVolume);
            _muted = PlayerPrefs.GetInt(MutedKey, 0) == 1;

            _musicA = CreateSource("Music A");
            _musicB = CreateSource("Music B");
            _sfxSource = CreateSource("Sfx");
            _sfxSource.volume = 1f; // PlayOneShot scales by the source volume

            foreach (Sfx sfx in Enum.GetValues(typeof(Sfx)))
            {
                var custom = Resources.Load<AudioClip>("Audio/Sfx/" + AudioIds.FileName(sfx));
                if (custom != null)
                    _sfxClips[sfx] = custom;
                else
                {
                    var id = sfx;
                    _sfxPending[sfx] = Task.Run(() => SfxLibrary.Render(id));
                }
            }

            foreach (MusicMood mood in Enum.GetValues(typeof(MusicMood)))
            {
                if (mood == MusicMood.None)
                    continue;
                var clips = Resources.LoadAll<AudioClip>("Audio/Music/" + mood);
                if (clips != null && clips.Length > 0)
                    _customMusic[mood] = clips;
            }

            // The first two moods a player hears are generated straight away.
            RequestMusic(MusicMood.Menu);
            RequestMusic(MusicMood.Peace);
        }

        AudioSource CreateSource(string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            source.volume = 0f;
            return source;
        }

        /// <summary>Changes the background music mood (cross-fades once the music is ready).</summary>
        public void PlayMusic(MusicMood mood)
        {
            if (mood == _mood)
                return;
            _mood = mood;
            if (mood != MusicMood.None)
                RequestMusic(mood);
        }

        public void Play(Sfx sfx, float volume = 1f)
        {
            if (!_sfxClips.TryGetValue(sfx, out var clip))
                return; // still being generated
            float now = Time.unscaledTime;
            if (_lastPlayed.TryGetValue(sfx, out var last) && now - last < MinRepeatSeconds)
                return;
            _lastPlayed[sfx] = now;
            if (!_muted)
                _sfxSource.PlayOneShot(clip, volume * _sfxVolume);
        }

        void RequestMusic(MusicMood mood)
        {
            if (_customMusic.ContainsKey(mood) || _generatedMusic.ContainsKey(mood) || _musicPending.ContainsKey(mood))
                return;
            _musicPending[mood] = Task.Run(() => MusicComposer.Render(mood));
        }

        void Update()
        {
            CollectFinishedSfx();
            CollectFinishedMusic();
            UpdateMusic(Time.unscaledDeltaTime);
        }

        void CollectFinishedSfx()
        {
            if (_sfxPending.Count == 0)
                return;
            List<Sfx> done = null;
            foreach (var kv in _sfxPending)
            {
                if (!kv.Value.IsCompleted)
                    continue;
                (done ??= new List<Sfx>()).Add(kv.Key);
                if (kv.Value.IsFaulted)
                    Debug.LogException(kv.Value.Exception);
                else
                    _sfxClips[kv.Key] = ToClip("sfx_" + AudioIds.FileName(kv.Key), kv.Value.Result, false);
            }
            if (done != null)
                foreach (var k in done)
                    _sfxPending.Remove(k);
        }

        void CollectFinishedMusic()
        {
            if (_musicPending.Count == 0)
                return;
            List<MusicMood> done = null;
            foreach (var kv in _musicPending)
            {
                if (!kv.Value.IsCompleted)
                    continue;
                (done ??= new List<MusicMood>()).Add(kv.Key);
                if (kv.Value.IsFaulted)
                    Debug.LogException(kv.Value.Exception);
                else
                    _generatedMusic[kv.Key] = ToClip("music_" + kv.Key, kv.Value.Result, false);
            }
            if (done != null)
                foreach (var k in done)
                    _musicPending.Remove(k);
        }

        static AudioClip ToClip(string name, AudioBuffer buffer, bool stream)
        {
            var clip = AudioClip.Create(name, buffer.Frames, buffer.Channels, buffer.SampleRate, stream);
            clip.SetData(buffer.Samples, 0);
            return clip;
        }

        void UpdateMusic(float dt)
        {
            // Start the new mood as soon as its music exists.
            if (_mood != _playingMood)
            {
                if (_mood == MusicMood.None)
                {
                    StartTrack(null, false);
                    _playingMood = MusicMood.None;
                }
                else if (TryGetTrack(_mood, out var clip, out bool loop))
                {
                    StartTrack(clip, loop);
                    _playingMood = _mood;
                }
            }
            else if (_current != null && _current.clip != null && !_current.loop && !_current.isPlaying && _fade >= 1f)
            {
                // A custom playlist track ended: play the next one.
                if (TryGetTrack(_mood, out var next, out bool loop))
                    StartTrack(next, loop);
            }

            _fade = Mathf.MoveTowards(_fade, 1f, dt / CrossfadeSeconds);
            float target = _muted ? 0f : _musicVolume * MusicHeadroom;
            if (_current != null)
                _current.volume = target * _fade;
            if (_previous != null)
            {
                _previous.volume = target * (1f - _fade);
                if (_fade >= 1f)
                {
                    _previous.Stop();
                    _previous = null;
                }
            }
        }

        bool TryGetTrack(MusicMood mood, out AudioClip clip, out bool loop)
        {
            if (_customMusic.TryGetValue(mood, out var list))
            {
                if (mood != _playingMood)
                    _playlistIndex = UnityEngine.Random.Range(0, list.Length);
                else
                    _playlistIndex = (_playlistIndex + 1) % list.Length;
                clip = list[_playlistIndex];
                loop = list.Length == 1;
                return true;
            }
            loop = true;
            return _generatedMusic.TryGetValue(mood, out clip);
        }

        void StartTrack(AudioClip clip, bool loop)
        {
            if (_previous != null)
                _previous.Stop();
            _previous = _current;
            if (clip == null)
            {
                _current = null;
                _fade = 0f;
                return;
            }
            _current = _current == _musicA ? _musicB : _musicA;
            _current.clip = clip;
            _current.loop = loop;
            _current.volume = 0f;
            _current.Play();
            _fade = 0f;
        }
    }
}

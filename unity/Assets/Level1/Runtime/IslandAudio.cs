using System.Collections.Generic;
using UnityEngine;

namespace EcoBotBuilders.Level1.View
{
    public sealed class IslandAudio : MonoBehaviour
    {
        AudioSource effects, sea;
        readonly Dictionary<string, AudioClip> clips = new Dictionary<string, AudioClip>();
        public bool Muted { get; private set; }
        void Awake()
        {
            effects = gameObject.AddComponent<AudioSource>(); effects.playOnAwake = false; effects.spatialBlend = 0;
            sea = gameObject.AddComponent<AudioSource>(); sea.playOnAwake = false; sea.loop = true; sea.spatialBlend = 0; sea.volume = 0.09f;
            Add("tap", 600, 0.07f); Add("move", 220, 0.13f); Add("turn", 410, 0.14f);
            Add("clean", 880, 0.28f); Add("empty", 330, 0.12f); Add("blocked", 130, 0.25f); Add("win", 660, 0.9f);
            const int samples = 44100 * 4; var data = new float[samples];
            var random = new System.Random(42); float filtered = 0;
            for (int i = 0; i < samples; i++)
            {
                filtered = filtered * 0.97f + ((float)random.NextDouble()*2-1) * 0.03f;
                float t = i / 44100f;
                data[i] = filtered * (0.5f+0.5f*Mathf.Sin(t*Mathf.PI/2)) * Mathf.Min(1, t*4, (4-t)*4);
            }
            var ambient = AudioClip.Create("Tideleaf sea", samples, 1, 44100, false); ambient.SetData(data,0);
            clips.Add("sea",ambient); sea.clip = ambient; sea.Play();
            Muted = PlayerPrefs.GetInt("TideleafMuted",0) == 1; ApplyMute();
        }
        void Add(string name, float frequency, float length)
        {
            int count = (int)(44100*length); var data = new float[count];
            for (int i=0; i<count; i++)
            {
                float t=i/44100f, envelope=Mathf.Sin(Mathf.PI*i/count);
                float f = frequency;
                if (name == "win") f *= t < .3f ? 1 : t < .6f ? 1.25f : 1.5f;
                if (name == "clean") f *= 1 + t;
                data[i] = (Mathf.Sin(2*Mathf.PI*f*t) + .2f*Mathf.Sin(4*Mathf.PI*f*t)) * envelope * .16f;
            }
            var clip = AudioClip.Create("Tideleaf " + name,count,1,44100,false); clip.SetData(data,0); clips.Add(name,clip);
        }
        public void Play(string name) { if (!Muted && clips.ContainsKey(name)) effects.PlayOneShot(clips[name]); }
        public void Toggle() { Muted=!Muted; PlayerPrefs.SetInt("TideleafMuted",Muted?1:0); PlayerPrefs.Save(); ApplyMute(); }
        void ApplyMute() { effects.mute=sea.mute=Muted; }
        public void Pause(bool paused) { if (paused) { effects.Pause(); sea.Pause(); } else { effects.UnPause(); sea.UnPause(); } }
        void OnDestroy() { foreach (var clip in clips.Values) Destroy(clip); }
    }
}

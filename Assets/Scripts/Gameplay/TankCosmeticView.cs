using System.Collections.Generic;
using UnityEngine;

namespace BumperTanks.Gameplay
{
    /// <summary>Applies the player's selected garage flag and generated bump sound.</summary>
    [RequireComponent(typeof(AudioSource))]
    public sealed class TankCosmeticView : MonoBehaviour
    {
        private static readonly Dictionary<string, AudioClip> SoundClips = new();
        private AudioSource audioSource;
        private GameObject flagObject;
        private string bumpSoundId = "impact";

        private void Awake()
        {
            audioSource = GetComponent<AudioSource>();
            audioSource.playOnAwake = false;
            audioSource.loop = false;
            audioSource.spatialBlend = 0f;
        }

        public void Configure(string flagId, string soundId)
        {
            bumpSoundId = soundId;
            ApplyFlag(flagId);
        }

        public void SetBumpSound(string soundId) => bumpSoundId = soundId;

        public void ApplyFlag(string flagId)
        {
            if (flagObject != null)
            {
                if (Application.isPlaying) Destroy(flagObject);
                else DestroyImmediate(flagObject);
            }
            if (string.IsNullOrEmpty(flagId) || flagId == "none") return;

            var clothColor = flagId switch
            {
                "checkered" => new Color(1f, 0.82f, 0.18f),
                "comet" => new Color(0.88f, 0.3f, 1f),
                _ => new Color(0.2f, 0.9f, 0.85f)
            };
            flagObject = new GameObject("Tank Flag");
            flagObject.transform.SetParent(transform, false);
            flagObject.transform.localPosition = new Vector3(0.32f, 0.62f, 0f);
            flagObject.transform.localScale = Vector3.one;

            var pole = new GameObject("Flag Pole");
            pole.transform.SetParent(flagObject.transform, false);
            pole.transform.localPosition = new Vector3(0f, 0.17f, 0.01f);
            pole.transform.localScale = new Vector3(0.035f, 0.5f, 1f);
            var poleRenderer = pole.AddComponent<SpriteRenderer>();
            poleRenderer.sprite = RuntimeSprite.White;
            poleRenderer.color = new Color(0.88f, 0.9f, 0.95f);
            poleRenderer.sortingOrder = 3;

            var cloth = new GameObject("Flag Cloth");
            cloth.transform.SetParent(flagObject.transform, false);
            cloth.transform.localPosition = new Vector3(0.14f, 0.3f, 0f);
            cloth.transform.localScale = new Vector3(0.28f, 0.18f, 1f);
            var clothRenderer = cloth.AddComponent<SpriteRenderer>();
            clothRenderer.sprite = RuntimeSprite.White;
            clothRenderer.color = clothColor;
            clothRenderer.sortingOrder = 4;

            if (flagId == "checkered")
            {
                var stripe = new GameObject("Flag Stripe");
                stripe.transform.SetParent(cloth.transform, false);
                stripe.transform.localScale = new Vector3(0.35f, 1.05f, 1f);
                var stripeRenderer = stripe.AddComponent<SpriteRenderer>();
                stripeRenderer.sprite = RuntimeSprite.White;
                stripeRenderer.color = new Color(0.12f, 0.16f, 0.22f);
                stripeRenderer.sortingOrder = 5;
            }
        }

        public void PlayBump(float collisionSpeed)
        {
            if (string.IsNullOrEmpty(bumpSoundId) || bumpSoundId == "silent" || collisionSpeed < 0.5f) return;
            if (!SoundClips.TryGetValue(bumpSoundId, out var clip))
            {
                clip = CreateSound(bumpSoundId);
                SoundClips.Add(bumpSoundId, clip);
            }

            audioSource.pitch = Mathf.Clamp(0.85f + collisionSpeed * 0.025f, 0.85f, 1.15f);
            audioSource.volume = Mathf.Clamp01(collisionSpeed / 8f);
            audioSource.PlayOneShot(clip);
        }

        private static AudioClip CreateSound(string soundId)
        {
            const int sampleRate = 22050;
            const int sampleCount = 2646;
            var clip = AudioClip.Create($"Bumper {soundId}", sampleCount, 1, sampleRate, false);
            var samples = new float[sampleCount];
            var startFrequency = soundId switch
            {
                "arcade" => 620f,
                "laser" => 980f,
                _ => 280f
            };
            var endFrequency = soundId switch
            {
                "arcade" => 180f,
                "laser" => 320f,
                _ => 90f
            };
            for (var i = 0; i < sampleCount; i++)
            {
                var t = i / (float)sampleRate;
                var progress = i / (float)sampleCount;
                var frequency = Mathf.Lerp(startFrequency, endFrequency, progress);
                var envelope = Mathf.Exp(-progress * 10f) * Mathf.Min(1f, progress * 35f);
                samples[i] = Mathf.Sin(2f * Mathf.PI * frequency * t) * envelope * 0.45f;
            }
            clip.SetData(samples, 0);
            return clip;
        }
    }
}

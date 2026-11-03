using UnityEngine;

namespace Seabreeze
{
    public class AmbientAudio : MonoBehaviour
    {
        AudioSource ocean, pad, steps;
        float foot;
        public void Initialize()
        {
            ocean = gameObject.AddComponent<AudioSource>();
            ocean.clip = MakeOcean();
            ocean.loop = true;
            ocean.volume = .15f;
            ocean.Play();
            pad = gameObject.AddComponent<AudioSource>();
            pad.clip = MakePad();
            pad.loop = true;
            pad.volume = .055f;
            pad.Play();
            steps = gameObject.AddComponent<AudioSource>();
            steps.clip = MakeStep();
            steps.volume = .075f;
        }

        void Update()
        {
            if (GameSession.Instance == null || ocean == null)
                return;
            float target = GameSession.Instance.CurrentPlace == PlaceId.Cafe ? .035f : .15f;
            ocean.volume = Mathf.Lerp(ocean.volume, target, Time.unscaledDeltaTime * 2);
            var w = WorldDirector.Instance;
            if (w == null || w.Player == null)
                return;
            var p = w.Player.GetComponent<PlayerController>();
            if (!GameSession.Instance.InputBlocked && p.Grounded && Mathf.Abs(p.MoveAxis) > .1f)
            {
                foot += Time.deltaTime;
                if (foot > .34f)
                {
                    foot = 0;
                    steps.pitch = Random.Range(.85f, 1.12f);
                    steps.Play();
                }
            }
        }

        static AudioClip MakeOcean()
        {
            int rate = 22050, n = rate * 16;
            float[] data = new float[n];
            var rng = new System.Random(23);
            float filter = 0;
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate;
                filter = filter * .965f + ((float)rng.NextDouble() * 2 - 1) * .035f;
                float swell = .55f + .3f * Mathf.Sin(2 * Mathf.PI * t / 8) + .12f * Mathf.Sin(2 * Mathf.PI * t / 4);
                float edge = Mathf.Min(1, Mathf.Min(t, 16 - t) * 2);
                data[i] = filter * swell * edge * 3;
            }

            var c = AudioClip.Create("Original synthesized shoreline", n, 1, rate, false);
            c.SetData(data, 0);
            return c;
        }

        static AudioClip MakePad()
        {
            int rate = 22050, n = rate * 16;
            float[] data = new float[n];
            float[] freq = {130.8125f, 164.8125f, 196, 246.9375f};
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / rate, v = 0;
                foreach (float f in freq)
                    v += Mathf.Sin(2 * Mathf.PI * f * t) * .12f;
                data[i] = v * Mathf.Pow(Mathf.Sin(Mathf.PI * t / 16), 2);
            }

            var c = AudioClip.Create("Original quiet chord", n, 1, rate, false);
            c.SetData(data, 0);
            return c;
        }

        static AudioClip MakeStep()
        {
            int rate = 22050, n = 2205;
            float[] data = new float[n];
            var rng = new System.Random(17);
            for (int i = 0; i < n; i++)
            {
                float t = (float)i / n;
                data[i] = ((float)rng.NextDouble() - .5f) * Mathf.Exp(-t * 14) * .7f;
            }

            var c = AudioClip.Create("Soft pavement step", n, 1, rate, false);
            c.SetData(data, 0);
            return c;
        }
    }
}

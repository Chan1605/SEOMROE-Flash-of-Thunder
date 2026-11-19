using UnityEngine;

// 보스 소리: Resources/Audio/Boss/<이름>_01.wav ~ (번호 없는 <이름>.wav도 읽음). 같은 이름으로 파일을 바꾸면 그대로 적용
// 보스 몸에서 나는 소리는 자식 AudioSource(반 입체), 바닥 낙뢰처럼 다른 자리 소리는 그 자리에서
public class BossSfx : MonoBehaviour
{
    public enum Cue { Swing, GrabTell, LeapJump, LeapLand, Wave, DashTell, Dash, RainCast, Thunder, Roar, Death }

    static readonly string[] Names =
    {
        "boss_swing", "boss_grab_tell", "boss_leap_jump", "boss_leap_land", "boss_wave",
        "boss_dash_tell", "boss_dash", "boss_rain_cast", "boss_thunder", "boss_roar", "boss_death"
    };
    static readonly float[] Volumes = { 0.7f, 0.8f, 0.7f, 1f, 0.85f, 0.7f, 0.9f, 0.9f, 0.85f, 0.75f, 1f };

    AudioClip[][] clips;
    AudioSource source;
    readonly int[] last = new int[Names.Length];

    void Awake()
    {
        clips = new AudioClip[Names.Length][];
        for (int i = 0; i < Names.Length; i++) clips[i] = Load(Names[i]);

        var go = new GameObject("Sfx_Boss");
        go.transform.SetParent(transform, false);
        go.transform.localPosition = Vector3.up * 1.2f;
        source = go.AddComponent<AudioSource>();
        source.playOnAwake = false;
        source.spatialBlend = 0.6f;
        source.minDistance = 6f;
        source.maxDistance = 45f;
    }

    static AudioClip[] Load(string name)
    {
        var list = new System.Collections.Generic.List<AudioClip>();
        AudioClip single = Resources.Load<AudioClip>("Audio/Boss/" + name);
        if (single != null) list.Add(single);
        for (int n = 1; n <= 6; n++)
        {
            AudioClip c = Resources.Load<AudioClip>($"Audio/Boss/{name}_{n:00}");
            if (c != null) list.Add(c);
        }
        return list.ToArray();
    }

    AudioClip Pick(Cue cue)
    {
        AudioClip[] set = clips != null ? clips[(int)cue] : null;
        if (set == null || set.Length == 0) return null;
        int i = set.Length == 1 ? 0 : (last[(int)cue] + Random.Range(1, set.Length)) % set.Length; // 직전과 다른 변형
        last[(int)cue] = i;
        return set[i];
    }

    public void Play(Cue cue, float volume = 1f)
    {
        AudioClip clip = Pick(cue);
        if (clip == null || source == null) return;
        source.pitch = cue == Cue.Swing ? Random.Range(0.94f, 1.06f) : 1f;
        source.PlayOneShot(clip, Volumes[(int)cue] * volume * AudioOptions.SfxVolume);
    }

    public void PlayAt(Cue cue, Vector3 position, float volume = 1f)
    {
        AudioClip clip = Pick(cue);
        if (clip != null) AudioSource.PlayClipAtPoint(clip, position, Volumes[(int)cue] * volume * AudioOptions.SfxVolume);
    }
}

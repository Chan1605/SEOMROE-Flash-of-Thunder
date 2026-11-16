using System.Linq;
using UnityEditor;
using UnityEngine;

// Player에 PlayerSfx를 붙이고 Assets/8.Audio/SFX/Player의 소리를 연결 (다시 실행해도 됨, 볼륨 등 조절값은 유지)
//  - [변경] air_slash_*.wav 전부 → 허공을 벨 때 (예전 air_swing_*는 안 씀)
//  - [변경] hit_slice_*.wav 전부 → 적을 벨 때 (예전 sword_swing_*는 안 씀)
//  - dash_cut_*.wav 전부 → 일섬 돌진(표식한 적을 지나갈 때)
//  - stance_charge_start / stance_charge_loop → 발도 자세 진입 / Q 차지 반복
//  - skill_strike_*.wav 전부 → 일섬 적중 낙뢰
//  - parry_clash.wav → ParryFx의 칼 부딪힘 소리 (비어 있을 때만, 패링·간파 공용)
//  - hit_body_* / hit_thump_* → 타격음 아래 두 겹, footstep_* → 발소리
// 진입음은 처음 실행할 때만 연결 (인스펙터에서 바꾼 소리를 덮어쓰지 않음)
public static class PlayerSfxSetup
{
    const string PlayerPrefabPath = "Assets/2.Model/Prefabs/Player.prefab";
    const string Folder = "Assets/8.Audio/SFX/Player";

    [MenuItem("Tools/Setup/Player Sfx")]
    static void Run()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            Debug.LogError("[PlayerSfx] 플레이 모드가 아닐 때 실행하세요.");
            return;
        }

        AudioClip[] swings = LoadAll("hit_slice_"); // [변경]
        AudioClip[] strikes = LoadAll("skill_strike_");
        AudioClip[] airSwings = LoadAll("air_slash_"); // [변경]
        AudioClip[] dashCuts = LoadAll("dash_cut_");
        var parryClash = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/parry_clash.wav");
        AudioClip[] hitBodies = LoadAll("hit_body_");
        AudioClip[] hitThumps = LoadAll("hit_thump_");
        AudioClip[] footsteps = LoadAll("footstep_");
        var start = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/stance_charge_start.wav");
        var loop = AssetDatabase.LoadAssetAtPath<AudioClip>(Folder + "/stance_charge_loop.wav");

        // 반복음은 메모리에 풀어 두고(끊김 방지), 짧은 휘두르기도 바로 재생되게
        foreach (AudioClip clip in swings.Concat(strikes).Concat(airSwings).Concat(dashCuts).Concat(hitBodies).Concat(hitThumps).Concat(footsteps).Append(start).Append(loop).Append(parryClash).Where(c => c != null))
        {
            var importer = AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(clip)) as AudioImporter;
            if (importer == null) continue;
            AudioImporterSampleSettings s = importer.defaultSampleSettings;
            s.loadType = AudioClipLoadType.DecompressOnLoad;
            importer.defaultSampleSettings = s;
            importer.SaveAndReimport();
        }

        GameObject root = PrefabUtility.LoadPrefabContents(PlayerPrefabPath);
        try
        {
            PlayerSfx sfx = root.GetComponent<PlayerSfx>();
            if (sfx == null) sfx = root.AddComponent<PlayerSfx>();

            var so = new SerializedObject(sfx);
            so.FindProperty("combat").objectReferenceValue = root.GetComponent<PlayerCombat>();
            so.FindProperty("stats").objectReferenceValue = root.GetComponent<PlayerStats>();
            so.FindProperty("skill").objectReferenceValue = root.GetComponent<PlayerSkill>();
            so.FindProperty("heavyCharge").objectReferenceValue = root.GetComponent<PlayerChargeAttack>();
            so.FindProperty("weapon").objectReferenceValue = root.GetComponentInChildren<WeaponHitbox>(true); // 적중 소리용 (공격 시작·공속 연결 삭제)

            SerializedProperty array = so.FindProperty("swingClips");
            array.arraySize = swings.Length;
            for (int i = 0; i < swings.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = swings[i];
            SerializedProperty startProp = so.FindProperty("stanceStart");
            if (startProp.objectReferenceValue == null) startProp.objectReferenceValue = start;
            so.FindProperty("stanceLoop").objectReferenceValue = loop;
            SetArray(so, "airSwingClips", airSwings);
            SetArray(so, "dashCutClips", dashCuts);
            SetArray(so, "hitBodyClips", hitBodies);
            SetArray(so, "hitThumpClips", hitThumps);
            SetArray(so, "footstepClips", footsteps);
            SerializedProperty strikeArray = so.FindProperty("strikeClips");
            strikeArray.arraySize = strikes.Length;
            for (int i = 0; i < strikes.Length; i++) strikeArray.GetArrayElementAtIndex(i).objectReferenceValue = strikes[i];
            so.ApplyModifiedPropertiesWithoutUndo();

            ParryFx parryFx = root.GetComponent<ParryFx>();
            if (parryFx != null && parryClash != null)
            {
                var fxSo = new SerializedObject(parryFx);
                SerializedProperty clash = fxSo.FindProperty("clashClip");
                if (clash.objectReferenceValue == null) clash.objectReferenceValue = parryClash;
                fxSo.ApplyModifiedPropertiesWithoutUndo();
            }

            PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            Debug.Log($"[PlayerSfx] 완료 · 휘두르기 {airSwings.Length}개, 적중 {swings.Length}개, 돌진 {dashCuts.Length}개, 일섬 낙뢰 {strikes.Length}개, 발도 진입 {(start != null ? "O" : "X")}, 반복 {(loop != null ? "O" : "X")}");
        }
        finally
        {
            PrefabUtility.UnloadPrefabContents(root);
        }
    }

    // 이름이 prefix로 시작하는 소리 전부 (이름순)
    static AudioClip[] LoadAll(string prefix) => AssetDatabase.FindAssets(prefix + " t:AudioClip", new[] { Folder })
        .Select(AssetDatabase.GUIDToAssetPath)
        .Where(p => System.IO.Path.GetFileName(p).StartsWith(prefix))
        .OrderBy(p => p)
        .Select(AssetDatabase.LoadAssetAtPath<AudioClip>)
        .Where(c => c != null)
        .ToArray();

    static void SetArray(SerializedObject so, string field, AudioClip[] clips)
    {
        SerializedProperty array = so.FindProperty(field);
        array.arraySize = clips.Length;
        for (int i = 0; i < clips.Length; i++) array.GetArrayElementAtIndex(i).objectReferenceValue = clips[i];
    }
}

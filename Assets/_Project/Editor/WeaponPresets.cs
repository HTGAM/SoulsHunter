using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SoulsHunter.Player;

namespace SoulsHunter.EditorTools
{
    /// <summary>
    /// 무기 6종(WeaponData 에셋)을 만든다: 한손검, 대검, 쌍검, 해머, 랜스, 조충곤.
    /// 에셋은 Assets/_Project/Data/Weapons 에 저장된다.
    /// - Build Test Arena: 없는 무기만 만든다 (Inspector에서 고친 수치는 유지)
    /// - 메뉴 SoulsHunter > Reset Weapon Presets: 모든 무기를 이 코드의 기본값으로 되돌린다
    /// </summary>
    public static class WeaponPresets
    {
        const string Folder = "Assets/_Project/Data/Weapons";

        static readonly Color Steel = new(0.8f, 0.82f, 0.86f);
        static readonly Color Iron = new(0.3f, 0.3f, 0.33f);
        static readonly Color Wood = new(0.42f, 0.3f, 0.2f);
        static readonly Color Leather = new(0.35f, 0.22f, 0.14f);

        [MenuItem("SoulsHunter/Reset Weapon Presets")]
        static void ResetAll()
        {
            if (!EditorUtility.DisplayDialog("SoulsHunter", "무기 6종의 수치를 기본값으로 되돌릴까요?\n(Inspector에서 고친 값이 사라져요)", "되돌리기", "취소")) return;
            GetOrCreateAll(overwrite: true);
            Debug.Log("[SoulsHunter] 무기 프리셋을 기본값으로 되돌렸어요.");
        }

        /// <summary>무기 6종 (거치대 순서). 첫 번째가 처음 드는 무기.</summary>
        public static List<WeaponData> GetOrCreateAll(bool overwrite = false)
        {
            if (!AssetDatabase.IsValidFolder("Assets/_Project/Data")) AssetDatabase.CreateFolder("Assets/_Project", "Data");
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project/Data", "Weapons");

            var list = new List<WeaponData>
            {
                Get("SwordAndShield", overwrite, SwordAndShield),
                Get("GreatSword", overwrite, GreatSword),
                Get("DualBlades", overwrite, DualBlades),
                Get("Hammer", overwrite, Hammer),
                Get("Lance", overwrite, Lance),
                Get("InsectGlaive", overwrite, InsectGlaive),
            };
            AssetDatabase.SaveAssets();
            return list;
        }

        static WeaponData Get(string file, bool overwrite, System.Action<WeaponData> setup)
        {
            string path = $"{Folder}/{file}.asset";
            var w = AssetDatabase.LoadAssetAtPath<WeaponData>(path);
            if (w && !overwrite) return w;
            bool isNew = !w;
            if (isNew) w = ScriptableObject.CreateInstance<WeaponData>();
            w.lightCombo = new();
            w.heavyCombo = new();
            w.visuals = new();
            w.specialAttack = new AttackStep();
            setup(w);
            if (isNew) AssetDatabase.CreateAsset(w, path);
            EditorUtility.SetDirty(w);
            return w;
        }

        // 공격 하나: 이름, 피해, 강인 피해, 스태미나, 예고, 판정, 후딜, 캔슬 가능 시점, 전진, 판정 중심, 판정 크기, 히트스톱
        static AttackStep S(string name, float dmg, float poise, float stam, float windup, float active, float recovery,
                            float cancel, float lunge, Vector3 center, Vector3 size, float hitStop) =>
            new(name, dmg, poise, stam, windup, active, recovery, cancel, lunge, center, size, hitStop);

        static AttackStep Charge(AttackStep a, int levels, float perLevel, float dmgPerLevel, float poisePerLevel)
        {
            a.chargeLevels = levels;
            a.chargeTimePerLevel = perLevel;
            a.chargeDamagePerLevel = dmgPerLevel;
            a.chargePoisePerLevel = poisePerLevel;
            return a;
        }

        static WeaponData.VisualPart V(PrimitiveType shape, Vector3 pos, Vector3 rot, Vector3 scale, Color color, bool shield = false) =>
            new() { shape = shape, position = pos, rotation = rot, scale = scale, color = color, isShield = shield };

        // ───────── 한손검: 기본기. 빠른 콤보 + 방패 가드 ─────────
        static void SwordAndShield(WeaponData w)
        {
            w.EditorSetup("한손검", "빠르고 무난한 검과 방패. 가드와 패링이 쉽다.");
            w.lightCombo.Add(S("Slash 1", 20, 10, 14, 0.22f, 0.12f, 0.35f, 0.50f, 0.6f, new(0, 1, 1.1f), new(1.6f, 1.2f, 1.6f), 0.05f));
            w.lightCombo.Add(S("Slash 2", 22, 10, 14, 0.20f, 0.12f, 0.35f, 0.48f, 0.6f, new(0, 1, 1.1f), new(1.8f, 1.2f, 1.6f), 0.05f));
            w.lightCombo.Add(S("Slash 3", 32, 20, 18, 0.32f, 0.15f, 0.50f, 0.70f, 1.0f, new(0, 1, 1.2f), new(1.4f, 1.4f, 2.0f), 0.08f));
            w.heavyCombo.Add(S("Shield Bash", 30, 35, 18, 0.35f, 0.12f, 0.45f, 0.70f, 0.8f, new(0, 1, 1.0f), new(1.4f, 1.4f, 1.6f), 0.08f));
            w.heavyCombo.Add(S("Leaping Slash", 50, 35, 25, 0.50f, 0.15f, 0.60f, 0.95f, 2.0f, new(0, 1, 1.3f), new(1.8f, 1.6f, 2.2f), 0.12f));
            w.moveSpeedMultiplier = 1f;
            w.dodgeDistanceMultiplier = 1f;
            w.canGuard = true; w.guardReduction = 0.85f; w.guardStaminaMultiplier = 1f; w.guardAngle = 150f;
            w.special = WeaponData.Special.None;
            w.visuals.Add(V(PrimitiveType.Cube, new(0.45f, 1.1f, 0.65f), Vector3.zero, new(0.07f, 0.07f, 1.0f), Steel));
            w.visuals.Add(V(PrimitiveType.Cube, new(0.45f, 1.1f, 0.15f), Vector3.zero, new(0.3f, 0.06f, 0.06f), Leather));
            w.visuals.Add(V(PrimitiveType.Cube, new(-0.5f, 1.0f, 0.35f), Vector3.zero, new(0.08f, 0.7f, 0.55f), Wood, shield: true));
        }

        // ───────── 대검: 느리지만 한 방. 강공격을 모아서 내려친다 ─────────
        static void GreatSword(WeaponData w)
        {
            w.EditorSetup("대검", "느리고 무겁다. 우클릭을 누르고 있으면 힘을 모아 최대 3단계까지 강해진다.");
            w.lightCombo.Add(S("Overhead Slash", 38, 25, 18, 0.45f, 0.15f, 0.60f, 0.95f, 0.5f, new(0, 1, 1.4f), new(1.4f, 2.0f, 2.6f), 0.10f));
            w.lightCombo.Add(S("Wide Slash", 42, 25, 18, 0.40f, 0.15f, 0.60f, 0.90f, 0.6f, new(0, 1, 1.3f), new(3.0f, 1.4f, 2.2f), 0.10f));
            w.heavyCombo.Add(Charge(S("Charged Slash", 60, 40, 25, 0.65f, 0.18f, 0.85f, 1.25f, 0.4f, new(0, 1, 1.5f), new(1.6f, 2.2f, 2.8f), 0.12f), 3, 0.5f, 0.55f, 0.5f));
            w.heavyCombo.Add(Charge(S("Strong Charged Slash", 75, 50, 28, 0.60f, 0.18f, 0.90f, 1.30f, 0.6f, new(0, 1, 1.5f), new(1.8f, 2.2f, 2.8f), 0.14f), 3, 0.5f, 0.55f, 0.5f));
            w.moveSpeedMultiplier = 0.85f;
            w.dodgeDistanceMultiplier = 0.9f;
            w.canGuard = true; w.guardReduction = 0.75f; w.guardStaminaMultiplier = 1.4f; w.guardAngle = 120f;
            w.special = WeaponData.Special.None;
            w.visuals.Add(V(PrimitiveType.Cube, new(0.5f, 1.3f, 1.0f), Vector3.zero, new(0.1f, 0.35f, 1.9f), Steel));
            w.visuals.Add(V(PrimitiveType.Cube, new(0.5f, 1.3f, 0.0f), Vector3.zero, new(0.08f, 0.5f, 0.08f), Iron));
            w.visuals.Add(V(PrimitiveType.Cube, new(0.5f, 1.3f, -0.25f), Vector3.zero, new(0.07f, 0.07f, 0.45f), Leather));
        }

        // ───────── 쌍검: 아주 빠른 연타. 가드 불가. 귀인화로 더 빠르고 강하게 ─────────
        static void DualBlades(WeaponData w)
        {
            w.EditorSetup("쌍검", "아주 빠른 연타. 가드는 못 한다. F로 귀인화하면 스태미나를 태우며 더 빠르고 강해진다.");
            w.lightCombo.Add(S("Slash 1", 10, 4, 7, 0.10f, 0.08f, 0.18f, 0.26f, 0.3f, new(0, 1, 1.1f), new(1.8f, 1.2f, 1.6f), 0.03f));
            w.lightCombo.Add(S("Slash 2", 10, 4, 7, 0.10f, 0.08f, 0.18f, 0.26f, 0.3f, new(0, 1, 1.1f), new(1.8f, 1.2f, 1.6f), 0.03f));
            w.lightCombo.Add(S("Slash 3", 12, 5, 8, 0.11f, 0.08f, 0.20f, 0.28f, 0.4f, new(0, 1, 1.1f), new(1.8f, 1.2f, 1.6f), 0.03f));
            w.lightCombo.Add(S("Cross Slash", 16, 8, 9, 0.14f, 0.10f, 0.30f, 0.40f, 0.5f, new(0, 1, 1.2f), new(2.0f, 1.3f, 1.8f), 0.05f));
            w.heavyCombo.Add(S("Lunging Strike", 16, 8, 10, 0.15f, 0.12f, 0.30f, 0.38f, 2.0f, new(0, 1, 1.3f), new(1.6f, 1.2f, 2.4f), 0.04f));
            w.heavyCombo.Add(S("Rising Spiral", 20, 10, 12, 0.18f, 0.15f, 0.35f, 0.45f, 0.5f, new(0, 1, 0), new(3.2f, 1.6f, 3.2f), 0.05f));
            w.heavyCombo.Add(S("Blade Dance", 34, 18, 18, 0.25f, 0.20f, 0.50f, 0.60f, 1.0f, new(0, 1, 0.6f), new(3.4f, 1.6f, 3.4f), 0.06f));
            w.moveSpeedMultiplier = 1.1f;
            w.dodgeDistanceMultiplier = 0.9f;
            w.canGuard = false;
            w.special = WeaponData.Special.DemonMode;
            w.demonDamageMultiplier = 1.25f; w.demonAttackSpeed = 1.3f; w.demonStaminaPerSecond = 12f;
            var blade = new Color(0.85f, 0.6f, 0.55f);
            w.visuals.Add(V(PrimitiveType.Cube, new(0.45f, 1.0f, 0.55f), Vector3.zero, new(0.06f, 0.06f, 0.7f), blade));
            w.visuals.Add(V(PrimitiveType.Cube, new(-0.45f, 1.0f, 0.55f), Vector3.zero, new(0.06f, 0.06f, 0.7f), blade));
            w.visuals.Add(V(PrimitiveType.Cube, new(0.45f, 1.0f, 0.15f), Vector3.zero, new(0.06f, 0.06f, 0.2f), Leather));
            w.visuals.Add(V(PrimitiveType.Cube, new(-0.45f, 1.0f, 0.15f), Vector3.zero, new(0.06f, 0.06f, 0.2f), Leather));
        }

        // ───────── 해머: 머리를 때려 기절(강인도 붕괴)시키는 둔기. 강공격 모으기 ─────────
        static void Hammer(WeaponData w)
        {
            w.EditorSetup("해머", "강인도를 크게 깎는 둔기. 머리를 노려 몬스터를 넘어뜨려라. 우클릭을 누르고 있으면 모으기.");
            w.lightCombo.Add(S("Side Smash", 28, 30, 15, 0.30f, 0.14f, 0.45f, 0.70f, 0.5f, new(0, 1, 1.3f), new(1.6f, 1.6f, 2.0f), 0.08f));
            w.lightCombo.Add(S("Upswing", 34, 40, 16, 0.35f, 0.14f, 0.55f, 0.80f, 0.5f, new(0, 1.2f, 1.3f), new(1.4f, 2.0f, 2.0f), 0.10f));
            w.heavyCombo.Add(Charge(S("Charged Slam", 45, 50, 22, 0.45f, 0.16f, 0.70f, 1.00f, 0.6f, new(0, 0.8f, 1.4f), new(1.8f, 1.8f, 2.2f), 0.12f), 3, 0.45f, 0.6f, 0.6f));
            w.heavyCombo.Add(Charge(S("Big Swing", 55, 55, 24, 0.50f, 0.20f, 0.80f, 1.10f, 1.0f, new(0, 1, 0.8f), new(3.2f, 1.8f, 3.0f), 0.14f), 2, 0.5f, 0.5f, 0.6f));
            w.moveSpeedMultiplier = 0.95f;
            w.dodgeDistanceMultiplier = 1f;
            w.canGuard = false;
            w.special = WeaponData.Special.None;
            w.visuals.Add(V(PrimitiveType.Cylinder, new(0.45f, 1.1f, 0.6f), new(90, 0, 0), new(0.07f, 0.55f, 0.07f), Wood));
            w.visuals.Add(V(PrimitiveType.Cube, new(0.45f, 1.1f, 1.2f), Vector3.zero, new(0.42f, 0.42f, 0.36f), Iron));
        }

        // ───────── 랜스: 최강의 방패. 짧은 스텝. 카운터 자세에서 반격 ─────────
        static void Lance(WeaponData w)
        {
            w.EditorSetup("랜스", "거대한 방패로 거의 모든 공격을 막는다. 회피는 짧은 스텝. F로 카운터 자세 → 막으면 반격 찌르기.");
            w.lightCombo.Add(S("Mid Thrust 1", 15, 8, 9, 0.18f, 0.10f, 0.30f, 0.42f, 0.3f, new(0, 1.1f, 1.9f), new(0.8f, 0.8f, 3.2f), 0.04f));
            w.lightCombo.Add(S("Mid Thrust 2", 15, 8, 9, 0.18f, 0.10f, 0.30f, 0.42f, 0.3f, new(0, 1.1f, 1.9f), new(0.8f, 0.8f, 3.2f), 0.04f));
            w.lightCombo.Add(S("Mid Thrust 3", 19, 12, 10, 0.20f, 0.10f, 0.40f, 0.50f, 0.4f, new(0, 1.1f, 1.9f), new(0.9f, 0.9f, 3.4f), 0.05f));
            w.heavyCombo.Add(S("High Thrust", 22, 14, 12, 0.25f, 0.12f, 0.40f, 0.55f, 0.4f, new(0, 1.6f, 2.0f), new(0.8f, 0.8f, 3.4f), 0.05f));
            w.heavyCombo.Add(S("Charging Thrust", 30, 25, 20, 0.35f, 0.25f, 0.60f, 0.80f, 3.0f, new(0, 1.1f, 1.6f), new(1.2f, 1.2f, 2.4f), 0.07f));
            w.moveSpeedMultiplier = 0.8f;
            w.dodgeDistanceMultiplier = 0.45f;
            w.canGuard = true; w.guardReduction = 0.95f; w.guardStaminaMultiplier = 0.55f; w.guardAngle = 170f;
            w.special = WeaponData.Special.Counter;
            w.counterWindow = 0.7f; w.counterStaminaCost = 10f;
            w.specialAttack = S("Counter Thrust", 50, 40, 0, 0.08f, 0.14f, 0.45f, 0.50f, 0.8f, new(0, 1.1f, 2.0f), new(1.0f, 1.0f, 3.4f), 0.10f);
            var shield = new Color(0.35f, 0.42f, 0.55f);
            w.visuals.Add(V(PrimitiveType.Cylinder, new(0.45f, 1.15f, 1.1f), new(90, 0, 0), new(0.1f, 1.4f, 0.1f), Steel));
            w.visuals.Add(V(PrimitiveType.Cube, new(0.45f, 1.15f, -0.2f), Vector3.zero, new(0.22f, 0.22f, 0.12f), Iron));
            w.visuals.Add(V(PrimitiveType.Cube, new(-0.55f, 1.0f, 0.35f), Vector3.zero, new(0.1f, 1.1f, 0.8f), shield, shield: true));
        }

        // ───────── 조충곤: 도약해서 공중전. 공중 베기가 맞으면 다시 튀어오른다 ─────────
        static void InsectGlaive(WeaponData w)
        {
            w.EditorSetup("조충곤", "가볍게 휘두르는 창. F로 높이 도약하고, 공중에서 좌클릭으로 베면 다시 튀어올라 연속 공중전이 가능하다.");
            w.lightCombo.Add(S("Rising Slash", 16, 8, 10, 0.18f, 0.12f, 0.30f, 0.42f, 0.5f, new(0, 1.1f, 1.5f), new(2.0f, 1.6f, 2.4f), 0.04f));
            w.lightCombo.Add(S("Reaping Slash", 18, 9, 10, 0.18f, 0.12f, 0.32f, 0.44f, 0.5f, new(0, 1.1f, 1.5f), new(2.4f, 1.4f, 2.4f), 0.04f));
            w.lightCombo.Add(S("Wide Sweep", 22, 12, 12, 0.22f, 0.14f, 0.38f, 0.50f, 0.5f, new(0, 1, 1.0f), new(3.6f, 1.4f, 3.0f), 0.05f));
            w.heavyCombo.Add(S("Overhead Smash", 26, 18, 14, 0.30f, 0.14f, 0.45f, 0.60f, 0.6f, new(0, 1, 1.6f), new(1.6f, 2.0f, 2.6f), 0.07f));
            w.heavyCombo.Add(S("Tornado Slash", 30, 20, 16, 0.30f, 0.20f, 0.50f, 0.65f, 0.5f, new(0, 1, 0), new(4.0f, 1.6f, 4.0f), 0.07f));
            w.moveSpeedMultiplier = 1.05f;
            w.dodgeDistanceMultiplier = 1f;
            w.canGuard = false;
            w.special = WeaponData.Special.Vault;
            w.vaultUpVelocity = 11f; w.vaultForwardSpeed = 6f; w.vaultStaminaCost = 18f;
            var air = S("Aerial Slash", 22, 15, 0, 0.10f, 0.15f, 0.25f, 0.30f, 0.4f, new(0, 0.6f, 0.9f), new(2.4f, 2.6f, 2.4f), 0.05f);
            air.bounceOnHit = 7f;
            w.specialAttack = air;
            var blade = new Color(0.55f, 0.75f, 0.5f);
            w.visuals.Add(V(PrimitiveType.Cylinder, new(0.4f, 1.1f, 0.6f), new(90, 0, 0), new(0.06f, 0.9f, 0.06f), Iron));
            w.visuals.Add(V(PrimitiveType.Cube, new(0.4f, 1.1f, 1.6f), Vector3.zero, new(0.05f, 0.25f, 0.35f), blade));
            w.visuals.Add(V(PrimitiveType.Cube, new(0.4f, 1.1f, -0.4f), Vector3.zero, new(0.05f, 0.2f, 0.25f), blade));
        }
    }
}

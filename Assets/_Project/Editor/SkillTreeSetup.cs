using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using SoulsHunter.Items;
using SoulsHunter.Progression;

namespace SoulsHunter.EditorTools
{
    /// <summary>
    /// 혼의 스킬 트리 에셋을 만든다 (Data/Progression/SkillTree.asset).
    /// 가운데 시작 노드에서 네 갈래(공격·방어·생존·사냥)가 뻗고, 갈래마다 노드 10개 (핵심 노드 2개).
    /// 이미 있으면 그대로 쓴다 (직접 고친 수치 유지). 새로 만들려면 메뉴 SoulsHunter/Rebuild Skill Tree.
    /// </summary>
    public static class SkillTreeSetup
    {
        const string Folder = "Assets/_Project/Data/Progression";
        const string TreePath = Folder + "/SkillTree.asset";

        public static ItemData IronSoul() => TestArenaBuilder.GetOrCreateItem("IronSoul", "철갑의 혼",
            "Ravager에게서 드물게 거둘 수 있는 단단한 혼. 공격·방어 갈래의 핵심 노드를 일깨운다.", 7);

        public static ItemData CinderSoul() => TestArenaBuilder.GetOrCreateItem("CinderSoul", "잿불의 혼",
            "Cinder Wyrm에게서 드물게 거둘 수 있는 타오르는 혼. 생존·사냥 갈래의 핵심 노드를 일깨운다.", 7);

        [MenuItem("SoulsHunter/Rebuild Skill Tree")]
        static void RebuildMenu()
        {
            if (!EditorUtility.DisplayDialog("SoulsHunter", "스킬 트리를 기본값으로 다시 만들까요?\n직접 고친 노드 수치는 사라져요. (저장된 해금 기록은 같은 ID라면 유지)", "다시 만들기", "취소"))
                return;
            var tree = GetOrCreate();
            Fill(tree);
            EditorUtility.SetDirty(tree);
            AssetDatabase.SaveAssets();
            Selection.activeObject = tree;
        }

        public static SkillTreeData GetOrCreate()
        {
            var tree = AssetDatabase.LoadAssetAtPath<SkillTreeData>(TreePath);
            if (tree) return tree;
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/_Project/Data", "Progression");
            tree = ScriptableObject.CreateInstance<SkillTreeData>();
            Fill(tree);
            AssetDatabase.CreateAsset(tree, TreePath);
            AssetDatabase.SaveAssets();
            return tree;
        }

        // 갈래 하나의 모양 (바깥 방향 x, 옆 방향 y):
        //   1 ─ 2 ┬ 3u ─ 4u ─ K1 ┐
        //         └ 3l ─ 4l ─ 5l ┴ 6 ─ K2
        static readonly (string key, Vector2 pos)[] Shape =
        {
            ("1", new(1, 0)), ("2", new(2, 0)),
            ("3u", new(3, -1)), ("4u", new(4, -1)), ("K1", new(5, -1)),
            ("3l", new(3, 1)), ("4l", new(4, 1)), ("5l", new(5, 1)),
            ("6", new(6, 0)), ("K2", new(7, 0)),
        };

        static readonly (string from, string to)[] Links =
        {
            ("root", "1"), ("1", "2"), ("2", "3u"), ("3u", "4u"), ("4u", "K1"),
            ("2", "3l"), ("3l", "4l"), ("4l", "5l"), ("K1", "6"), ("5l", "6"), ("6", "K2"),
        };

        class Spec
        {
            public string title, glyph;
            public SkillEffect[] effects;
        }

        static Spec S(string title, string glyph, params (SkillStat stat, float value)[] fx)
        {
            var list = new List<SkillEffect>();
            foreach (var (stat, value) in fx) list.Add(new SkillEffect(stat, value));
            return new Spec { title = title, glyph = glyph, effects = list.ToArray() };
        }

        static void Fill(SkillTreeData tree)
        {
            tree.baseCost = 100;
            tree.costGrowth = 1.10f;
            tree.nodes = new List<SkillNode>
            {
                new() { id = "root", title = "혼의 근원", glyph = "魂", branch = SkillBranch.Root, position = Vector2.zero },
            };

            var iron = IronSoul();
            var cinder = CinderSoul();

            // 공격 (오른쪽)
            AddBranch(tree, "off", SkillBranch.Offense, p => new Vector2(p.x, p.y), iron, new Dictionary<string, Spec>
            {
                ["1"] = S("날 선 의지", "공", (SkillStat.AttackPower, 0.04f)),
                ["2"] = S("사냥꾼의 팔", "공", (SkillStat.AttackPower, 0.04f)),
                ["3u"] = S("급소 찌르기", "약", (SkillStat.WeakPointDamage, 0.08f)),
                ["4u"] = S("약점 노리기", "약", (SkillStat.WeakPointDamage, 0.08f)),
                ["K1"] = S("약점 간파", "간", (SkillStat.WeakPointDamage, 0.20f), (SkillStat.AttackPower, 0.05f)),
                ["3l"] = S("부수는 일격", "파", (SkillStat.PartBreak, 0.15f)),
                ["4l"] = S("뼈 가르기", "파", (SkillStat.PartBreak, 0.15f)),
                ["5l"] = S("단련된 손목", "공", (SkillStat.AttackPower, 0.05f)),
                ["6"] = S("포식의 기세", "공", (SkillStat.AttackPower, 0.05f)),
                ["K2"] = S("포식자의 이빨", "포", (SkillStat.AttackPower, 0.15f), (SkillStat.DamageTaken, 0.10f)),
            });

            // 방어 (왼쪽)
            AddBranch(tree, "def", SkillBranch.Defense, p => new Vector2(-p.x, p.y), iron, new Dictionary<string, Spec>
            {
                ["1"] = S("단단한 몸", "체", (SkillStat.MaxHealth, 15f)),
                ["2"] = S("두꺼운 가죽", "방", (SkillStat.DamageTaken, -0.04f)),
                ["3u"] = S("버티는 팔", "가", (SkillStat.GuardStamina, 0.10f)),
                ["4u"] = S("방패 숙련", "가", (SkillStat.GuardStamina, 0.10f)),
                ["K1"] = S("철벽", "벽", (SkillStat.GuardStamina, 0.20f), (SkillStat.DamageTaken, -0.05f)),
                ["3l"] = S("강인한 심장", "체", (SkillStat.MaxHealth, 20f)),
                ["4l"] = S("굳센 뼈대", "체", (SkillStat.MaxHealth, 20f)),
                ["5l"] = S("비늘 같은 피부", "방", (SkillStat.DamageTaken, -0.04f)),
                ["6"] = S("흔들림 없는 발", "방", (SkillStat.DamageTaken, -0.05f)),
                ["K2"] = S("불굴", "불", (SkillStat.MaxHealth, 50f), (SkillStat.DamageTaken, -0.08f)),
            });

            // 생존 (위)
            AddBranch(tree, "sur", SkillBranch.Survival, p => new Vector2(p.y, p.x), cinder, new Dictionary<string, Spec>
            {
                ["1"] = S("깊은 호흡", "지", (SkillStat.MaxStamina, 10f)),
                ["2"] = S("빠른 회복", "기", (SkillStat.StaminaRegen, 0.08f)),
                ["3u"] = S("가벼운 발", "회", (SkillStat.DodgeInvulnerability, 0.03f)),
                ["4u"] = S("바람 타기", "회", (SkillStat.DodgeInvulnerability, 0.03f)),
                ["K1"] = S("그림자 걸음", "영", (SkillStat.DodgeInvulnerability, 0.06f), (SkillStat.StaminaRegen, 0.10f)),
                ["3l"] = S("약초 지식", "병", (SkillStat.FlaskHeal, 0.04f)),
                ["4l"] = S("진한 약", "병", (SkillStat.FlaskHeal, 0.04f)),
                ["5l"] = S("지구력", "지", (SkillStat.MaxStamina, 15f)),
                ["6"] = S("멈추지 않는 숨", "기", (SkillStat.StaminaRegen, 0.10f)),
                ["K2"] = S("끈질긴 생명", "생", (SkillStat.FlaskHeal, 0.10f), (SkillStat.MaxStamina, 20f)),
            });

            // 사냥 (아래)
            AddBranch(tree, "hun", SkillBranch.Hunt, p => new Vector2(p.y, -p.x), cinder, new Dictionary<string, Spec>
            {
                ["1"] = S("혼 거두기", "혼", (SkillStat.SoulGain, 0.05f)),
                ["2"] = S("혼의 갈증", "혼", (SkillStat.SoulGain, 0.05f)),
                ["3u"] = S("꼼꼼한 손질", "재", (SkillStat.MaterialBonus, 0.10f)),
                ["4u"] = S("갈무리 요령", "재", (SkillStat.MaterialBonus, 0.10f)),
                ["K1"] = S("갈무리 명인", "명", (SkillStat.MaterialBonus, 0.20f), (SkillStat.RareSoulChance, 0.05f)),
                ["3l"] = S("희미한 기척", "희", (SkillStat.RareSoulChance, 0.03f)),
                ["4l"] = S("혼의 메아리", "혼", (SkillStat.SoulGain, 0.08f)),
                ["5l"] = S("짙은 기척", "희", (SkillStat.RareSoulChance, 0.03f)),
                ["6"] = S("혼의 포식", "혼", (SkillStat.SoulGain, 0.08f)),
                ["K2"] = S("사냥꾼의 집념", "집", (SkillStat.ExtraFaints, 1f), (SkillStat.SoulGain, 0.10f)),
            });
        }

        static void AddBranch(SkillTreeData tree, string prefix, SkillBranch branch, System.Func<Vector2, Vector2> place,
                              ItemData rare, Dictionary<string, Spec> specs)
        {
            string Id(string key) => key == "root" ? "root" : $"{prefix}_{key}";
            var made = new Dictionary<string, SkillNode>();
            foreach (var (key, pos) in Shape)
            {
                var spec = specs[key];
                bool keystone = key.StartsWith("K");
                var node = new SkillNode
                {
                    id = Id(key),
                    title = spec.title,
                    glyph = spec.glyph,
                    branch = branch,
                    position = place(pos),
                    keystone = keystone,
                    effects = new List<SkillEffect>(spec.effects),
                    rareCost = keystone ? rare : null,
                    rareAmount = key == "K2" ? 2 : keystone ? 1 : 0,
                };
                made[key] = node;
                tree.nodes.Add(node);
            }
            foreach (var (from, to) in Links)
            {
                // 링크는 바깥쪽 노드에 적어 둔다 (시작 노드 쪽은 건드리지 않음)
                made[to].links.Add(Id(from));
            }
        }
    }
}

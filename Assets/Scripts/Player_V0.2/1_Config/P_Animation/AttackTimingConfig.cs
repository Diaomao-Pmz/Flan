using System.Collections.Generic;
using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>出招位移从哪个阶段的开头起步</summary>
    public enum ThrustStartPhase
    {
        /// <summary>前摇开始（出招那一刻）</summary>
        Windup,
        /// <summary>判定段开始</summary>
        Active,
        /// <summary>后摇开始</summary>
        Recovery,
    }

    /// <summary>
    /// 【节拍总表】所有招式的连段窗口、三段时长（前摇 / 攻击 / 后摇）、动画与表现、蓄力位移集中在这一张表里调。
    ///
    /// - 平a 6 招、蓄力 6 招：位置固定，按名字自动绑定到同名的招式资产（ComboNode）。
    /// - 双蓄 5 个槽：还没定，把想复用的招式拖进选框即可。双蓄是连段终点，所以没有连段窗口。
    /// - 表里没有的招式：没有动画（也就打不出判定），三段按 -1，连段窗口 0。运行时 Console 会报错提醒。
    ///
    /// 用法：右键 Create → Flandre → Player → 节拍总表 建一张，
    ///       拖到角色身上 PlayerState 的 Attack Timing Config 栏位。
    /// ComboNode 上的连段窗口、三段时长、动画与表现（动画剪辑 / 出招位移 / 锁移动）现在都是来这里查的。
    /// </summary>
    [CreateAssetMenu(fileName = "AttackTimingConfig", menuName = "Flandre/Player/节拍总表 (Attack Timing Config)")]
    public class AttackTimingConfig : ScriptableObject
    {
        // ==========================================================
        // 条目
        // ==========================================================

        /// <summary>三段时长（所有条目共有）</summary>
        [System.Serializable]
        public class TimingEntry
        {
            [Tooltip("用这一行参数的招式")]
            public ComboNode node;

            [Tooltip(
                "前摇时长：出招到出刀/出弹。\n" +
                "-1 = 用动画里第一个 TriggerAttackHitbox / SpawnProjectile 事件的时间。\n" +
                "填了数字后，动画的前摇这一段会自动调速，正好在这个时间里播完；出刀 / 出弹 / 特效跟着对齐。")]
            public float windupOverride = -1f;

            [Tooltip(
                "攻击时长：出刀/出弹之后，判定持续多久。期间不能移动，到点判定和连射一律收掉。\n" +
                "-1 = 到动画事件 MarkAttackEnd 为止；动画里没打这个事件时，按判定窗口 / 连射时长推算（推算值会让调速不准，建议打上 MarkAttackEnd）。\n" +
                "填了数字后，动画的攻击这一段会自动调速，正好在这个时间里播完。\n" +
                "注意：判定框自己的 Hitbox Duration、连射间隔是实际秒数，不跟着调速；" +
                "Hitbox Duration 比这里短的话，判定会先结束，但「不能移动」仍持续到本段结束。")]
            public float activeOverride = -1f;

            [Tooltip(
                "后摇时长：攻击段结束之后还要多久这一招才算完。\n" +
                "-1 = MarkAttackEnd 到动画播完的剩余时间。\n" +
                "填了数字后，动画的后摇这一段会自动调速，正好在这个时间里播完。\n" +
                "后摇里可以移动 / 换手 / 冲刺；但同一只手要等后摇走完才能再出招。")]
            public float recoveryOverride = -1f;

            // ---------------- 动画与表现 ----------------

            [Tooltip("直接把动画文件 (Anim Clip) 拖进来。请确保 Animator 里的 State 名与该动画文件名一致")]
            public AnimationClip attackClip;

            [Tooltip(
                "出招位移的【距离】（格）。在判定段开始的那一刻启动，匀速走完；中途被打断就停在半路。\n" +
                "  X 正数 = 朝角色正面前进（A 系列用）\n" +
                "  X 负数 = 朝背面后退（B 系列用）\n" +
                "  Y 非 0 = 同时上下移动（跳斩、下沉斩之类）\n\n" +
                "X 会自动乘以朝向，所以填「前进多少」即可，不用管角色朝左朝右。撞墙会停下。")]
            public Vector2 forwardThrust = Vector2.zero;

            [Tooltip(
                "位移从哪个阶段的开头起步：\n" +
                "  Windup = 出招那一刻（前摇开始）\n" +
                "  Active = 判定段开始（默认）\n" +
                "  Recovery = 后摇开始")]
            public ThrustStartPhase thrustStart = ThrustStartPhase.Active;

            [Tooltip(
                "位移用多久走完（秒）。\n" +
                "-1 = 和上面选的阶段一样长 —— 阶段开始时起步，阶段结束时正好走到。")]
            public float thrustDuration = -1f;

            [Tooltip("空中出招时是否也产生位移。取消勾选则只有地面招式会位移")]
            public bool applyThrustInAir = true;

            [Tooltip(
                "出招期间锁死移动（默认开启，符合「攻击时不能走动」的基础设定）。\n\n" +
                "位移本身照常生效。取消勾选则保留进入攻击时的残留速度，做「边走边砍」的招式时才需要。")]
            public bool lockMovementDuringAttack = true;

            // ---------------- 蓄力位移（Aim Dash）----------------

            [Tooltip(
                "本招是否带瞄准方向的位移（AA1 / BB1 这类）。\n" +
                "勾上后用下面的蓄力位移参数，上面的 Forward Thrust 不再生效。")]
            public bool useAimDash = false;

            [Tooltip(
                "位移方向的自由度（方向来源由武器决定：远程跟鼠标，近战跟方向键）。\n" +
                "  Free = 任意角度\n  EightWay = 八向吸附\n  FourWay = 上下左右\n  FacingOnly = 只朝面朝方向")]
            public AimDashMode aimDashMode = AimDashMode.FourWay;

            [Tooltip(
                "位移距离（格）。判定段开始时起步，匀速走完；中途被打断就停在半路。\n" +
                "【填负数 = 朝瞄准的反方向弹开】用来做枪的后坐力，此时角色仍然面朝鼠标。")]
            public float aimDashDistance = 2f;

            [Tooltip("位移用多久走完（秒）。-1 = 和攻击段一样长")]
            public float aimDashDuration = 0.15f;

            /// <summary>动画与表现是从哪个招式身上搬过来的（换了招式会重新搬一次）</summary>
            [HideInInspector] public ComboNode migratedFrom;

            /// <summary>搬到第几版了：1 = 动画与表现，2 = 加上蓄力位移</summary>
            [HideInInspector] public int migratedVersion;

            /// <summary>把招式身上旧的蓄力位移抄过来（动量加成已删除，距离 = 原来的低保距离）</summary>
            public void CopyLegacyAimDash(ComboNode n)
            {
                useAimDash = n.LegacyUseAimDash;
                aimDashMode = n.LegacyAimDashMode;
                aimDashDistance = n.LegacyAimDashBaseDistance;
                aimDashDuration = n.LegacyAimDashDuration;
                migratedVersion = 2;
            }

            /// <summary>把招式身上留着的旧"动画与表现"抄过来。旧位移是速度（线性衰减），换算成距离 = 速度 × 时长 ÷ 2</summary>
            public void CopyLegacyFrom(ComboNode n)
            {
                attackClip = n.LegacyAttackClip;

                float oldDuration = n.LegacyThrustDuration > 0f ? n.LegacyThrustDuration : 0.12f;
                Vector2 d = n.LegacyForwardThrust * (oldDuration * 0.5f);
                forwardThrust = new Vector2(Mathf.Round(d.x * 1000f) / 1000f, Mathf.Round(d.y * 1000f) / 1000f);

                thrustDuration = -1f;
                applyThrustInAir = n.LegacyApplyThrustInAir;
                lockMovementDuringAttack = n.LegacyLockMovement;
                CopyLegacyAimDash(n);
                migratedFrom = n;
            }
        }

        /// <summary>已经定下来的招式：名字固定，带连段窗口</summary>
        [System.Serializable]
        public class FixedEntry : TimingEntry
        {
            [Tooltip("招式名（固定，按这个名字自动绑定同名的招式资产）")]
            public string slotName = "";

            [Tooltip(
                "本招【总时长走完之后】，玩家还有多久可以接下一招。超时则段数归零。\n" +
                "总时长 = 前摇 + 攻击 + 后摇（见下方「三段时长」）。")]
            public float comboWindow = 0.5f;
        }

        /// <summary>双蓄：招式自己选，没有连段窗口（双蓄是连段终点）</summary>
        [System.Serializable]
        public class DualEntry : TimingEntry { }

        [SerializeField] private List<FixedEntry> normalAttacks = new List<FixedEntry>();
        [SerializeField] private List<FixedEntry> chargeAttacks = new List<FixedEntry>();
        [SerializeField] private List<DualEntry> dualAttacks = new List<DualEntry>();

        public IReadOnlyList<FixedEntry> NormalAttacks => normalAttacks;
        public IReadOnlyList<FixedEntry> ChargeAttacks => chargeAttacks;
        public IReadOnlyList<DualEntry> DualAttacks => dualAttacks;

        // ==========================================================
        // 固定槽位 + 初始值（建表时 = 当时各招式资产上填的值）
        // ==========================================================

        private struct Preset
        {
            public string name;
            public float window, windup, active, recovery;
            public Preset(string n, float w, float wu, float a, float r) { name = n; window = w; windup = wu; active = a; recovery = r; }
        }

        private static readonly Preset[] NormalPresets =
        {
            new Preset("A1", 0.3f, 0.2f, -1f, 0.2f),
            new Preset("A2", 0.7f, 0.2f, -1f, 0.2f),
            new Preset("A3", 0.5f, 0.2f, -1f, 0.2f),
            new Preset("B1", 0.1f, -1f, -1f, -1f),
            new Preset("B2", 0.7f, -1f, -1f, -1f),
            new Preset("B3", 0.5f, -1f, -1f, -1f),
        };

        private static readonly Preset[] ChargePresets =
        {
            new Preset("AA1", 0.7f, -1f, -1f, -1f),
            new Preset("AA2", 0.5f, -1f, -1f, -1f),
            new Preset("AA3", 0.7f, -1f, -1f, -1f),
            new Preset("BB1", 0.7f, -1f, -1f, -1f),
            new Preset("BB2", 0.5f, -1f, -1f, -1f),
            new Preset("BB3", 0.7f, -1f, -1f, -1f),
        };

        public const int DualSlotCount = 5;

        /// <summary>保证固定槽位齐全、顺序正确（缺的按初始值补上，已有的保留你填的数）</summary>
        private void EnsureSlots()
        {
            EnsureFixed(normalAttacks, NormalPresets);
            EnsureFixed(chargeAttacks, ChargePresets);
            while (dualAttacks.Count < DualSlotCount) dualAttacks.Add(new DualEntry());
        }

        private static void EnsureFixed(List<FixedEntry> list, Preset[] presets)
        {
            var ordered = new List<FixedEntry>(presets.Length);
            for (int i = 0; i < presets.Length; i++)
            {
                Preset p = presets[i];
                FixedEntry e = list.Find(x => x != null && x.slotName == p.name);
                if (e == null)
                {
                    e = new FixedEntry
                    {
                        slotName = p.name,
                        comboWindow = p.window,
                        windupOverride = p.windup,
                        activeOverride = p.active,
                        recoveryOverride = p.recovery,
                    };
                }
                ordered.Add(e);
            }
            list.Clear();
            list.AddRange(ordered);
        }

        // ==========================================================
        // 查表
        // ==========================================================

        private static AttackTimingConfig active;
#if UNITY_EDITOR
        private static bool editorSearched;   // 编辑器里只全工程搜一次，免得没建表时每次出招都搜
#endif

        private readonly Dictionary<ComboNode, TimingEntry> lookup = new Dictionary<ComboNode, TimingEntry>();
        private bool lookupDirty = true;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatic()
        {
            active = null;
            warnedMissing.Clear();
#if UNITY_EDITOR
            editorSearched = false;
#endif
        }

        /// <summary>当前生效的表。由 PlayerState 设置；编辑器里没进游戏时自动找工程里的第一张</summary>
        public static AttackTimingConfig Active
        {
            get
            {
#if UNITY_EDITOR
                if (active == null && !editorSearched)
                {
                    editorSearched = true;
                    string[] guids = UnityEditor.AssetDatabase.FindAssets("t:" + nameof(AttackTimingConfig));
                    if (guids.Length > 0)
                        active = UnityEditor.AssetDatabase.LoadAssetAtPath<AttackTimingConfig>(
                            UnityEditor.AssetDatabase.GUIDToAssetPath(guids[0]));
                }
#endif
                return active;
            }
        }

        public static void SetActive(AttackTimingConfig config)
        {
            if (config == null) return;
            active = config;
            config.lookupDirty = true;
        }

        private void BuildLookup()
        {
            lookup.Clear();
            // 平a、蓄力在前：同一个招式同时出现在双蓄槽里时，以前面的为准
            AddAll(normalAttacks);
            AddAll(chargeAttacks);
            AddAll(dualAttacks);
            lookupDirty = false;
        }

        private void AddAll<T>(List<T> list) where T : TimingEntry
        {
            for (int i = 0; i < list.Count; i++)
            {
                T e = list[i];
                if (e != null && e.node != null && !lookup.ContainsKey(e.node)) lookup.Add(e.node, e);
            }
        }

        /// <summary>这个招式在表里的那一行；不在表里返回 null</summary>
        public static TimingEntry Find(ComboNode node)
        {
            AttackTimingConfig cfg = Active;
            if (cfg == null || node == null) return null;
            if (cfg.lookupDirty) cfg.BuildLookup();
            return cfg.lookup.TryGetValue(node, out TimingEntry e) ? e : null;
        }

        /// <summary>连段窗口。只有平a / 蓄力有；双蓄和表里没有的招式 = 0</summary>
        public static float GetComboWindow(ComboNode node) => Find(node) is FixedEntry f ? f.comboWindow : 0f;

        public static float GetWindup(ComboNode node) { TimingEntry e = Find(node); return e != null ? e.windupOverride : -1f; }

        // ---- 动画与表现 ----

        private static readonly HashSet<ComboNode> warnedMissing = new HashSet<ComboNode>();

        /// <summary>动画剪辑。表里没有这个招式 = 没有动画（运行时报错一次）</summary>
        public static AnimationClip GetClip(ComboNode node)
        {
            TimingEntry e = Find(node);
            if (e != null) return e.attackClip;

            if (Application.isPlaying && node != null && warnedMissing.Add(node))
                Debug.LogError($"[节拍总表] 招式「{node.name}」不在节拍总表里：它没有动画，也打不出判定 / 子弹 / 特效。" +
                               "请把它加进表（双蓄槽可以直接拖）。", node);
            return null;
        }

        /// <summary>只在表已经加载过时查动画剪辑：不全工程搜表、不报错（给资产导入期间的检查用）</summary>
        public static AnimationClip PeekClip(ComboNode node)
        {
            if (active == null || node == null) return null;
            if (active.lookupDirty) active.BuildLookup();
            return active.lookup.TryGetValue(node, out TimingEntry e) ? e.attackClip : null;
        }

        public static Vector2 GetThrust(ComboNode node) { TimingEntry e = Find(node); return e != null ? e.forwardThrust : Vector2.zero; }
        public static ThrustStartPhase GetThrustStart(ComboNode node) { TimingEntry e = Find(node); return e != null ? e.thrustStart : ThrustStartPhase.Active; }
        public static float GetThrustDuration(ComboNode node) { TimingEntry e = Find(node); return e != null ? e.thrustDuration : -1f; }
        public static bool GetApplyThrustInAir(ComboNode node) { TimingEntry e = Find(node); return e == null || e.applyThrustInAir; }
        public static bool GetLockMovement(ComboNode node) { TimingEntry e = Find(node); return e == null || e.lockMovementDuringAttack; }

        // ---- 蓄力位移 ----
        public static bool GetUseAimDash(ComboNode node) { TimingEntry e = Find(node); return e != null && e.useAimDash; }
        public static AimDashMode GetAimDashMode(ComboNode node) { TimingEntry e = Find(node); return e != null ? e.aimDashMode : AimDashMode.FourWay; }
        public static float GetAimDashDistance(ComboNode node) { TimingEntry e = Find(node); return e != null ? e.aimDashDistance : 0f; }
        public static float GetAimDashDuration(ComboNode node) { TimingEntry e = Find(node); return e != null ? e.aimDashDuration : 0.15f; }
        public static float GetActive(ComboNode node) { TimingEntry e = Find(node); return e != null ? e.activeOverride : -1f; }
        public static float GetRecovery(ComboNode node) { TimingEntry e = Find(node); return e != null ? e.recoveryOverride : -1f; }

        /// <summary>这个招式在不在表里</summary>
        public static bool Contains(ComboNode node) => Find(node) != null;

        // ==========================================================
        // 编辑器：建表 / 改表时自动补槽位、按名字绑定招式
        // ==========================================================

        private void Reset()
        {
            normalAttacks.Clear();
            chargeAttacks.Clear();
            dualAttacks.Clear();
            EnsureSlots();
            AutoBind();
            SyncFromNodes();
            lookupDirty = true;
        }

        private void OnValidate()
        {
            EnsureSlots();
            SyncFromNodes();      // 双蓄槽拖进新招式 → 把它身上旧的动画与表现搬过来
            lookupDirty = true;   // 换了招式也能马上查到
            if (active == null) active = this;   // 刚建的表（还没拖给 PlayerState）编辑器里也能马上生效
        }

        /// <summary>
        /// 固定槽位还没绑到招式的，按名字找同名的 ComboNode 资产。
        /// 建表时、以及在 Inspector 里打开这张表时调用（不放在 OnValidate 里：资产导入期间不宜查 AssetDatabase）。
        /// </summary>
        /// <returns>这次有没有新绑上的</returns>
        public bool AutoBind()
        {
            bool changed = false;
#if UNITY_EDITOR
            changed |= BindByName(normalAttacks);
            changed |= BindByName(chargeAttacks);
            if (changed) lookupDirty = true;
#endif
            changed |= SyncFromNodes();
            return changed;
        }

        /// <summary>
        /// 每一行第一次绑上某个招式（或换成另一个招式）时，把那个招式身上旧的"动画与表现"抄过来一次。
        /// 之后你在表里改的数不会再被覆盖。
        /// </summary>
        /// <returns>这次有没有抄</returns>
        public bool SyncFromNodes()
        {
            bool changed = false;
            changed |= SyncList(normalAttacks);
            changed |= SyncList(chargeAttacks);
            changed |= SyncList(dualAttacks);
            return changed;
        }

        private static bool SyncList<T>(List<T> list) where T : TimingEntry
        {
            bool changed = false;
            for (int i = 0; i < list.Count; i++)
            {
                T e = list[i];
                if (e == null || e.node == null) continue;

                if (e.migratedFrom != e.node)
                {
                    e.CopyLegacyFrom(e.node);        // 新绑上的招式：全部搬一次
                    changed = true;
                }
                else if (e.migratedVersion < 2)
                {
                    e.CopyLegacyAimDash(e.node);     // 之前已经搬过动画与表现：只补搬蓄力位移，不动你改过的数
                    changed = true;
                }
            }
            return changed;
        }

#if UNITY_EDITOR
        private static bool BindByName(List<FixedEntry> list)
        {
            bool changed = false;
            for (int i = 0; i < list.Count; i++)
            {
                FixedEntry e = list[i];
                if (e == null || e.node != null || string.IsNullOrEmpty(e.slotName)) continue;

                foreach (string guid in UnityEditor.AssetDatabase.FindAssets(e.slotName + " t:ComboNode"))
                {
                    string path = UnityEditor.AssetDatabase.GUIDToAssetPath(guid);
                    if (System.IO.Path.GetFileNameWithoutExtension(path) != e.slotName) continue;   // DOWN_AA1 不能当成 AA1
                    e.node = UnityEditor.AssetDatabase.LoadAssetAtPath<ComboNode>(path);
                    changed = true;
                    break;
                }
            }
            return changed;
        }
#endif
    }
}

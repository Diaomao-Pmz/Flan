using System.Collections.Generic;
using UnityEngine;

namespace Flandre.CombatSystem
{
    /// <summary>特效表里一条的用途</summary>
    public enum EffectUsage
    {
        /// <summary>招式特效：动画里的 PlayEffect / PlayEffectByKey 时间戳触发，按存活模式自己消失</summary>
        Attack,
        /// <summary>蓄力光效：蓄到某一级时挂上，升级、松手、受击、换武器时收回</summary>
        Charge,
    }

    /// <summary>
    /// 【特效管理器】场景里放一个空物体挂上本组件，所有角色特效集中在这张表里配。
    ///
    /// 每一条：一个特效预制体 + 参数（给哪些招式 / 哪把武器的第几级蓄力用、活多久、位置 / 缩放 / 旋转）。
    /// - 招式特效：招式出手时，时间表在 PlayEffect 事件那一刻来这里查表播放。
    /// - 蓄力光效：蓄力等级变化时，PlayerEffectSpawner 来这里查表挂上 / 收回。
    ///
    /// 预制体由本管理器自己按种类分池、借出、回收，不需要在 ObjectPoolManager 里登记。
    /// 预制体上如果还残留旧的 PooledEffect 组件，会被自动关掉（它的自动回收会和这里抢）。
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class EffectManager : MonoBehaviour
    {
        public static EffectManager Instance { get; private set; }

        [System.Serializable]
        public class EffectSettings
        {
            [Tooltip("招式特效 = 动画里的 PlayEffect 时间戳触发\n蓄力光效 = 蓄到某一级时挂上，松手 / 升级 / 受击时收回")]
            public EffectUsage usage = EffectUsage.Attack;

            // ---------------- 招式特效 ----------------

            [Tooltip("哪些招式用这个特效。把招式节点（ComboNode）拖进来，可以填多个")]
            public List<ComboNode> nodes = new List<ComboNode>();

            [Tooltip("（可选）名字。动画事件 PlayEffectByKey 的字符串参数填这个名字，就能在一招里额外播这个特效")]
            public string key = "";

            [Tooltip(
                "特效什么时候消失：\n" +
                "Follow Active = 从 PlayEffect 事件到判定段结束\n" +
                "Follow Attack = 到整招（含后摇）结束\n" +
                "Fixed = 播完特效自己的动画一遍（时长自动读预制体的动画，不用填）\n" +
                "Until Marker = 到动画里配对的 EffectEnd 事件为止（可以活过后摇）。没打 EffectEnd 时播完一遍\n\n" +
                "前两种在招式被打断时特效立刻消失；Fixed 和 Until Marker 不受打断影响。")]
            public EffectLifetimeMode lifetimeMode = EffectLifetimeMode.FollowActive;

            [Tooltip(
                "勾选 = 调整动画播放速度，让动画在存活时间里正好播完一遍（Fixed 本来就是播完一遍，不需要这个）\n" +
                "不勾 = 原速播放，时间到了就收（动画长了会被截断，短了会循环或停在最后一帧，看动画有没有勾 Loop Time）")]
            public bool fitDuration = true;

            [Tooltip("勾选 = 位置用招式判定框的偏移；不勾 = 用下面的 Offset")]
            public bool useHitboxOffset = false;

            // ---------------- 蓄力光效 ----------------

            [Tooltip("哪些武器用这个光效。把武器资产（WeaponMoveSet，如 MainWeapon / SubWeapon）拖进来，可以填多个")]
            public List<WeaponMoveSet> weapons = new List<WeaponMoveSet>();

            [Tooltip("蓄到第几级时挂这个光效")]
            [Min(1)]
            public int chargeLevel = 1;

            // ---------------- 通用 ----------------

            [Tooltip("相对角色的位置（角色面朝右时的值，转身自动镜像）")]
            public Vector2 offset = Vector2.zero;

            [Tooltip("缩放。(1,1) = 原始大小。不要填 0，会看不见；想左右翻转也不要填负数，转身已经自动处理")]
            public Vector2 scale = Vector2.one;

            [Tooltip("旋转角度（度）")]
            public float rotation = 0f;
        }

        [System.Serializable]
        public class EffectEntry
        {
            [Tooltip("特效预制体（就是原来对象池里登记的那些）。动画、粒子、排序层都用预制体自己的")]
            public GameObject prefab;

            public EffectSettings settings = new EffectSettings();
        }

        [Header("--- 特效表（每个元素：预制体 + 参数）---")]
        public List<EffectEntry> effects = new List<EffectEntry>();

        [Header("默认值")]
        [Tooltip("要「播完一遍」、但特效预制体里既没有动画也没有粒子时，活多久")]
        [Min(0.01f)]
        public float defaultLifetime = 0.4f;

        [Header("池")]
        [Tooltip("开局给每种特效预先准备几个。不够用时会自动再建")]
        [Min(0)]
        public int prewarmCount = 2;

        [Header("Debug")]
        public bool verboseLog = false;

        /// <summary>跟随时段短于这个值就不跟了，退回固定时长</summary>
        private const float MinBoundLifetime = 0.05f;

        /// <summary>Until Marker 模式却没配对到 EffectEnd 的条目，只警告一次</summary>
        private readonly HashSet<EffectEntry> warnedNoMarker = new HashSet<EffectEntry>();

        /// <summary>按预制体分开的空闲池</summary>
        private readonly Dictionary<GameObject, Stack<ManagedEffect>> free = new Dictionary<GameObject, Stack<ManagedEffect>>();
        private readonly List<ManagedEffect> active = new List<ManagedEffect>();

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                PlayerLog.Warn("[特效] 场景里有多个 EffectManager，只有第一个生效。", this);
                return;
            }
            Instance = this;

            // 每种预制体预热几个（同一个预制体出现在多条里只预热一次）
            for (int i = 0; i < effects.Count; i++)
            {
                GameObject prefab = effects[i] != null ? effects[i].prefab : null;
                if (prefab == null || free.ContainsKey(prefab)) continue;

                Stack<ManagedEffect> stack = GetFreeStack(prefab);
                for (int n = 0; n < prewarmCount; n++) stack.Push(CreateInstance(prefab));
            }
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private Stack<ManagedEffect> GetFreeStack(GameObject prefab)
        {
            if (!free.TryGetValue(prefab, out Stack<ManagedEffect> stack))
            {
                stack = new Stack<ManagedEffect>();
                free[prefab] = stack;
            }
            return stack;
        }

        private ManagedEffect CreateInstance(GameObject prefab)
        {
            GameObject go = Instantiate(prefab, transform);
            go.name = prefab.name;

            // 预制体上残留的旧 PooledEffect 会自己计时、到点往 ObjectPoolManager 还 —— 关掉它。
            // 按名字找，不引用类型：PooledEffect.cs 删掉之后这里照样能编译。
            MonoBehaviour[] behaviours = go.GetComponentsInChildren<MonoBehaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                MonoBehaviour b = behaviours[i];
                if (b != null && b.GetType().Name == "PooledEffect") b.enabled = false;
            }

            if (!go.TryGetComponent(out ManagedEffect fx)) fx = go.AddComponent<ManagedEffect>();
            fx.Init(prefab);

            go.SetActive(false);
            return fx;
        }

        private ManagedEffect Borrow(GameObject prefab)
        {
            Stack<ManagedEffect> stack = GetFreeStack(prefab);
            while (stack.Count > 0)
            {
                ManagedEffect fx = stack.Pop();
                if (fx != null) return fx;   // 池里的物体被外部销毁了就跳过
            }
            return CreateInstance(prefab);
        }

        // ==========================================================
        // 查表
        // ==========================================================

        /// <summary>这个招式在表里对应的条目（只找招式特效）</summary>
        public EffectEntry FindByNode(ComboNode node)
        {
            if (node == null) return null;
            for (int i = 0; i < effects.Count; i++)
            {
                EffectEntry e = effects[i];
                if (e == null || e.settings == null || e.settings.usage != EffectUsage.Attack) continue;
                if (e.settings.nodes != null && e.settings.nodes.Contains(node)) return e;
            }
            return null;
        }

        private EffectEntry FindByKey(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            for (int i = 0; i < effects.Count; i++)
            {
                EffectEntry e = effects[i];
                if (e == null || e.settings == null || e.settings.usage != EffectUsage.Attack) continue;
                if (e.settings.key == key) return e;
            }
            return null;
        }

        /// <summary>这把武器第 level 级蓄力在表里对应的条目</summary>
        public EffectEntry FindCharge(WeaponMoveSet weapon, int level)
        {
            if (weapon == null) return null;
            for (int i = 0; i < effects.Count; i++)
            {
                EffectEntry e = effects[i];
                if (e == null || e.settings == null || e.settings.usage != EffectUsage.Charge) continue;
                if (e.settings.chargeLevel == level && e.settings.weapons != null && e.settings.weapons.Contains(weapon))
                    return e;
            }
            return null;
        }

        /// <summary>这个招式在表里有没有配特效</summary>
        public bool HasEffectFor(ComboNode node) => FindByNode(node) != null;

        /// <summary>这把武器第 level 级蓄力在表里有没有配光效</summary>
        public bool HasChargeEffectFor(WeaponMoveSet weapon, int level) => FindCharge(weapon, level) != null;

        // ==========================================================
        // 播放：招式特效
        // ==========================================================

        /// <summary>播放这个招式配的特效。表里没有这个招式（或没拖预制体）时返回 null</summary>
        /// <param name="root">特效挂在谁下面（跟着它移动和转身）</param>
        /// <param name="activeRemain">距离判定段结束还有多久（没有就传 -1）</param>
        /// <param name="attackRemain">距离整招结束还有多久（没有就传 -1）</param>
        /// <param name="markerLife">距离配对的 EffectEnd 还有多久（没有就传 -1）</param>
        public GameObject Play(ComboNode node, Transform root, float activeRemain = -1f, float attackRemain = -1f,
            float markerLife = -1f)
        {
            EffectEntry entry = FindByNode(node);
            return entry != null ? PlayAttackEntry(entry, node, root, activeRemain, attackRemain, markerLife) : null;
        }

        /// <summary>按名字播放（动画事件 PlayEffectByKey）。表里没有这个名字时返回 null</summary>
        public GameObject PlayByKey(string key, ComboNode node, Transform root, float activeRemain = -1f, float attackRemain = -1f,
            float markerLife = -1f)
        {
            EffectEntry entry = FindByKey(key);
            return entry != null ? PlayAttackEntry(entry, node, root, activeRemain, attackRemain, markerLife) : null;
        }

        private GameObject PlayAttackEntry(EffectEntry entry, ComboNode node, Transform root,
            float activeRemain, float attackRemain, float markerLife)
        {
            if (entry.prefab == null) return null;

            EffectSettings s = entry.settings;

            // ---- 活多久：默认播完特效自己的动画一遍（Fixed 就是这个；其他模式没有时段可跟时也退回这个）----
            float life = ManagedEffect.PlayOnce;
            if (s.lifetimeMode == EffectLifetimeMode.UntilMarker)
            {
                if (markerLife >= MinBoundLifetime)
                {
                    life = markerLife;
                }
                else if (warnedNoMarker.Add(entry))
                {
                    string nodeName = node != null ? node.nodeName : "?";
                    PlayerLog.Warn($"[特效] 特效「{entry.prefab.name}」的存活模式是 Until Marker，但招式「{nodeName}」的动画里" +
                                   "没有和它配对的 EffectEnd 事件，这次按特效动画播完一遍。", this);
                }
            }
            else if (s.lifetimeMode != EffectLifetimeMode.Fixed)
            {
                float remain = s.lifetimeMode == EffectLifetimeMode.FollowActive ? activeRemain : attackRemain;
                if (remain >= MinBoundLifetime) life = remain;
            }

            bool survives = s.lifetimeMode == EffectLifetimeMode.Fixed || s.lifetimeMode == EffectLifetimeMode.UntilMarker;
            Vector2 offset = s.useHitboxOffset && node != null ? node.GetFirstHitboxOffset() : s.offset;

            return Spawn(entry, root, offset, life, s.fitDuration, survives);
        }

        // ==========================================================
        // 播放：蓄力光效
        // ==========================================================

        /// <summary>
        /// 挂上这把武器第 level 级的蓄力光效。常驻，直到调用 Stop 收回。
        /// 表里没配（或没拖预制体）时返回 null。
        /// </summary>
        public GameObject PlayCharge(WeaponMoveSet weapon, int level, Transform root)
        {
            EffectEntry entry = FindCharge(weapon, level);
            if (entry == null || entry.prefab == null) return null;

            // life = 0 → 常驻；蓄力光效本来就不会被招式打断，由调用方负责收
            return Spawn(entry, root, entry.settings.offset, 0f, false, true);
        }

        // ==========================================================
        // 生成 / 回收
        // ==========================================================

        /// <param name="life">存活时长；0 = 常驻；ManagedEffect.PlayOnce = 播完特效自己的动画一遍</param>
        private GameObject Spawn(EffectEntry entry, Transform root, Vector2 offset, float life, bool fit, bool survivesInterrupt)
        {
            EffectSettings s = entry.settings;
            ManagedEffect fx = Borrow(entry.prefab);

            Transform t = fx.transform;
            t.SetParent(root != null ? root : transform, false);
            t.localPosition = offset;
            t.localRotation = Quaternion.Euler(0f, 0f, s.rotation);
            // 角色转身用的是旋转，子物体已经跟着翻了，所以这里 X 只取正值
            t.localScale = new Vector3(Mathf.Abs(s.scale.x), s.scale.y, 1f);

            // 先激活再 Begin：Animator 要在激活状态下才能倒带
            fx.gameObject.SetActive(true);
            fx.Begin(life, fit, survivesInterrupt, defaultLifetime);
            active.Add(fx);

            if (verboseLog)
                PlayerLog.Info($"[特效] 播放 {entry.prefab.name}，" +
                               (life > 0f ? $"时长 {life:F2}s" + (fit ? "（倍速适配）" : "")
                                : life < 0f ? $"播完一遍（{fx.Lifetime:F2}s）" : "常驻"));

            return fx.gameObject;
        }

        /// <summary>提前收回一个特效。只有它还在播、且仍是 playId 那一次播放时才收</summary>
        public void Stop(ManagedEffect fx, int playId)
        {
            if (fx == null || !fx.IsPlaying || fx.PlayId != playId) return;
            Release(fx);
        }

        private void Update()
        {
            float dt = Time.deltaTime;
            for (int i = active.Count - 1; i >= 0; i--)
            {
                ManagedEffect fx = active[i];

                // 挂在角色身上时角色被销毁，特效物体也跟着没了
                if (fx == null)
                {
                    active.RemoveAt(i);
                    continue;
                }

                if (fx.Tick(dt)) Release(fx);
            }
        }

        private void Release(ManagedEffect fx)
        {
            active.Remove(fx);
            fx.End();
            fx.transform.SetParent(transform, false);
            fx.gameObject.SetActive(false);

            if (fx.SourcePrefab != null) GetFreeStack(fx.SourcePrefab).Push(fx);
            else Destroy(fx.gameObject);
        }
    }
}

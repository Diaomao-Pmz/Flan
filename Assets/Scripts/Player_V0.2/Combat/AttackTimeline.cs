using System.Collections.Generic;
using UnityEngine;

namespace Flandre.CombatSystem
{
    // ==========================================================
    // 【实验：平a/蓄力重做】攻击逻辑由「动画事件驱动」改为「时间表驱动」
    // ==========================================================

    /// <summary>一次攻击所处的阶段</summary>
    public enum AttackPhase
    {
        /// <summary>没有攻击实例</summary>
        None,
        /// <summary>前摇：首次出刀/出弹之前</summary>
        Windup,
        /// <summary>判定：判定框 / 连射持续中</summary>
        Active,
        /// <summary>后摇：判定结束之后</summary>
        Recovery,
    }

    public enum TimelineEventKind { Hitbox, Projectile, Effect, EffectByKey }

    public struct TimelineEvent
    {
        public float time;
        public TimelineEventKind kind;
        public string str;

        /// <summary>
        /// 【仅特效事件】配对的 EffectEnd 事件时间；没有配对则 -1。
        /// 配对规则：PlayEffect 配字符串参数为空的 EffectEnd，PlayEffectByKey("x") 配参数为 "x" 的 EffectEnd，
        /// 取它之后最近的、还没被别人配走的那一个。
        /// </summary>
        public float endTime;
    }

    /// <summary>
    /// 一招的三段时长，以及"动画里的时间 ↔ 实际时间"的换算。
    ///
    /// 人物动画按三段【各自的倍速】播放：
    ///   动画的前摇这一段在 windup 秒里播完，攻击段在 active 秒里，后摇段在 recovery 秒里。
    /// 动画里打的事件（出刀 / 出弹 / 特效 / EffectEnd）都走同一套换算对到实际时间上，
    /// 所以画面、判定、特效始终对齐。
    ///
    /// 比喻：动画是一段录像，ComboNode 填的三段时长是"每一段要放多久"，
    ///       这里负责算出每一段该快放还是慢放、录像上第 x 秒对应现实里第几秒。
    /// </summary>
    public struct AttackTiming
    {
        // ---- 动画里的分界点（秒，按动画原速）----
        /// <summary>第一个出刀 / 出弹事件</summary>
        public float animWindup;
        /// <summary>MarkAttackEnd（没打则推算）</summary>
        public float animActiveEnd;
        public float clipLength;

        // ---- 实际时长（秒）----
        public float windup;
        public float active;
        public float recovery;

        /// <summary>攻击段的结束点是明确给定的（MarkAttackEnd / 手填），不是推算的</summary>
        public bool activeExplicit;

        /// <summary>招式配了动画剪辑（没配的话不调速）</summary>
        public bool hasClip;

        public float Duration => windup + active + recovery;

        /// <summary>实际时间落在第几段：0 = 前摇，1 = 攻击，2 = 后摇</summary>
        public int SegmentAt(float real)
        {
            if (real < windup) return 0;
            if (real < windup + active) return 1;
            return 2;
        }

        /// <summary>
        /// 这一段动画的播放倍速 = 动画里这段的长度 ÷ 实际时长。
        /// 实际时长为 0 的段不会停留（直接跳过），返回 1；动画里这段长度为 0 时返回 0（停在这一帧等）。
        /// </summary>
        public float SpeedOf(int seg)
        {
            float animLen, realLen;
            switch (seg)
            {
                case 0: animLen = animWindup; realLen = windup; break;
                case 1: animLen = animActiveEnd - animWindup; realLen = active; break;
                default: animLen = clipLength - animActiveEnd; realLen = recovery; break;
            }
            if (realLen <= 0.0001f) return 1f;
            return Mathf.Max(0f, animLen) / realLen;
        }

        /// <summary>动画里第 t 秒 → 实际第几秒（出招那一刻算 0）</summary>
        public float ToReal(float t)
        {
            if (t <= animWindup)
                return animWindup > 0.0001f ? t / animWindup * windup : windup;
            if (t <= animActiveEnd)
                return windup + (t - animWindup) / (animActiveEnd - animWindup) * active;

            float tail = clipLength - animActiveEnd;
            float k = tail > 0.0001f ? Mathf.Clamp01((t - animActiveEnd) / tail) : 1f;
            return windup + active + k * recovery;
        }

        /// <summary>实际第几秒 → 动画里该播到第几秒</summary>
        public float ToAnim(float real)
        {
            if (real < windup)
                return windup > 0.0001f ? real / windup * animWindup : animWindup;
            real -= windup;
            if (real < active)
                return active > 0.0001f ? animWindup + real / active * (animActiveEnd - animWindup) : animActiveEnd;
            real -= active;
            float k = recovery > 0.0001f ? Mathf.Clamp01(real / recovery) : 1f;
            return animActiveEnd + k * (clipLength - animActiveEnd);
        }
    }

    /// <summary>从一个动画剪辑解析出来的时间表（按剪辑缓存）</summary>
    public class AttackTimelineData
    {
        public readonly List<TimelineEvent> events = new List<TimelineEvent>();
        public float clipLength;

        /// <summary>第一个出刀/出弹事件的时间；没有则 -1</summary>
        public float firstStrikeTime = -1f;

        /// <summary>动画事件 MarkAttackEnd（"攻击结束"时间戳）的时间；没打这个事件则 -1</summary>
        public float attackEndTime = -1f;

        private static readonly Dictionary<AnimationClip, AttackTimelineData> cache
            = new Dictionary<AnimationClip, AttackTimelineData>();

        private static readonly AttackTimelineData empty = new AttackTimelineData();

        /// <summary>解析时临时存放 EffectEnd 事件（时间 + 名字）</summary>
        private static readonly List<TimelineEvent> endScratch = new List<TimelineEvent>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearCache() => cache.Clear();

        /// <summary>
        /// 丢掉某个剪辑的缓存，下次 Get 时重新解析。
        /// 编辑器里改了动画事件之后（OnValidate、打印三段时长）用，免得读到旧的时间点。
        /// </summary>
        public static void Invalidate(AnimationClip clip)
        {
            if (clip != null) cache.Remove(clip);
        }

        public static AttackTimelineData Get(AnimationClip clip)
        {
            if (clip == null) return empty;
            if (cache.TryGetValue(clip, out AttackTimelineData data)) return data;

            data = new AttackTimelineData { clipLength = clip.length };

            AnimationEvent[] evs = clip.events;
            endScratch.Clear();
            for (int i = 0; i < evs.Length; i++)
            {
                AnimationEvent e = evs[i];

                // "特效结束"时间戳：先收集起来，最后和特效事件配对
                if (e.functionName == "EffectEnd")
                {
                    endScratch.Add(new TimelineEvent { time = e.time, str = e.stringParameter ?? "" });
                    continue;
                }

                // "攻击结束"时间戳：只记时间，不进事件表
                if (e.functionName == "MarkAttackEnd")
                {
                    if (data.attackEndTime < 0f || e.time > data.attackEndTime) data.attackEndTime = e.time;
                    continue;
                }

                TimelineEvent te = new TimelineEvent { time = e.time, endTime = -1f };

                switch (e.functionName)
                {
                    case "TriggerAttackHitbox": te.kind = TimelineEventKind.Hitbox; break;
                    case "SpawnProjectile": te.kind = TimelineEventKind.Projectile; break;
                    case "PlayEffect": te.kind = TimelineEventKind.Effect; break;
                    case "PlayEffectByKey":
                        te.kind = TimelineEventKind.EffectByKey;
                        te.str = e.stringParameter;
                        break;
                    default: continue;   // OnAttackAnimationEnd / Open(Close)ComboWindow 等仍由动画驱动
                }

                data.events.Add(te);

                bool isStrike = te.kind == TimelineEventKind.Hitbox || te.kind == TimelineEventKind.Projectile;
                if (isStrike && (data.firstStrikeTime < 0f || te.time < data.firstStrikeTime))
                    data.firstStrikeTime = te.time;
            }

            data.events.Sort((a, b) => a.time.CompareTo(b.time));
            PairEffectEnds(data.events);
            cache[clip] = data;
            return data;
        }

        /// <summary>给每个特效事件找它的 EffectEnd（同名、在它之后、最近、没被配走）</summary>
        private static void PairEffectEnds(List<TimelineEvent> events)
        {
            if (endScratch.Count == 0) return;
            endScratch.Sort((a, b) => a.time.CompareTo(b.time));

            for (int i = 0; i < events.Count; i++)
            {
                TimelineEvent te = events[i];
                if (te.kind != TimelineEventKind.Effect && te.kind != TimelineEventKind.EffectByKey) continue;

                string key = te.kind == TimelineEventKind.EffectByKey ? (te.str ?? "") : "";

                for (int j = 0; j < endScratch.Count; j++)
                {
                    TimelineEvent end = endScratch[j];
                    if (end.time <= te.time || end.str != key) continue;

                    te.endTime = end.time;
                    events[i] = te;
                    endScratch.RemoveAt(j);   // 配走了，别的特效不能再用
                    break;
                }
            }
            endScratch.Clear();
        }

        // ==========================================================
        // 三段时长的统一口径（前摇 / 攻击 / 后摇）
        // ==========================================================

        private static readonly List<HitboxWindow> windowScratch = new List<HitboxWindow>();

        /// <summary>没配动画剪辑时的兜底时长</summary>
        public const float FallbackDuration = 2.0f;

        /// <param name="shift">【旧口径，已不用于触发事件】填了前摇后，前摇时长与动画前摇的差</param>
        public static void ResolvePhases(ComboNode node,
            out float windup, out float active, out float recovery, out float shift)
            => ResolvePhases(node, out windup, out active, out recovery, out shift, out _);

        /// <param name="activeExplicit">
        /// 攻击段的结束点是不是【明确给定】的（动画里打了 MarkAttackEnd，或 ComboNode 上手填了攻击时长）。
        /// 只是推算出来的话，到点不会去掐还在跑的判定。
        /// </param>
        public static void ResolvePhases(ComboNode node,
            out float windup, out float active, out float recovery, out float shift, out bool activeExplicit)
        {
            AttackTiming t = ResolveTiming(node);
            windup = t.windup;
            active = t.active;
            recovery = t.recovery;
            shift = t.windup - t.animWindup;
            activeExplicit = t.activeExplicit;
        }

        /// <summary>算出一招的三段时长和动画换算（见 AttackTiming）</summary>
        public static AttackTiming ResolveTiming(ComboNode node)
        {
            AnimationClip clip = node != null ? node.attackClip : null;
            AttackTimelineData data = Get(clip);

            bool hasClip = clip != null && clip.length > 0f;
            float clipLen = hasClip ? clip.length : FallbackDuration;

            float animWindup = data.firstStrikeTime >= 0f ? data.firstStrikeTime : 0f;

            float animActiveEnd;
            if (data.attackEndTime >= 0f) animActiveEnd = data.attackEndTime;
            else if (data.firstStrikeTime >= 0f && node != null) animActiveEnd = EstimateActiveEnd(node, data);
            else animActiveEnd = clipLen;

            animActiveEnd = Mathf.Clamp(animActiveEnd, animWindup, clipLen);

            return new AttackTiming
            {
                animWindup = animWindup,
                animActiveEnd = animActiveEnd,
                clipLength = clipLen,
                hasClip = hasClip,

                windup = (node != null && node.windupOverride >= 0f) ? node.windupOverride : animWindup,
                active = (node != null && node.activeOverride >= 0f) ? node.activeOverride : animActiveEnd - animWindup,
                recovery = (node != null && node.recoveryOverride >= 0f) ? node.recoveryOverride : clipLen - animActiveEnd,

                activeExplicit = (node != null && node.activeOverride >= 0f) || data.attackEndTime >= 0f,
            };
        }

        /// <summary>没打 MarkAttackEnd 时，按判定窗口序列 / 连射时长推算攻击段在动画里的结束时刻</summary>
        private static float EstimateActiveEnd(ComboNode node, AttackTimelineData data)
        {
            float end = data.firstStrikeTime;

            // 判定窗口序列的总时长（与 PlayerHitDetection 的顺序执行口径一致）
            node.CollectWindows(windowScratch);
            float seq = 0f;
            for (int i = 0; i < windowScratch.Count; i++)
            {
                HitboxWindow w = windowScratch[i];
                seq = Mathf.Max(seq, w.startDelay) + Mathf.Max(0f, w.duration);
            }

            int count = Mathf.Max(1, node.projectileCount);
            float burst = (count > 1 && node.projectileInterval > 0f) ? (count - 1) * node.projectileInterval : 0f;

            for (int i = 0; i < data.events.Count; i++)
            {
                if (data.events[i].kind == TimelineEventKind.Hitbox) end = Mathf.Max(end, data.events[i].time + seq);
                else if (data.events[i].kind == TimelineEventKind.Projectile) end = Mathf.Max(end, data.events[i].time + burst);
            }
            return end;
        }
    }

    /// <summary>
    /// 【攻击实例】一次出招的全部逻辑（判定、子弹、特效）与它的阶段。
    /// 自带 node / weapon 快照与命中名单，可以多份并行。
    /// </summary>
    public class AttackInstance
    {
        public ComboNode node;
        public WeaponMoveSet weapon;
        public InputCmd cmd;
        public int attackId = -1;

        public bool isFly;
        public bool isChargeRelease;
        public bool isDual;

        /// <summary>已经过去多久</summary>
        public float elapsed;

        /// <summary>出招时刻（Time.time）</summary>
        public float startTime;

        /// <summary>总时长 = 前摇 + 攻击 + 后摇。这只手在此之前不能再出招</summary>
        public float duration;

        /// <summary>前摇结束时刻（相对出招）</summary>
        public float windupEnd;

        /// <summary>攻击段结束时刻（相对出招）</summary>
        public float activeEnd;

        /// <summary>攻击段结束点是明确给定的（MarkAttackEnd / 手填）。是的话到点收判定框；推算值则让判定自己走完</summary>
        public bool activeExplicit;

        /// <summary>【旧口径，已不用于触发事件】前摇时长与动画前摇的差。事件时间现在走 timing 的换算</summary>
        public float eventShift;

        /// <summary>三段时长和"动画时间 ↔ 实际时间"的换算。事件触发、动画倍速都按它来</summary>
        public AttackTiming timing;

        /// <summary>还在跑（还可能产生判定/子弹/特效）</summary>
        public bool alive = true;

        /// <summary>
        /// 这一招是在飞行中放出来的（飞行中松手放蓄力会先落回地面状态出招）。
        /// 进入后摇阶段后由 ComboState 自动把人送回飞行状态。
        /// </summary>
        public bool resumeFlight;

        /// <summary>还是当前动画的主人。被别的招顶掉动画后为 false —— 此后连射不再让角色转向</summary>
        public bool ownsAnimation = true;

        // ---- 连段回退快照（冲刺/滑铲打断前摇 → 本招不算，段数退回）----
        public ComboNode prevNode;
        public int prevDepth;
        public InputCmd prevCmd;
        public WeaponMoveSet prevWeapon;
        public float prevExpire;

        // ---- 运行时 ----
        internal AttackTimelineData data;
        internal int nextEvent;
        internal int startFrame;
        internal bool strikesClosed;
        internal readonly List<PlayerHitDetection.HitboxRun> hitRuns = new List<PlayerHitDetection.HitboxRun>();
        internal readonly List<PlayerWeaponEmitter.BurstRun> bursts = new List<PlayerWeaponEmitter.BurstRun>();
        internal readonly List<GameObject> spawnedEffects = new List<GameObject>();
        internal readonly List<GameObject> spawnedBullets = new List<GameObject>();

        // 与上面两张表一一对应：生成那一刻对象的借出号（PooledObject.BorrowId），回收前核对用
        internal readonly List<int> spawnedEffectIds = new List<int>();
        internal readonly List<int> spawnedBulletIds = new List<int>();

        public AttackPhase Phase
        {
            get
            {
                if (!alive) return AttackPhase.Recovery;
                if (elapsed < windupEnd) return AttackPhase.Windup;
                // 连射 / 判定还没走完也算攻击中 —— 攻击段的结束点可能比最后一发子弹早一两帧
                if (elapsed < activeEnd || hitRuns.Count > 0 || bursts.Count > 0) return AttackPhase.Active;
                return AttackPhase.Recovery;
            }
        }
    }
}

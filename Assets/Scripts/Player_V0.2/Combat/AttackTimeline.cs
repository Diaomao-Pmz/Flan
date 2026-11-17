using System.Collections.Generic;
using UnityEngine;

namespace Flandre.CombatSystem
{
    // ==========================================================
    // 【实验：平a/蓄力重做】攻击逻辑由「动画事件驱动」改为「时间表驱动」
    //
    // 出招时从 node.attackClip.events 里读出 TriggerAttackHitbox / SpawnProjectile /
    // PlayEffect / PlayEffectByKey 的时间点，生成一张时间表，由「攻击实例」自己掐表执行。
    // 动画被顶掉不影响实例继续跑 —— 这是 R5「只打断动画、不打断逻辑」
    // 和 R1「按前摇/判定/后摇分阶段」的地基。
    //
    // 不需要重新配置任何时间：时间点全部来自动画里已经打好的事件。
    // 本组件由 ComboInputBuffer.Awake 自动挂到玩家身上，不用手动加。
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

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ClearCache() => cache.Clear();

        public static AttackTimelineData Get(AnimationClip clip)
        {
            if (clip == null) return empty;
            if (cache.TryGetValue(clip, out AttackTimelineData data)) return data;

            data = new AttackTimelineData { clipLength = clip.length };

            AnimationEvent[] evs = clip.events;
            for (int i = 0; i < evs.Length; i++)
            {
                AnimationEvent e = evs[i];

                // "攻击结束"时间戳：只记时间，不进事件表
                if (e.functionName == "MarkAttackEnd")
                {
                    if (data.attackEndTime < 0f || e.time > data.attackEndTime) data.attackEndTime = e.time;
                    continue;
                }

                TimelineEvent te = new TimelineEvent { time = e.time };

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
            cache[clip] = data;
            return data;
        }

        // ==========================================================
        // 三段时长的统一口径（前摇 / 攻击 / 后摇）
        //
        //   前摇 = 动画开始 → 第一个 TriggerAttackHitbox / SpawnProjectile 事件
        //   攻击 = 该事件 → 动画事件 MarkAttackEnd
        //   后摇 = MarkAttackEnd → 动画播完
        //
        // ComboNode 上的 Windup / Active / Recovery Override 填了（>= 0）就以它为准，
        // 没填（-1）就按上面从动画读。三段加起来 = 这一招的总时长：
        // 这只手在总时长内不能再出招，连段窗口从总时长结束起算。
        // 不再有"动画播完之后另加一段 Recovery Time"。
        //
        // 动画里没打 MarkAttackEnd 时的兜底：攻击段按判定窗口 / 连射时长推算（不超过动画长度）；
        // 连出刀/出弹事件都没有 → 整招都算攻击段。
        // ==========================================================

        private static readonly List<HitboxWindow> windowScratch = new List<HitboxWindow>();

        /// <summary>没配动画剪辑时的兜底时长</summary>
        public const float FallbackDuration = 2.0f;

        /// <param name="shift">事件整体平移量：前摇被覆盖后，出刀/出弹/特效事件跟着挪</param>
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
            AnimationClip clip = node != null ? node.attackClip : null;
            AttackTimelineData data = Get(clip);

            float clipLen = (clip != null && clip.length > 0f) ? clip.length : FallbackDuration;

            float animWindup = data.firstStrikeTime >= 0f ? data.firstStrikeTime : 0f;

            float animActiveEnd;
            if (data.attackEndTime >= 0f) animActiveEnd = data.attackEndTime;
            else if (data.firstStrikeTime >= 0f && node != null) animActiveEnd = EstimateActiveEnd(node, data);
            else animActiveEnd = clipLen;

            animActiveEnd = Mathf.Clamp(animActiveEnd, animWindup, clipLen);

            windup = (node != null && node.windupOverride >= 0f) ? node.windupOverride : animWindup;
            active = (node != null && node.activeOverride >= 0f) ? node.activeOverride : animActiveEnd - animWindup;
            recovery = (node != null && node.recoveryOverride >= 0f) ? node.recoveryOverride : clipLen - animActiveEnd;

            shift = windup - animWindup;

            activeExplicit = (node != null && node.activeOverride >= 0f) || data.attackEndTime >= 0f;
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

        /// <summary>事件平移量（前摇被 ComboNode 覆盖时，动画事件的时间点跟着挪）</summary>
        public float eventShift;

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

    /// <summary>【时间表执行器】持有所有在跑的攻击实例，每帧推进</summary>
    [DefaultExecutionOrder(50)]
    public class AttackTimelineRunner : MonoBehaviour
    {
        public bool verboseLog = false;

        private readonly List<AttackInstance> instances = new List<AttackInstance>();

        private PlayerHitDetection hit;
        private PlayerWeaponEmitter emitter;
        private PlayerEffectSpawner fx;

        private void Awake()
        {
            hit = GetComponent<PlayerHitDetection>();
            emitter = GetComponent<PlayerWeaponEmitter>();
            fx = GetComponent<PlayerEffectSpawner>();
        }

        /// <summary>一招的总时长 = 前摇 + 攻击 + 后摇</summary>
        public static float ExpectedDuration(ComboNode node)
        {
            AttackTimelineData.ResolvePhases(node, out float w, out float a, out float r, out _);
            return w + a + r;
        }

        // ==========================================================
        // 开始
        // ==========================================================

        public AttackInstance Begin(ComboNode node, WeaponMoveSet weapon, InputCmd cmd, bool isFly = false)
        {
            AttackInstance inst = new AttackInstance
            {
                node = node,
                weapon = weapon,
                cmd = cmd,
                isFly = isFly,
                data = AttackTimelineData.Get(node != null ? node.attackClip : null),
                startFrame = Time.frameCount,
                startTime = Time.time,
            };

            AttackTimelineData.ResolvePhases(node, out float windup, out float active, out float recovery,
                out float shift, out bool activeExplicit);
            inst.activeExplicit = activeExplicit;
            inst.windupEnd = windup;
            inst.activeEnd = windup + active;
            inst.duration = windup + active + recovery;
            inst.eventShift = shift;

            instances.Add(inst);

            if (verboseLog)
            {
                Debug.Log($"[时间表] {node.nodeName} 开始：前摇 {windup:F2}s / 攻击 {active:F2}s / 后摇 {recovery:F2}s（总长 {inst.duration:F2}s），" +
                          $"事件 {inst.data.events.Count} 个" +
                          (inst.data.attackEndTime < 0f ? "，动画里没有 MarkAttackEnd 事件（攻击段为推算值）" : ""));
            }

            return inst;
        }

        // ==========================================================
        // 每帧推进
        // ==========================================================

        private void Update()
        {
            float dt = Time.deltaTime;

            for (int i = 0; i < instances.Count; i++)
            {
                AttackInstance inst = instances[i];
                if (!inst.alive) continue;

                // 出招当帧不推进：动画要到本帧 LateUpdate 才从 0 开始播，下一帧两边一起走 dt
                if (inst.startFrame == Time.frameCount) continue;

                Tick(inst, dt);
            }

            for (int i = instances.Count - 1; i >= 0; i--)
            {
                if (!instances[i].alive) instances.RemoveAt(i);
            }
        }

        private void Tick(AttackInstance inst, float dt)
        {
            inst.elapsed += dt;

            // 先推进已经在跑的判定 / 连射，再触发新事件（新开的那份自己会做第 0 帧的检测）
            for (int i = inst.hitRuns.Count - 1; i >= 0; i--)
            {
                hit.TickRun(inst.hitRuns[i], dt);
                if (inst.hitRuns[i].done) inst.hitRuns.RemoveAt(i);
            }

            for (int i = inst.bursts.Count - 1; i >= 0; i--)
            {
                emitter.TickBurst(inst.bursts[i], dt, inst.ownsAnimation, inst.spawnedBullets);
                if (inst.bursts[i].done) inst.bursts.RemoveAt(i);
            }

            var evs = inst.data.events;
            while (inst.alive && inst.nextEvent < evs.Count
                   && evs[inst.nextEvent].time + inst.eventShift <= inst.elapsed)
            {
                TimelineEvent e = evs[inst.nextEvent];
                inst.nextEvent++;

                // 攻击段已经结束：晚到的出刀/出弹事件不再触发（特效照常）
                bool isStrike = e.kind == TimelineEventKind.Hitbox || e.kind == TimelineEventKind.Projectile;
                if (isStrike && e.time + inst.eventShift > inst.activeEnd + 0.0001f) continue;

                Fire(inst, e);
            }

            // 攻击段结束。
            //
            // 【修复 2026-11-16】原先到点把判定和连射一律掐掉，结果连射的最后一发经常射不出来：
            // 攻击段终点 = 出弹时刻 + (发数-1)×间隔，而最后一发要等计时"满"这个间隔才射，
            // 两者差着一帧以内的误差，掐断总是抢在前面 —— B2 只射 1 发（看起来像 B1），B3 只射 2 发。
            // 现在：
            //   · 连射一律让它射完（已经开始的一轮弹幕不会被攻击段终点截断）
            //   · 判定框只有在终点是【明确给定】的（MarkAttackEnd / 手填攻击时长）时才到点收掉；
            //     终点只是推算值时让判定框自己走完
            if (!inst.strikesClosed && inst.elapsed >= inst.activeEnd)
            {
                if (inst.activeExplicit)
                {
                    for (int i = 0; i < inst.hitRuns.Count; i++) hit?.EndRun(inst.hitRuns[i]);
                    inst.hitRuns.Clear();
                }
                inst.strikesClosed = true;
            }

            // 总时长走完：这一招彻底结束
            if (inst.elapsed >= inst.duration)
            {
                StopStrikes(inst);
                inst.alive = false;
            }
        }

        private void Fire(AttackInstance inst, TimelineEvent e)
        {
            switch (e.kind)
            {
                case TimelineEventKind.Hitbox:
                    if (hit != null)
                    {
                        PlayerHitDetection.HitboxRun run = hit.BeginRun(inst.node);
                        if (run != null && !run.done) inst.hitRuns.Add(run);
                    }
                    break;

                case TimelineEventKind.Projectile:
                    if (emitter != null)
                    {
                        PlayerWeaponEmitter.BurstRun b = emitter.BeginBurst(
                            inst.node, inst.weapon, inst.ownsAnimation, inst.spawnedBullets);
                        if (b != null && !b.done) inst.bursts.Add(b);
                    }
                    break;

                case TimelineEventKind.Effect:
                    if (fx != null) Track(inst.spawnedEffects, fx.SpawnAttackEffect(inst.node));
                    break;

                case TimelineEventKind.EffectByKey:
                    if (fx != null) Track(inst.spawnedEffects, fx.SpawnEffectByKey(e.str, inst.node));
                    break;
            }
        }

        private static void Track(List<GameObject> list, GameObject go)
        {
            if (go != null) list.Add(go);
        }

        // ==========================================================
        // 结束的几种方式
        // ==========================================================

        private void StopStrikes(AttackInstance inst)
        {
            for (int i = 0; i < inst.hitRuns.Count; i++) hit?.EndRun(inst.hitRuns[i]);
            inst.hitRuns.Clear();

            for (int i = 0; i < inst.bursts.Count; i++) emitter?.EndBurst(inst.bursts[i]);
            inst.bursts.Clear();
        }

        /// <summary>
        /// 掐断：之后不再产生任何判定/子弹/特效；已经生成的特效和子弹不动。
        /// 前摇作废、冲刺/滑铲打断、受击都走这个。
        /// </summary>
        public void Void(AttackInstance inst)
        {
            if (inst == null || !inst.alive) return;
            StopStrikes(inst);
            inst.alive = false;
        }

        /// <summary>
        /// 整发撤掉（R5 判定方式 B：第一发单蓄力被强化招替换）：
        /// 判定、连射停掉，已经生成的特效和子弹也回收。
        /// </summary>
        public void KillAll(AttackInstance inst)
        {
            if (inst == null) return;

            StopStrikes(inst);
            inst.alive = false;

            for (int i = 0; i < inst.spawnedEffects.Count; i++)
                ObjectPoolManager.Instance?.Recycle(inst.spawnedEffects[i]);
            inst.spawnedEffects.Clear();

            for (int i = 0; i < inst.spawnedBullets.Count; i++)
                ObjectPoolManager.Instance?.Recycle(inst.spawnedBullets[i]);
            inst.spawnedBullets.Clear();
        }

        /// <summary>掐断所有地面攻击实例（冲刺/滑铲打断、受击）</summary>
        public void VoidAll(bool includeFly = true)
        {
            for (int i = 0; i < instances.Count; i++)
            {
                if (!includeFly && instances[i].isFly) continue;
                Void(instances[i]);
            }
        }

        /// <summary>有没有地面攻击实例正处于判定阶段（含动画已被顶掉、逻辑还在跑的）</summary>
        public bool AnyGroundActive()
        {
            for (int i = 0; i < instances.Count; i++)
            {
                AttackInstance inst = instances[i];
                if (inst.alive && !inst.isFly && inst.Phase == AttackPhase.Active) return true;
            }
            return false;
        }
    }
}

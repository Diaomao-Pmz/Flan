using System.Collections.Generic;
using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 【攻击判定框】
/// </summary>
public class PlayerHitDetection : MonoBehaviour
{
    private PlayerController player;
    private TargetFinder targetFinder;

    [Header("Debug 设置")]
    public bool showHitbox = true;

    [Tooltip("按此键切换判定框显示。仅在旧输入系统可用时生效")]
    public KeyCode toggleKey = KeyCode.F3;

    [Header("命中设置")]
    [Tooltip("敌人 Tag")]
    public string enemyTag = "Enemy";

    // ==========================================================
    // 命中广播
    // ==========================================================

    /// <summary>命中敌人时广播 (敌人本体, 本次伤害)。命中回MP、打击感反馈订阅这个</summary>
    public event System.Action<EntityBase, int> OnEnemyHit;

    /// <summary>本次招式最后命中的目标。招式开始时清空</summary>
    public Transform LastHitTarget { get; private set; }

    /// <summary>本次招式一共命中了几个目标</summary>
    public int HitCountThisAttack { get; private set; }

    // ==========================================================
    // 【实验：平a/蓄力重做】判定改为「多份并行 + 时间表驱动」
    // ==========================================================

    /// <summary>一次出刀的判定序列（一招里所有 HitboxWindow）</summary>
    public class HitboxRun
    {
        public ComboNode node;
        public readonly List<HitboxWindow> windows = new List<HitboxWindow>();
        public readonly HashSet<Collider2D> alreadyHit = new HashSet<Collider2D>();

        internal float seqTime;      // 序列已经走了多久
        internal int index;          // 轮到第几段窗口
        internal bool inWindow;      // 当前段是否正在判定
        internal float winElapsed;   // 当前段已经判定了多久
        internal float winDuration;  // 当前段实际要判定多久（duration 填 -1 时 = 到判定段结束）

        /// <summary>开这份序列时，距离判定段结束还有多久（秒）；-1 = 不知道</summary>
        internal float untilActiveEnd = -1f;

        /// <summary>这一帧正在判定的窗口（Gizmos 用）</summary>
        internal HitboxWindow activeWindow;

        public bool done;
    }

    private readonly List<HitboxRun> activeRuns = new List<HitboxRun>();

    // 复用缓冲，避免每帧产生 GC
    private readonly List<Collider2D> overlapResults = new List<Collider2D>(16);
    private ContactFilter2D overlapFilter;

    void Awake()
    {
        player = GetComponent<PlayerController>();
        targetFinder = GetComponent<TargetFinder>();

        overlapFilter = ContactFilter2D.noFilter;
        overlapFilter.useTriggers = Physics2D.queriesHitTriggers;
    }

    void Update()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        if (Input.GetKeyDown(toggleKey)) showHitbox = !showHitbox;
#endif
    }

    // ==========================================================
    // 动画事件入口（已改为空操作）
    // ==========================================================

    /// <summary>
    /// 【已改为空操作】原先由动画事件触发判定。
    /// 现在判定由 AttackTimelineRunner 按时间表调用 BeginRun —— 时间点仍然取自动画里的这个事件，
    /// 所以动画上的事件【不要删】。保留本方法只是为了让 Unity 不报 "has no receiver"。
    /// </summary>
    public void TriggerAttackHitbox() { }

    /// <summary>
    /// 【"攻击结束"时间戳】动画事件用，本身是空操作。
    /// </summary>
    public void MarkAttackEnd() { }

    /// <summary>
    /// 【"攻击开始"时间戳】动画事件用，本身是空操作。
    /// 打了的话前摇到它为止，判定段从它开始（出刀事件可以放在判定段里更靠后的地方）。
    /// </summary>
    public void MarkAttackBegin() { }

    /// <summary>掐断所有正在跑的判定</summary>
    public void ForceStopHitbox()
    {
        for (int i = 0; i < activeRuns.Count; i++)
        {
            activeRuns[i].done = true;
            activeRuns[i].activeWindow = null;
        }
        activeRuns.Clear();
    }

    // ==========================================================
    // 判定序列（由时间表驱动）
    // ==========================================================

    /// <summary>开一份判定序列，并立刻做第 0 帧的检测</summary>
    /// <param name="untilActiveEnd">距离判定段结束还有多久（秒）。持续时间填 -1 的段会一直开到那时；-1 = 不知道</param>
    public HitboxRun BeginRun(ComboNode node, float untilActiveEnd = -1f)
    {
        if (node == null) return null;

        HitboxRun run = new HitboxRun { node = node, untilActiveEnd = untilActiveEnd };

        // 没配多段的话会自动用旧版单段参数合成一条 —— 已配好的资产无需改动
        node.CollectWindows(run.windows);

        LastHitTarget = null;
        HitCountThisAttack = 0;

        activeRuns.Add(run);
        TickRun(run, 0f);
        return run;
    }

    /// <summary>
    /// 推进一份判定序列。口径与旧协程一致：
    /// 各段按顺序执行，等到 startDelay 才开；duration 为 0 的段只判定一帧；
    /// duration 为 -1 的段一直开到判定段结束（不知道判定段什么时候结束时按 0 处理）。
    /// </summary>
    public void TickRun(HitboxRun run, float dt)
    {
        if (run == null || run.done) return;

        int guard = 0;
        while (!run.done && guard++ < 64)
        {
            if (run.index >= run.windows.Count)
            {
                EndRun(run);
                return;
            }

            HitboxWindow w = run.windows[run.index];

            if (!run.inWindow)
            {
                float wait = w.startDelay - run.seqTime;
                if (wait > 0f)
                {
                    if (dt < wait)
                    {
                        run.seqTime += dt;
                        return;
                    }
                    run.seqTime += wait;
                    dt -= wait;
                }

                run.inWindow = true;
                run.winElapsed = 0f;
                run.winDuration = ResolveDuration(run, w);
                run.activeWindow = w;

                // 本段是否重新允许命中同一个敌人（三连斩每刀都吃伤害）
                if (w.refreshHitList) run.alreadyHit.Clear();

                CheckOverlap(run, w);

                // duration 为 0 → 瞬间伤害，只判定一帧
                if (run.winDuration <= 0f)
                {
                    run.inWindow = false;
                    run.activeWindow = null;
                    run.index++;
                    continue;
                }
                return;
            }

            run.winElapsed += dt;
            dt = 0f;

            if (run.winElapsed < run.winDuration)
            {
                CheckOverlap(run, w);
                return;
            }

            run.inWindow = false;
            run.activeWindow = null;
            run.seqTime += Mathf.Max(0f, run.winDuration);
            run.index++;
        }
    }

    /// <summary>这一段实际判定多久：duration ≥ 0 照填的；-1 = 从这一段开始一直到判定段结束</summary>
    private static float ResolveDuration(HitboxRun run, HitboxWindow w)
    {
        if (w.duration >= 0f) return w.duration;
        if (run.untilActiveEnd < 0f) return 0f;
        return Mathf.Max(0f, run.untilActiveEnd - run.seqTime);
    }

    /// <summary>结束一份判定序列</summary>
    public void EndRun(HitboxRun run)
    {
        if (run == null) return;
        run.done = true;
        run.activeWindow = null;
        activeRuns.Remove(run);
    }

    private void CheckOverlap(HitboxRun run, HitboxWindow window)
    {
        ComboNode node = run.node;
        HashSet<Collider2D> alreadyHitEnemies = run.alreadyHit;

        float dirX = player.facingDirection;
        Vector2 finalOffset = new Vector2(window.offset.x * dirX, window.offset.y);
        Vector2 boxCenter = (Vector2)transform.position + finalOffset;

        // 勾了「近战判定」消弹：同一个框里的 Boss 子弹顺手消掉（单独查询，确保查得到触发器）
        if (node.CancelsBulletsBy(BulletCancelMode.Melee))
            BulletCanceller.CancelInBox(boxCenter, window.size);

        overlapResults.Clear();
        Physics2D.OverlapBox(boxCenter, window.size, 0f, overlapFilter, overlapResults);

        for (int i = 0; i < overlapResults.Count; i++)
        {
            Collider2D hit = overlapResults[i];
            if (hit == null) continue;
            if (alreadyHitEnemies.Contains(hit)) continue;
            if (!hit.CompareTag(enemyTag)) continue;

            alreadyHitEnemies.Add(hit);

            // 必须用 GetComponentInParent —— 判定框常挂在子物体上，
            // 直接 GetComponent 会砍中但不掉血
            EntityBase enemy = hit.GetComponentInParent<EntityBase>();

            if (enemy == null)
            {
                Debug.LogWarning($"[判定] 砍中了 {hit.name}，但它和它的父节点上都没有 EntityBase", hit);
                continue;
            }

            // 把本招的打断能力一并传过去。
            // 【注意】打断与否由受击方裁决 —— 这里只声明"我能破哪几类"，
            // 至于敌人当前在做的动作算不算那一类、破了之后怎么表现，
            // 全是敌人侧的事。这和击退力度归受击方管是同一条原则。
            // 击退 Boss：招式填的距离放在 knockbackForce.x（受击方自己决定怎么滑、滑多久）
            bool knock = node.bossKnockbackDistance > 0f;
            enemy.TakeDamage(new DamageInfo(
                window.damage, DamageType.Melee, transform.position, gameObject,
                node.breakMask, node.hitReaction, node.hitReactionParam,
                knock, knock ? new Vector2(node.bossKnockbackDistance, 0f) : Vector2.zero,
                node.bossHitStun));

            // ---- 命中记账与广播 ----
            LastHitTarget = enemy.transform;
            HitCountThisAttack++;

            OnEnemyHit?.Invoke(enemy, window.damage);

            // ---- 自动朝向 ----
            TryFaceTarget(node, enemy.transform);
        }
    }

    /// <summary>
    /// 【批次N 新增】命中后自动转向目标。
    /// </summary>
    private void TryFaceTarget(ComboNode node, Transform target)
    {
        if (node == null || !node.faceTargetOnHit) return;
        if (player == null || target == null) return;

        int facing = (targetFinder != null)
            ? targetFinder.GetFacingTowards(target)
            : (target.position.x > transform.position.x ? 1 : -1);

        // 0 表示重叠，此时不转身，避免左右抖动
        if (facing != 0) player.SetFacingDirection(facing);
    }

    // ==========================================================
    // Gizmos
    // ==========================================================

    /// <summary>
    /// 【多段判定的可视化】
    /// </summary>
    private void OnDrawGizmos()
    {
        if (!showHitbox || !Application.isPlaying) return;
        if (activeRuns.Count == 0) return;

        float dirX = (player != null) ? player.facingDirection : 1f;

        for (int r = 0; r < activeRuns.Count; r++)
        {
            HitboxRun run = activeRuns[r];

            for (int i = 0; i < run.windows.Count; i++)
            {
                HitboxWindow w = run.windows[i];
                if (w == null) continue;

                bool isActive = ReferenceEquals(w, run.activeWindow);

                Gizmos.color = isActive
                    ? Color.green
                    : new Color(0.3f, 0.9f, 0.3f, 0.25f);

                Vector2 offset = new Vector2(w.offset.x * dirX, w.offset.y);
                Gizmos.DrawWireCube((Vector2)transform.position + offset, w.size);
            }
        }
    }
}

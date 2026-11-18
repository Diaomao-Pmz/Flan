using System.Collections;
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

<<<<<<< Updated upstream
    private Coroutine activeHitboxCoroutine;
    private readonly HashSet<Collider2D> alreadyHitEnemies = new HashSet<Collider2D>();
=======
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

        /// <summary>这一帧正在判定的窗口（Gizmos 用）</summary>
        internal HitboxWindow activeWindow;

        public bool done;
    }

    private readonly List<HitboxRun> activeRuns = new List<HitboxRun>();
>>>>>>> Stashed changes

    // 复用缓冲，避免每帧产生 GC
    private readonly List<Collider2D> overlapResults = new List<Collider2D>(16);
    private ContactFilter2D overlapFilter;
    private readonly List<HitboxWindow> windowBuffer = new List<HitboxWindow>();

    // 当前正在生效的窗口（供 Gizmos 精确绘制）
    private HitboxWindow activeWindow;
    private bool isHitboxActiveThisFrame = false;

    // 整套招式期间保留全部窗口，供 Gizmos 一并画出来
    private bool isSequenceRunning = false;

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
    // 动画事件入口
    // ==========================================================

    /// <summary>
<<<<<<< Updated upstream
    /// 由动画事件触发：开始跑本招式的判定窗口序列。
    ///
    /// 一次调用跑完整个招式的所有段。
    /// 【不要】为了做三连斩而在动画里插三个 TriggerAttackHitbox 事件 ——
    /// 那样后面的会掐断前面的。正确做法是在 ComboNode 里配三条 HitboxWindow。
=======
    /// 【已改为空操作】原先由动画事件触发判定。
    /// 现在判定由 AttackTimelineRunner 按时间表调用 BeginRun —— 时间点仍然取自动画里的这个事件，
    /// 所以动画上的事件【不要删】。保留本方法只是为了让 Unity 不报 "has no receiver"。
    /// </summary>
    public void TriggerAttackHitbox() { }

    /// <summary>
    /// 【"攻击结束"时间戳】动画事件用，本身是空操作。
>>>>>>> Stashed changes
    /// </summary>
    public void TriggerAttackHitbox()
    {
        ComboNode currentNode = player.inputBuffer.currentNode;
        if (currentNode == null) return;

        if (activeHitboxCoroutine != null) StopCoroutine(activeHitboxCoroutine);

        alreadyHitEnemies.Clear();
        LastHitTarget = null;
        HitCountThisAttack = 0;

        activeHitboxCoroutine = StartCoroutine(HitboxSequenceCoroutine(currentNode));
    }

    public void ForceStopHitbox()
    {
        if (activeHitboxCoroutine != null)
        {
            StopCoroutine(activeHitboxCoroutine);
            activeHitboxCoroutine = null;
        }

        alreadyHitEnemies.Clear();
        activeWindow = null;
        isHitboxActiveThisFrame = false;
        isSequenceRunning = false;
    }

    // ==========================================================
    // 判定序列
    // ==========================================================

    private IEnumerator HitboxSequenceCoroutine(ComboNode node)
    {
        // 没配多段的话会自动用旧版单段参数合成一条 —— 已配好的资产无需改动
        node.CollectWindows(windowBuffer);
        isSequenceRunning = true;

        float sequenceTime = 0f;

        for (int i = 0; i < windowBuffer.Count; i++)
        {
            HitboxWindow window = windowBuffer[i];

            float waitTime = window.startDelay - sequenceTime;
            if (waitTime > 0f)
            {
                yield return new WaitForSeconds(waitTime);
                sequenceTime += waitTime;
            }

            // 本段是否重新允许命中同一个敌人（三连斩每刀都吃伤害）
            if (window.refreshHitList) alreadyHitEnemies.Clear();

            yield return RunSingleWindow(node, window);

            sequenceTime += Mathf.Max(0f, window.duration);
        }

        activeWindow = null;
        isHitboxActiveThisFrame = false;
        isSequenceRunning = false;
        activeHitboxCoroutine = null;
    }

    private IEnumerator RunSingleWindow(ComboNode node, HitboxWindow window)
    {
        float elapsed = 0f;

        activeWindow = window;
        isHitboxActiveThisFrame = true;

        do
        {
            CheckOverlap(node, window);

            // duration 为 0 → 瞬间伤害，只判定一帧
            if (window.duration <= 0f) break;

            yield return null;
            elapsed += Time.deltaTime;

        } while (elapsed < window.duration);

        isHitboxActiveThisFrame = false;
    }

    private void CheckOverlap(ComboNode node, HitboxWindow window)
    {
        float dirX = player.facingDirection;
        Vector2 finalOffset = new Vector2(window.offset.x * dirX, window.offset.y);
        Vector2 boxCenter = (Vector2)transform.position + finalOffset;

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
            enemy.TakeDamage(new DamageInfo(
                window.damage, DamageType.Melee, transform.position, gameObject,
                node.breakMask, node.hitReaction, node.hitReactionParam));

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
        if (!isSequenceRunning) return;

        float dirX = (player != null) ? player.facingDirection : 1f;

        for (int i = 0; i < windowBuffer.Count; i++)
        {
            HitboxWindow w = windowBuffer[i];
            if (w == null) continue;

            bool isActive = isHitboxActiveThisFrame && ReferenceEquals(w, activeWindow);

            Gizmos.color = isActive
                ? Color.green
                : new Color(0.3f, 0.9f, 0.3f, 0.25f);

            Vector2 offset = new Vector2(w.offset.x * dirX, w.offset.y);
            Gizmos.DrawWireCube((Vector2)transform.position + offset, w.size);
        }
    }
}

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 【攻击判定框】
///
/// ==========================================================
/// 【批次N 新增】命中通知管道
///
/// 判定框打中敌人后，原先【不告诉任何人】—— 伤害算完就结束了。
/// 但有三个功能都需要知道"刚打中了谁"：
///   自动朝向    —— 滑铲攻击命中后转向那个敌人
///   命中回 MP   —— 按招式伤害回蓝
///   蓄力突刺    —— dash 途中第一个碰到的敌人
///
/// 三个需求同一条管道，所以现在广播出去，而不是各自再写一遍检测。
///
/// 比喻：原先是收银员收完钱就把小票扔了。
///       现在留一份账 —— 财务、会员积分、库存都要看这张单子。
/// ==========================================================
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

    private Coroutine activeHitboxCoroutine;
    private readonly HashSet<Collider2D> alreadyHitEnemies = new HashSet<Collider2D>();

    // 复用缓冲，避免每帧产生 GC
    private readonly List<Collider2D> overlapResults = new List<Collider2D>(16);
    private ContactFilter2D overlapFilter;
    private readonly List<HitboxWindow> windowBuffer = new List<HitboxWindow>();

    // 当前正在生效的窗口（供 Gizmos 精确绘制）
    private HitboxWindow activeWindow;
    private bool isHitboxActiveThisFrame = false;

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
    /// 由动画事件触发：开始跑本招式的判定窗口序列。
    ///
    /// 一次调用跑完整个招式的所有段。
    /// 【不要】为了做三连斩而在动画里插三个 TriggerAttackHitbox 事件 ——
    /// 那样后面的会掐断前面的。正确做法是在 ComboNode 里配三条 HitboxWindow。
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
    }

    // ==========================================================
    // 判定序列
    // ==========================================================

    private IEnumerator HitboxSequenceCoroutine(ComboNode node)
    {
        // 没配多段的话会自动用旧版单段参数合成一条 —— 已配好的资产无需改动
        node.CollectWindows(windowBuffer);

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
                node.breakMask));

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
    ///
    /// 用于滑铲攻击这类"边冲边砍"的招式：
    ///   命中敌人 → 面向那个敌人
    ///   没命中   → 维持原朝向（滑铲方向）
    ///
    /// 只有勾了 faceTargetOnHit 的招式才会转，
    /// 普通平A 不该因为砍到背后的敌人就突然转身。
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

    private void OnDrawGizmos()
    {
        if (!showHitbox || !Application.isPlaying) return;
        if (!isHitboxActiveThisFrame || activeWindow == null) return;

        // 画【当前正在生效的那一段】，而不是 currentNode 的参数 ——
        // 连招推进后 currentNode 已经变了，用它画出来的框和实际判定对不上
        float dirX = (player != null) ? player.facingDirection : 1f;

        Vector2 finalOffset = new Vector2(activeWindow.offset.x * dirX, activeWindow.offset.y);
        Vector2 centerPos = (Vector2)transform.position + finalOffset;

        Gizmos.color = Color.green;
        Gizmos.DrawWireCube(centerPos, activeWindow.size);
    }
}

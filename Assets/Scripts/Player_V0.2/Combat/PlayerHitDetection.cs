using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 【攻击判定框】
///
/// ==========================================================
/// 【批次F 改动】
///
/// 1. 支持一招多段判定。
///    原先一个招式只有一个 hitbox 窗口，而且 TriggerAttackHitbox 重入时
///    会 StopCoroutine 掐断上一次 —— 想靠连打三次动画事件模拟三连斩，
///    结果是三次互相打断，只有最后一次生效。
///    现在一次动画事件就能跑完整个窗口序列。
///
/// 2. Gizmos 改为绘制【当前正在生效的那一段窗口】。
///    原先画的是 buffer.currentNode 的参数 ——
///    但连招推进后 currentNode 已经变了，画出来的框和实际判定对不上，
///    调判定框时会被误导。
///
/// 3. 移除旧输入系统 Input.GetKeyDown(F3)，改为条件编译保护。
/// ==========================================================
/// </summary>
public class PlayerHitDetection : MonoBehaviour
{
    private PlayerController player;

    [Header("Debug 设置")]
    public bool showHitbox = true;

    [Tooltip("按此键切换判定框显示。仅在旧输入系统可用时生效")]
    public KeyCode toggleKey = KeyCode.F3;

    [Header("命中设置")]
    [Tooltip("哪些图层算敌人。留空则退化为按 Tag 判断")]
    public LayerMask enemyLayer;

    [Tooltip("单次判定最多能命中多少个目标")]
    public int maxTargetsPerCheck = 16;

    private Coroutine activeHitboxCoroutine;
    private readonly HashSet<Collider2D> alreadyHitEnemies = new HashSet<Collider2D>();

    // 复用缓冲，避免每帧 OverlapBoxAll 产生 GC
    private readonly List<Collider2D> overlapResults = new List<Collider2D>(16);
    private ContactFilter2D overlapFilter;
    private readonly List<HitboxWindow> windowBuffer = new List<HitboxWindow>();

    // 当前正在生效的窗口（供 Gizmos 精确绘制）
    private HitboxWindow activeWindow;
    private bool isHitboxActiveThisFrame = false;

    void Awake()
    {
        player = GetComponent<PlayerController>();
        overlapFilter = ContactFilter2D.noFilter;
        overlapFilter.useTriggers = Physics2D.queriesHitTriggers;
    }

    void Update()
    {
#if ENABLE_LEGACY_INPUT_MANAGER
        // 条件编译保护：Active Input Handling 设为「Input System Package (New)」时
        // Input.GetKeyDown 会直接抛异常。加了这层保护后两种设置下都能编译运行。
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

        // 上一招的判定还在就强行掐断，并清空命中名单
        if (activeHitboxCoroutine != null) StopCoroutine(activeHitboxCoroutine);
        alreadyHitEnemies.Clear();

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
        // 取出本招式的所有判定窗口。
        // 没配多段的话，会自动用旧版单段参数合成一条 —— 已配好的资产无需改动。
        node.CollectWindows(windowBuffer);

        float sequenceTime = 0f;

        for (int i = 0; i < windowBuffer.Count; i++)
        {
            HitboxWindow window = windowBuffer[i];

            // 等到本段该开始的时刻
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

        int count = Physics2D.OverlapBox(boxCenter, window.size, 0f, overlapFilter, overlapResults);

        for (int i = 0; i < count; i++)
        {
            Collider2D hit = overlapResults[i];
            if (hit == null) continue;
            if (alreadyHitEnemies.Contains(hit)) continue;
            if (!hit.CompareTag("Enemy")) continue;

            alreadyHitEnemies.Add(hit);

            // 必须用 GetComponentInParent —— 判定框常挂在子物体上，
            // 直接 GetComponent 会砍中但不掉血
            EntityBase enemy = hit.GetComponentInParent<EntityBase>();

            if (enemy != null)
            {
                enemy.TakeDamage(new DamageInfo(
                    window.damage, DamageType.Melee, transform.position, gameObject));
            }
            else
            {
                Debug.LogWarning($"[判定] 砍中了 {hit.name}，但它和它的父节点上都没有 EntityBase", hit);
            }
        }
    }

    // ==========================================================
    // Gizmos
    // ==========================================================

    private void OnDrawGizmos()
    {
        if (!showHitbox || !Application.isPlaying) return;
        if (!isHitboxActiveThisFrame || activeWindow == null) return;

        // 画【当前正在生效的那一段】，而不是 buffer.currentNode 的参数。
        // 连招推进后 currentNode 已经变了，用它画出来的框和实际判定对不上。
        float dirX = (player != null) ? player.facingDirection : 1f;

        Vector2 finalOffset = new Vector2(activeWindow.offset.x * dirX, activeWindow.offset.y);
        Vector2 centerPos = (Vector2)transform.position + finalOffset;

        Gizmos.color = new Color(0f, 1f, 0f, 1f);
        Gizmos.DrawWireCube(centerPos, activeWindow.size);
    }
}
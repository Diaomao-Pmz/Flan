using UnityEngine;
using UnityEngine.InputSystem;
using Flandre.CombatSystem;

/// <summary>
/// 【虚拟手柄】—— 全项目唯一允许接触 Input System 的文件。
///
/// ==========================================================
/// 【批次C 改动】
///
/// 1. C 键从"只置一个 bool"升级为"置 bool + 发脉冲"。
///
///    为什么两者都要？
///      脉冲(InputCmd.Crouch) → 表达"按下的那一瞬间"，用于触发滑铲/蹲下，
///                              享受预输入宽恕
///      持续(isCrouchHeld)    → 表达"还按着"，用于判断能不能站起来
///    这两个语义不同，缺一不可。
///
/// 2. 位移指令（跳/冲/蹲铲）改为发给 PlayerCommandRouter，
///    不再发给 ComboInputBuffer。
///
///    原因：ComboInputBuffer 的本职是连招匹配，
///          它同时兼任位移指令分发是越权。现在各归各位：
///            攻击键 → ComboInputBuffer
///            位移键 → PlayerCommandRouter
/// ==========================================================
/// </summary>
[RequireComponent(typeof(Rigidbody2D), typeof(ComboInputBuffer))]
[RequireComponent(typeof(PlayerCommandRouter))]
public class PlayerController : MonoBehaviour
{
    [Header("硬件与核心脑部引用")]
    public Rigidbody2D rb { get; private set; }
    public SpriteRenderer sr { get; private set; }
    public Animator anim { get; private set; }

    public PlayerStateMachine stateMachine;
    public LoadoutManager loadoutManager;
    public ComboInputBuffer inputBuffer { get; private set; }
    public PlayerCommandRouter commandRouter { get; private set; }
    private Coroutine blinkCoroutine;

    [Header("虚拟手柄信号 (Virtual Gamepad)")]
    public Vector2 moveInput { get; private set; }
    public int facingDirection = 1;

    /// <summary>
    /// 鼠标/指针的屏幕坐标。由 PlayerAimProvider 转成世界方向。
    ///
    /// 【为什么放在这里】本文件是全项目唯一允许接触 Input System 的地方。
    /// 让瞄准组件自己去读 Mouse.current 的话，这条规矩就破了 ——
    /// 以后想支持手柄右摇杆瞄准、或者做输入录制回放，会发现输入源散落在好几处。
    /// </summary>
    public Vector2 screenAimPosition { get; private set; }

    // ==========================================
    // 持续按压与蓄力状态记录区
    // ==========================================
    public bool isJumpHeld { get; private set; }
    public bool isCrouchHeld { get; private set; }
    public bool isFlyHeld { get; private set; }

    public bool isMainAttackHeld { get; private set; }
    public float mainAttackHoldTime { get; private set; }
    public bool isMainChargeConsumed { get; private set; }

    public bool isSubAttackHeld { get; private set; }
    public float subAttackHoldTime { get; private set; }
    public bool isSubChargeConsumed { get; private set; }

    [Header("点按 / 长按判定")]
    [Tooltip(
        "按住超过这个时长就判定为「长按」，直接进入蓄力（不打出普攻）。\n\n" +
        "代价：普攻会有同等时长的输入延迟 —— 这是「同一个键区分点按与长按」\n" +
        "必须付出的账，调小可以更跟手但更容易把长按误判成点按。")]
    public float attackHoldThreshold = 0.15f;

    // 本次按压是否已经作出「点按 / 长按」的判定。
    // 一次按压只判定一次，判定完就不再重复触发。
    private bool mainHoldResolved;
    private bool subHoldResolved;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        sr = GetComponent<SpriteRenderer>();
        anim = GetComponent<Animator>();

        stateMachine = GetComponent<PlayerStateMachine>();
        loadoutManager = GetComponent<LoadoutManager>();
        inputBuffer = GetComponent<ComboInputBuffer>();
        commandRouter = GetComponent<PlayerCommandRouter>();
    }

    void Update()
    {
        PollPointer();

        bool isHit = (stateMachine != null && stateMachine.currentState == stateMachine.hitState);

        // 蓄力秒表走字 (双轨独立计时)
        if (!isHit)
        {
            if (isMainAttackHeld) mainAttackHoldTime += Time.deltaTime;
            if (isSubAttackHeld) subAttackHoldTime += Time.deltaTime;

            ResolveHoldIfNeeded();
        }
    }

    /// <summary>
    /// 每帧同步指针位置。
    ///
    /// 直接读 Mouse.current 而不是绑一个 Look Action，是为了少一步配置 ——
    /// 你不需要去 Input Actions 资产里新建 Action 再绑函数。
    /// 想改成 Action 驱动的话，调用下面的 OnLook 即可，两条路都留着。
    /// </summary>
    private void PollPointer()
    {
        var mouse = Mouse.current;
        if (mouse != null) screenAimPosition = mouse.position.ReadValue();
    }

    /// <summary>
    /// 【可选】如果你更想用 Input Actions 驱动指针（比如要支持触屏或手柄准星），
    /// 在资产里建一个 Pass Through / Vector2 的 Look 动作，绑到这个函数即可。
    /// </summary>
    public void OnLook(InputAction.CallbackContext ctx)
    {
        screenAimPosition = ctx.ReadValue<Vector2>();
    }

    // ==========================================
    // 位移指令 → 路由器
    // ==========================================

    public void OnMove(InputAction.CallbackContext ctx)
    {
        moveInput = ctx.ReadValue<Vector2>();
    }

    public void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.started)
        {
            isJumpHeld = true;
            commandRouter.OnCommand(InputCmd.Jump);
        }
        else if (ctx.canceled)
        {
            isJumpHeld = false;
        }
    }

    public void OnDashPerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.performed) commandRouter.OnCommand(InputCmd.Dash);
    }

    /// <summary>
    /// 滑铲 / 下蹲（共用 C 键）。
    ///
    /// 【批次C 改动】按下的瞬间额外发出 Crouch 脉冲。
    /// "变成铲还是变成蹲"由路由器判断 —— 那是决策层的事，不是输入层的事。
    /// </summary>
    public void OnCrouchSlidePerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.started)
        {
            isCrouchHeld = true;
            commandRouter.OnCommand(InputCmd.Crouch);
        }
        else if (ctx.canceled)
        {
            isCrouchHeld = false;
        }
    }

    [Header("Fly 键设置")]
    [Tooltip("按住不超过这个时长就松手，算作「点按」，会触发 fly 衍生")]
    public float flyTapThreshold = 0.2f;

    private float flyPressTime;

    /// <summary>
    /// 飞行键。有两路语义，各走各的通道：
    ///   长按 → isFlyHeld 布尔，供 Jump/Fall 的悬停蓄力读取
    ///   点按 → InputCmd.Fly 脉冲，供「打出 AAn 后跃起进飞行」的衍生使用
    ///
    /// 脉冲在【松手时】才发 —— 因为按下的那一刻还分不清是点按还是长按。
    /// 这是"同一个键两种用法"必然要付的代价：点按的响应会晚一个抬手的时间。
    /// </summary>
    public void OnFlyPerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.started)
        {
            isFlyHeld = true;
            flyPressTime = Time.time;
        }
        else if (ctx.canceled)
        {
            isFlyHeld = false;

            bool wasTap = (Time.time - flyPressTime) <= flyTapThreshold;
            if (wasTap) commandRouter.OnCommand(InputCmd.Fly);
        }
    }

    /// <summary>
    /// 主动技能键。
    ///
    /// 目前 PlayerInputConfig 里可能还没有这个 Action —— 需要你在
    /// Input Actions 资产里新建一个（建议绑 R 或 E），然后把这个函数绑上去。
    /// 不绑也不影响编译与运行。
    /// </summary>
    public void OnActiveSkillPerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.performed) loadoutManager?.TriggerActiveSkill();
    }

    // ==========================================
    // 攻击指令 → 连招引擎
    // ==========================================

    /// <summary>
    /// 【P1a 重写】攻击键改为「点按 / 长按」二选一。
    ///
    /// 按下的那一刻【什么都不做】—— 因为还分不清玩家想干嘛：
    ///   0.15 秒内松手  → 点按 → 打出普攻
    ///   0.15 秒后仍按着 → 长按 → 直接进蓄力（不打出普攻）
    ///
    /// 旧版是"按下立刻出普攻，动画放完还按着才进蓄力"（效仿空洞骑士），
    /// 新规则把那一发普攻删掉了，用这段判定延迟来分离两种意图。
    ///
    /// 代价是普攻有 0.15 秒输入延迟，这是同一个键承担两种用法必然要付的账。
    /// </summary>
    public void OnMainAttackPerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.started)
        {
            isMainAttackHeld = true;
            mainAttackHoldTime = 0f;
            mainHoldResolved = false;
        }
        else if (ctx.canceled)
        {
            isMainAttackHeld = false;

            if (!mainHoldResolved)
            {
                // 还没到长按阈值就松手 → 点按 → 普攻
                mainHoldResolved = true;
                inputBuffer.OnAttackTap(InputCmd.MainAttack);
            }
            else
            {
                // 已判定为长按 → 蓄力的释放由 ChargeState 主导（它才知道蓄到几级），
                // 这里只清掉预输入缓存，免得松手后又跑出来打一发普攻
                inputBuffer.OnAttackReleased(InputCmd.MainAttack);
            }
        }
    }

    public void OnSubAttackPerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.started)
        {
            isSubAttackHeld = true;
            subAttackHoldTime = 0f;
            subHoldResolved = false;
        }
        else if (ctx.canceled)
        {
            isSubAttackHeld = false;

            if (!subHoldResolved)
            {
                subHoldResolved = true;
                inputBuffer.OnAttackTap(InputCmd.SubAttack);
            }
            else
            {
                inputBuffer.OnAttackReleased(InputCmd.SubAttack);
            }
        }
    }

    /// <summary>
    /// 按住超过阈值时，判定为长按并通知连招引擎进入蓄力。
    /// 一次按压只判定一次。
    /// </summary>
    private void ResolveHoldIfNeeded()
    {
        if (isMainAttackHeld && !mainHoldResolved
            && mainAttackHoldTime >= attackHoldThreshold)
        {
            mainHoldResolved = true;
            inputBuffer.OnAttackHold(InputCmd.MainAttack);
        }

        if (isSubAttackHeld && !subHoldResolved
            && subAttackHoldTime >= attackHoldThreshold)
        {
            subHoldResolved = true;
            inputBuffer.OnAttackHold(InputCmd.SubAttack);
        }
    }

    // 【批次J】蓄力不再「自动放」，因此这两个「已消耗」标记失去了原本的用途。
    // 保留是为了不破坏可能存在的外部调用，新代码不要再用。
    [System.Obsolete("批次J 起蓄力改为松手释放，不再需要消耗标记")]
    public void ConsumeMainCharge() { isMainChargeConsumed = true; }

    [System.Obsolete("批次J 起蓄力改为松手释放，不再需要消耗标记")]
    public void ConsumeSubCharge() { isSubChargeConsumed = true; }

    // ==========================================
    // 杂项
    // ==========================================

    public void SetFacingDirection(int dir)
    {
        facingDirection = dir;

        // 用 Transform 旋转而非 flipX：所有子物体（特效、判定框）自动跟着翻
        if (dir == 1) transform.rotation = Quaternion.Euler(0, 0, 0);
        else if (dir == -1) transform.rotation = Quaternion.Euler(0, 180, 0);
    }

    public void StartBlink(float duration, float interval)
    {
        if (blinkCoroutine != null) StopCoroutine(blinkCoroutine);
        blinkCoroutine = StartCoroutine(BlinkRoutine(duration, interval));
    }

    private System.Collections.IEnumerator BlinkRoutine(float duration, float interval)
    {
        float elapsed = 0f;
        if (interval <= 0) interval = 0.1f;

        while (elapsed < duration)
        {
            sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, 0f);
            yield return new WaitForSeconds(interval);
            elapsed += interval;

            sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, 1f);
            yield return new WaitForSeconds(interval);
            elapsed += interval;
        }
        sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, 1f);
    }
}

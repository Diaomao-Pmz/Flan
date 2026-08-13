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
        bool isHit = (stateMachine != null && stateMachine.currentState == stateMachine.hitState);

        // 蓄力秒表走字 (双轨独立计时)
        if (!isHit)
        {
            if (isMainAttackHeld) mainAttackHoldTime += Time.deltaTime;
            if (isSubAttackHeld) subAttackHoldTime += Time.deltaTime;
        }
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

    public void OnFlyPerformed(InputAction.CallbackContext ctx)
    {
        // 只供 JumpState / FallState / FlyState 读取布尔值，不发脉冲
        if (ctx.started) isFlyHeld = true;
        else if (ctx.canceled) isFlyHeld = false;
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

    public void OnMainAttackPerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.started)
        {
            isMainAttackHeld = true;
            isMainChargeConsumed = false;
            mainAttackHoldTime = 0f;
            inputBuffer.OnReceiveInput(InputCmd.MainAttack);
        }
        else if (ctx.canceled)
        {
            isMainAttackHeld = false;

            if (!isMainChargeConsumed)
            {
                inputBuffer.OnReceiveChargeRelease(InputCmd.MainAttack, mainAttackHoldTime);
            }
        }
    }

    public void OnSubAttackPerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.started)
        {
            isSubAttackHeld = true;
            isSubChargeConsumed = false;
            subAttackHoldTime = 0f;
            inputBuffer.OnReceiveInput(InputCmd.SubAttack);
        }
        else if (ctx.canceled)
        {
            isSubAttackHeld = false;

            if (!isSubChargeConsumed)
            {
                inputBuffer.OnReceiveChargeRelease(InputCmd.SubAttack, subAttackHoldTime);
            }
        }
    }

    public void ConsumeMainCharge() { isMainChargeConsumed = true; }
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

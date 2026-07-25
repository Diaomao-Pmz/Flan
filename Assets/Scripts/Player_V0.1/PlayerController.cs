using UnityEngine;
using UnityEngine.InputSystem;
using Flandre.CombatSystem;

[RequireComponent(typeof(Rigidbody2D), typeof(ComboInputBuffer))]
public class PlayerController : MonoBehaviour
{
    [Header("硬件与核心脑部引用")]
    public Rigidbody2D rb { get; private set; }
    public SpriteRenderer sr { get; private set; }
    public Animator anim { get; private set; }

    public PlayerStateMachine stateMachine;
    public LoadoutManager loadoutManager;
    public ComboInputBuffer inputBuffer { get; private set; }
    private Coroutine blinkCoroutine;

    [Header("虚拟手柄信号 (Virtual Gamepad)")]
    public Vector2 moveInput { get; private set; }
    public int facingDirection = 1;

    // ==========================================
    // 持续按压与蓄力状态记录区
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
    }

    void Update()
    {
        bool isHit = (stateMachine != null && stateMachine.currentState == stateMachine.hitState);

        // ==========================================
        // 蓄力秒表走字逻辑 (双轨独立计时)
        // ==========================================
        if (!isHit)
        {
            if (isMainAttackHeld) mainAttackHoldTime += Time.deltaTime;
            if (isSubAttackHeld) subAttackHoldTime += Time.deltaTime;
        }
    }

    // ==========================================
    // 动作系统回调 (对接 Player Input Events)
    // ==========================================
    public void OnMove(InputAction.CallbackContext ctx)
    {
        moveInput = ctx.ReadValue<Vector2>();
    }

    public void OnJumpPerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.started) // 刚按下的瞬间
        {
            isJumpHeld = true;

            // 向大脑发送常规跳跃脉冲指令
            inputBuffer.OnReceiveInput(InputCmd.Jump);
        }
        else if (ctx.canceled) // 手指松开的瞬间
        {
            isJumpHeld = false;
        }
    }

    public void OnDashPerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.performed) inputBuffer.OnReceiveInput(InputCmd.Dash);
    }

    // 滑铲 / 下蹲 (共用键)
    public void OnCrouchSlidePerformed(InputAction.CallbackContext ctx)
    {
        if (ctx.started) isCrouchHeld = true;
        else if (ctx.canceled) isCrouchHeld = false;
    }

    // 【新增】：飞行专用监听！
    public void OnFlyPerformed(InputAction.CallbackContext ctx)
    {
        // 只需要供 FallState 和 FlyState 读取布尔值，不向 Buffer 发脉冲
        if (ctx.started) isFlyHeld = true;
        else if (ctx.canceled) isFlyHeld = false;
    }

    // ==========================================
    // 主/副攻击的瞬间触发与蓄力释放
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
        // 1. 记录朝向数据 (-1 或 1)
        facingDirection = dir;

        // 2. 彻底抛弃 sr.flipX！改用 Transform 的 3D 旋转
        if (dir == 1)
        {
            // 面朝右：Y轴旋转归零。所有子物体恢复默认位置。
            transform.rotation = Quaternion.Euler(0, 0, 0);
        }
        else if (dir == -1)
        {
            // 面朝左：Y轴旋转180度。这会带着所有子物体（特效、判定框）完美翻转到另一边！
            transform.rotation = Quaternion.Euler(0, 180, 0);
        }
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
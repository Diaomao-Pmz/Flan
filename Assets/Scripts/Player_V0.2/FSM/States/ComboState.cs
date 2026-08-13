using UnityEngine;

/// <summary>
/// 连招执行状态。
///
/// 【批次E 改动】空中连招的「速度锁死」挪到 FixedUpdate。
///
/// 原先写在 Update 里的 rb.linearVelocity = Vector2.zero，
/// 在低帧率时会出现「物理步已经加过重力、但 Update 还没来得及归零」的空档，
/// 表现为空中挥剑时角色轻微下坠一小截。挪到固定步长后，每个物理步都锁得住。
/// </summary>
public class ComboState : PlayerStateBase
{
    public bool isCancelable = false;

    private float originalGravity;
    private bool wasAirborneOnEnter;

    // 缓存引用，避免每帧 GetComponent
    private ComboInputBuffer buffer;
    private PlayerHitDetection hitDetection;

    public ComboState(PlayerStateMachine stateMachine) : base(stateMachine) { }

    private ComboInputBuffer Buffer
        => buffer != null ? buffer : (buffer = sm.GetComponent<ComboInputBuffer>());

    private PlayerHitDetection HitDetection
        => hitDetection != null ? hitDetection : (hitDetection = sm.GetComponent<PlayerHitDetection>());

    public override void Enter()
    {
        isCancelable = false;
        originalGravity = sm.rb.gravityScale;

        wasAirborneOnEnter = !sm.IsGrounded();

        // 空中连段反重力悬停：把角色钉在空中，防止挥剑时诡异下滑
        if (wasAirborneOnEnter)
        {
            sm.rb.gravityScale = 0f;
            sm.rb.linearVelocity = Vector2.zero;
        }

        ComboNode activeNode = Buffer.currentNode;

        if (activeNode != null)
        {
            sm.anim.Play(activeNode.animName, 0, 0f);

            // 地面招式的突进位移
            if (!wasAirborneOnEnter)
            {
                float dir = sm.playerController.facingDirection;
                sm.rb.linearVelocity = new Vector2(dir * activeNode.forwardThrust.x, sm.rb.linearVelocity.y);
            }
        }
    }

    public override void Update()
    {
        // 后摇取消检测
        if (isCancelable)
        {
            if (Buffer.TryAdvanceCombo()) return;
        }

        // 攻击中允许微调朝向（瞄准/修招式朝向）
        float moveDir = sm.playerController.moveInput.x;
        if (Mathf.Abs(moveDir) > 0.1f)
        {
            UpdateFacing(moveDir);
        }
    }

    public override void FixedUpdate()
    {
        // 空中连招期间锁死速度，不接受重力叠加
        if (!sm.IsGrounded())
        {
            sm.rb.linearVelocity = Vector2.zero;
        }
    }

    public override void Exit()
    {
        HitDetection?.ForceStopHitbox();
        sm.rb.gravityScale = originalGravity;
    }
}

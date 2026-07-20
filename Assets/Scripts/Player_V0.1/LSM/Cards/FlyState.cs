using UnityEngine;

public class FlyState : IState
{
    private PlayerStateMachine sm;
    private float originalGravity;
    private float manaAccumulator;

    // 【新增】：释放锁，用来完美模拟 GetKeyDown
    private bool requireFlyRelease = false;

    public FlyState(PlayerStateMachine stateMachine)
    {
        sm = stateMachine;
    }

    public void Enter()
    {
        Debug.Log("进入了：飞行状态");
        originalGravity = sm.rb.gravityScale;
        sm.rb.gravityScale = 0f;
        sm.rb.linearVelocity = Vector2.zero;
        manaAccumulator = 0f;
        sm.anim.Play("Flandre_Fly");

        // 一切入飞行，立刻上锁，要求玩家先松开 Q 键
        requireFlyRelease = sm.playerController.isFlyHeld;
    }

    public void Update()
    {
        // 耗蓝逻辑
        manaAccumulator += sm.flyManaCostPerSecond * Time.deltaTime;
        if (manaAccumulator >= 1f)
        {
            int cost = Mathf.FloorToInt(manaAccumulator);
            manaAccumulator -= cost;

            bool hasMana = sm.playerState.health.ConsumeMP(cost);
            if (!hasMana)
            {
                sm.ChangeState(sm.fallState);
                return;
            }
        }

        // ==========================================
        // 【核心修改】：利用释放锁机制替代 Input.GetKeyDown
        // ==========================================
        if (requireFlyRelease)
        {
            // 玩家终于松开了 Q 键，解除锁定
            if (!sm.playerController.isFlyHeld) requireFlyRelease = false;
        }
        else if (sm.playerController.isFlyHeld)
        {
            // 锁定解除后，玩家再次按下了 Q 键，触发取消飞行！
            Debug.Log("[FlyState] 玩家主动取消飞行！");
            sm.ChangeState(sm.fallState);
            sm.rb.linearVelocity = new Vector2(sm.rb.linearVelocity.x, sm.flyCancelJumpForce);
            return;
        }

        // ==========================================
        // 【修改】：使用虚拟手柄获取八向移动
        // ==========================================
        Vector2 moveDir = sm.playerController.moveInput.normalized;
        sm.rb.linearVelocity = moveDir * sm.flySpeed;

        if (moveDir.x < 0) sm.playerController.SetFacingDirection(-1);
        else if (moveDir.x > 0) sm.playerController.SetFacingDirection(1);
    }

    public void Exit()
    {
        sm.rb.gravityScale = originalGravity;
    }
}
using UnityEngine;
using Flandre.CombatSystem;

public class DashState : IState
{
    private PlayerStateMachine sm;
    private float dashTimer;
    private float originalGravity;
    private bool isFirstHit;

    // 【必须保留】防 CD 递归污染标记
    private bool isRejected;

    private readonly int playerLayer = LayerMask.NameToLayer("Player");
    private readonly int invincibleLayer = LayerMask.NameToLayer("Invincible");

    public DashState(PlayerStateMachine stateMachine) { sm = stateMachine; }

    public void Enter()
    {
        isRejected = false;
        sm.playerController.loadoutManager.ExecuteActionModifier(ActionType.Dash);

        if (!sm.dashSkill.CanExecute())
        {
            Debug.LogWarning("[Dash] CD或硬直中！拦截执行");
            isRejected = true;
            HandleFallback();
            return;
        }

        isFirstHit = (sm.dashSkill.currentCombo == 0);
        sm.dashSkill.Execute();

        // ==========================================
        // 【逻辑分流 1】：如果是 Relay 的第二段，强制触发传送并退出
        // ==========================================
        if (!isFirstHit && sm.isDashRelay)
        {
            sm.transform.position = sm.dashAnchorPos;
            sm.rb.linearVelocity = Vector2.zero;
            Debug.Log("[Dash] Relay 传送触发！");

            isRejected = true; // 传送后不需要算CD和重置参数
            HandleFallback();
            return;
        }

        // ==========================================
        // 【逻辑分流 2】：常规冲刺逻辑 (包含首冲，以及 Echo 的二冲)
        // ==========================================

        // 只有在 Relay 的第一段，才需要留下锚点和开启无敌
        if (isFirstHit && sm.isDashRelay)
        {
            sm.dashAnchorPos = sm.transform.position;

            sm.playerState.health.SetUntargetable(true);//玩家无敌
            sm.gameObject.layer = invincibleLayer;//玩家可穿过敌人

            if (sm.anchorPrefab != null)
                sm.activeDashAnchor = GameObject.Instantiate(sm.anchorPrefab, sm.dashAnchorPos, Quaternion.identity);
        }

        // ----- 正常冲刺的物理与视觉表现 -----
        Debug.Log("进入了：冲刺状态");
        if (sm.dashTrail != null) sm.dashTrail.emitting = true;

        dashTimer = sm.dashDuration;
        originalGravity = sm.rb.gravityScale;
        sm.rb.gravityScale = 0f;

        float direction = sm.GetComponent<SpriteRenderer>().flipX ? -1f : 1f;
        sm.rb.linearVelocity = new Vector2(direction * sm.dashSpeed, 0f);
    }

    public void Update()
    {
        dashTimer -= Time.deltaTime;
        if (dashTimer <= 0)
        {
            sm.rb.gravityScale = originalGravity;
            HandleFallback();
        }
    }

    public void Exit()
    {
        if (isRejected) return;

        // 退出无敌
        if (sm.isDashRelay)
        {
            sm.gameObject.layer = playerLayer;
            sm.playerState.health.SetUntargetable(false);
        }

        sm.dashSkill.StartCooldownIfFirstHit(isFirstHit);

        if (sm.dashTrail != null) sm.dashTrail.emitting = false;
        sm.rb.gravityScale = originalGravity;
    }

    private void HandleFallback()
    {
        if (!sm.IsGrounded()) sm.ChangeState(sm.fallState);
        else if (Mathf.Abs(sm.rb.linearVelocity.x) > 0.1f || Input.GetAxisRaw("Horizontal") != 0) sm.ChangeState(sm.runState);
        else sm.ChangeState(sm.idleState);
    }
}
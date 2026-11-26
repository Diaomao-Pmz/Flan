using System;
using System.Collections;
using UnityEngine;
using Flandre.CombatSystem;

/// <summary>
/// 【Boss 演出导演】挂在 Boss 身上，负责播放演出卡（BossCinematic）。
///
/// 开始时：Boss 切到演出状态（AI 不做决定），按卡上的开关锁玩家输入 / 给无敌，
///         暂停回盾倒计时。
/// 逐步执行卡上的步骤。
/// 结束时：一切还原，招式 / 套路 / 反应冷却顺延演出的时长，镜头还原（如果配了镜头），
///         Boss 回到移动状态（套路进度保留）。
/// Boss 死亡时立刻结束、一切还原（BossController.Die 调 Abort）；其他情况都不打断演出。
/// </summary>
[RequireComponent(typeof(BossController))]
public class BossCinematicDirector : MonoBehaviour
{
    [Header("--- 演出卡 ---")]
    [Tooltip("进入二阶段时播放的演出。\n" +
             "留空 = 原来的做法：瞬间传送到场地中央，马上开始二阶段。\n" +
             "拖了以后传送不再自动做，要传送就在演出卡里放一步「出招：Center 传送卡」")]
    [SerializeField] private BossCinematic phase2Cinematic;

    [Header("--- 镜头（预留，还没有镜头步骤）---")]
    [Tooltip("实现了 IBossCinematicCamera 的镜头控制组件。演出结束时会调用它的 RestoreDefault。现在留空即可")]
    [SerializeField] private MonoBehaviour cameraRig;

    /// <summary>进入二阶段时播放的演出（可能为空）</summary>
    public BossCinematic Phase2Cinematic => phase2Cinematic;

    /// <summary>镜头控制（预留，可能为空）</summary>
    public IBossCinematicCamera CameraRig => cameraRig as IBossCinematicCamera;

    /// <summary>正在播演出</summary>
    public bool IsPlaying { get; private set; }

    /// <summary>正在播的演出卡</summary>
    public BossCinematic Current { get; private set; }

    /// <summary>此刻 Boss 是否因演出无敌</summary>
    public bool BossInvulnerable => IsPlaying && Current != null && Current.bossInvulnerable;

    /// <summary>演出开始 / 结束（含被中止）时广播。以后的镜头、UI、音效可以订阅</summary>
    public event Action<BossCinematic> CinematicStarted;
    public event Action<BossCinematic> CinematicEnded;

    private BossController boss;
    private Coroutine routine;
    private float startTime;
    private IBossActionExecutor runningExecutor;

    private PlayerController lockedPlayer;
    private PlayerHealth invulnerablePlayer;

    private void Awake()
    {
        boss = GetComponent<BossController>();
    }

    private void OnValidate()
    {
        if (cameraRig != null && !(cameraRig is IBossCinematicCamera))
        {
            Debug.LogWarning($"[BossCinematicDirector] {cameraRig.GetType().Name} 没有实现 IBossCinematicCamera，已清空。", this);
            cameraRig = null;
        }
    }

    private void OnDisable()
    {
        Abort("导演组件被禁用");
    }

    // ==========================================================
    //  播放 / 中止
    // ==========================================================

    /// <summary>
    /// 播放一张演出卡。正在播别的演出时先中止它。
    /// 演完 Boss 回到移动状态。卡为空返回 false（什么都不做）
    /// </summary>
    public bool Play(BossCinematic cinematic)
    {
        if (cinematic == null || boss == null) return false;
        if (IsPlaying) Abort($"被「{cinematic.name}」顶替");

        // 先切状态再接管：已经在演出状态（刚中止了上一段）就不用再切，免得离开演出状态时把新演出也中止掉
        if (boss.CurrentState != boss.CinematicState) boss.ChangeState(boss.CinematicState);

        Begin(cinematic);
        routine = StartCoroutine(RunSteps(cinematic));
        return true;
    }

    /// <summary>立刻结束正在播的演出并还原一切（不切 Boss 状态，由调用方决定）</summary>
    public void Abort(string reason)
    {
        if (!IsPlaying) return;

        Debug.Log($"[演出] 「{Current.name}」中止：{reason}");

        if (routine != null)
        {
            StopCoroutine(routine);
            routine = null;
        }
        if (runningExecutor != null)
        {
            runningExecutor.Cancel();
            runningExecutor = null;
        }
        End();
    }

    private IEnumerator RunSteps(BossCinematic cinematic)
    {
        Debug.Log($"[演出] 开始「{cinematic.name}」（{cinematic.steps.Count} 步）");

        for (int i = 0; i < cinematic.steps.Count; i++)
        {
            CinematicStep step = cinematic.steps[i];
            if (step == null) continue;

            Debug.Log($"[演出] 「{cinematic.name}」第 {i + 1} 步：{step.Describe()}");
            boss.SetMovementStatus($"[演出] {cinematic.name} · {step.Describe()}");

            // 直接 yield return IEnumerator：内联进本协程，Abort 停掉本协程时步骤也一起停
            yield return step.Run(this);
        }

        routine = null;
        Debug.Log($"[演出] 「{cinematic.name}」结束");
        End();

        // 演完回到移动状态，接着原来的流程（套路进度还在）
        if (boss.CurrentState == boss.CinematicState) boss.ChangeState(boss.MoveState);
    }

    // ==========================================================
    //  步骤用的工具
    // ==========================================================

    /// <summary>出招步骤：找到认领这张卡的执行器，演完它</summary>
    public IEnumerator RunAction(ActionNode node)
    {
        if (node == null) yield break;

        IBossActionExecutor executor = boss.GetExecutorFor(node);
        if (executor == null)
        {
            Debug.LogError($"[演出] 没有执行器认领 {node.GetType().Name}（卡片: {node.name}），这一步跳过。", this);
            yield break;
        }

        boss.StopHorizontal();
        runningExecutor = executor;
        yield return executor.Execute(node, boss.Context);
        runningExecutor = null;
        boss.StopHorizontal();
    }

    /// <summary>播动画步骤</summary>
    public void PlayAnim(string stateName)
    {
        if (boss.animator == null || string.IsNullOrEmpty(stateName)) return;
        boss.animator.Play(stateName);
    }

    // ==========================================================
    //  开始 / 结束时的接管与还原
    // ==========================================================

    private void Begin(BossCinematic cinematic)
    {
        IsPlaying = true;
        Current = cinematic;
        startTime = Time.time;

        boss.StopHorizontal();
        if (boss.bossState != null) boss.bossState.timersPaused = true;

        PlayerState player = boss.Context.playerState;
        if (cinematic.lockPlayerInput && player != null)
        {
            lockedPlayer = player.GetComponent<PlayerController>();
            if (lockedPlayer != null) lockedPlayer.SetInputLocked(true);
        }
        if (cinematic.playerInvulnerable && player != null && player.health != null)
        {
            invulnerablePlayer = player.health;
            invulnerablePlayer.RequestUntargetable(InvulnerableTokens.Cinematic);
        }

        CinematicStarted?.Invoke(cinematic);
    }

    private void End()
    {
        BossCinematic ended = Current;
        float duration = Time.time - startTime;

        if (lockedPlayer != null)
        {
            lockedPlayer.SetInputLocked(false);
            lockedPlayer = null;
        }
        if (invulnerablePlayer != null)
        {
            invulnerablePlayer.ReleaseUntargetable(InvulnerableTokens.Cinematic);
            invulnerablePlayer = null;
        }

        if (boss != null)
        {
            if (boss.bossState != null) boss.bossState.timersPaused = false;
            if (boss.AI != null) boss.AI.ShiftTimers(startTime, duration); // 冷却顺延演出的时长
        }

        CameraRig?.RestoreDefault(0f);

        IsPlaying = false;
        Current = null;

        CinematicEnded?.Invoke(ended);
    }
}

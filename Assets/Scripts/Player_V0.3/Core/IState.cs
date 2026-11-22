/// <summary>
/// 状态卡带接口。
///
/// 【批次D 改动】新增 FixedUpdate。
///
/// 为什么需要它：所有 rb.linearVelocity = ... 目前都写在 Update（渲染帧）里。
/// 对讲究帧数精度的硬核 ACT 来说这是硬伤 ——
///   144Hz 与 60Hz 下跳跃高度、冲刺距离会有细微差异
///   开 Rigidbody Interpolation 时会抖动
///   玩家掉帧时手感会飘
///
/// 本批次【只加接口，不搬代码】。物理写入的实际迁移放在下一批单独做，
/// 这样一旦手感有变化，能立刻确定是时序改动导致的，而不是这次的结构搬家。
///
/// 各状态不需要逐个实现空的 FixedUpdate —— 继承 PlayerStateBase 即可拿到默认空实现。
/// </summary>
public interface IState
{
    void Enter();

    /// <summary>渲染帧。负责逻辑判断：该不该切状态、动画播哪个</summary>
    void Update();

    /// <summary>物理帧。负责速度写入（下一批开始使用）</summary>
    void FixedUpdate();

    void Exit();
}

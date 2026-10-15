using System;
using System.Collections;

/// <summary>
/// Boss 技能执行器的统一契约（「格口」）。
///
/// 【为什么需要它】
/// 原先 BossActionExecuter.DoCombat() 里是一串 if (node is XNode) 分支 ——
/// Boss 大脑必须亲自认识每一个部门，每加一种技能就得回来改这个文件一次。
///
/// 改成注册表之后，大脑只做分拣：查字典找到认领这种卡的执行器，把卡递过去。
/// 它**完全不需要知道存在哪些执行器**。
///
/// 加一个新技能的完整步骤：
///   1. 新建一个 ActionNode 派生的 SO（配参数）
///   2. 新建一个实现本接口的 MonoBehaviour（写逻辑），挂到 Boss 身上
///   3. 在 Inspector 里建一张卡塞进 AI 卡池
/// 现有文件零修改 —— 这就是开闭原则。
/// </summary>
public interface IBossActionExecutor
{
    /// <summary>
    /// 我认领哪一种卡。例如 typeof(LaserNode)。
    /// BossController 在 Awake 时据此建立 Node 类型 → 执行器的映射。
    /// 一种 Node 类型只能有一个执行器认领，重复认领会被判为配置错误并报错。
    /// </summary>
    Type NodeType { get; }

    /// <summary>
    /// 执行这张卡，直到整套动作演完（含前摇、判定、后摇）。
    /// 由 BossActionExecuter 驱动并等待其结束，结束后自动回到 MoveState。
    ///
    /// 实现须知：
    /// - 不要在这里切换状态机，交给调用方统一处理，避免状态流转分散在各处。
    /// - 传入的 node 保证不为 null，且其运行时类型与 NodeType 一致，可直接强转。
    /// - 中途可能被打断（破盾 / 转阶段），届时调用方会先停协程再调 Cancel()。
    /// </summary>
    IEnumerator Execute(ActionNode node, BossContext ctx);

    /// <summary>
    /// 强制收摊。破盾、转阶段、Boss 死亡时由调用方统一触发。
    ///
    /// 【为什么必须有】原先 BossActionExecuter.Exit() 里写死了
    /// boss.BulletEmitter.StopAttack() —— 只清理弹幕。等有了激光和冲撞，
    /// 打断时它们不会被停掉，会出现「Boss 晕倒了激光还在照」的 Bug。
    ///
    /// 实现须知：必须可重入 —— 未在执行中时调用它应当是安全的空操作。
    /// </summary>
    void Cancel();
}
namespace Flandre.CombatSystem
{
    /// <summary>
    /// 宝石可以插进去的四个槽位。
    ///
    /// 前三个对应基础动作（跳/冲/铲），第四个是主动技能。
    /// 按设计：四颗宝石选三颗装在动作上，剩下的那一颗装进 Active 槽当大招。
    ///
    /// 【为什么不直接复用 ActionType？】
    /// 因为 Active 不是一个"动作状态"，它没有对应的状态卡带。
    /// 硬塞进 ActionType 会让所有 switch 都多出一个永远走不到的分支。
    /// </summary>
    public enum GemSlot
    {
        Jump,
        Dash,
        Slide,
        Active
    }

    /// <summary>
    /// 宝石对一次动作的裁决结果。
    ///
    /// 这是整个宝石系统里最关键的设计点 —— 有了返回值，
    /// 状态卡带就不需要认识任何一颗具体的宝石了。
    ///
    /// 比喻：原先的状态卡带像一个必须认识每一位客人的门卫，
    ///       来一个新客人就得培训他一遍（加一个 if 分支）。
    ///       现在门卫只做一件事 —— "请出示通行证"，
    ///       证上写着放行 / 自己进 / 不许进。门卫永远不需要重新培训。
    /// </summary>
    public enum GemActionResult
    {
        /// <summary>放行。我只是加点料，你继续执行默认逻辑（例：Echo 只改段数上限）</summary>
        Normal,

        /// <summary>我接管了，默认逻辑别跑（例：Relay 第二段是传送，不是冲刺）</summary>
        Override,

        /// <summary>这次动作不该发生，请回退到 Idle/Run/Fall</summary>
        Reject
    }
}

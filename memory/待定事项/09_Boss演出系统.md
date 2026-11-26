# Boss 演出系统（演出导演 + 演出卡）

> 提出：2026-11-26。用户同意先记进待定事项，开工时再细化改动方案。

> **11-26 第一版已完成（只做了语法检查，等用户在 Unity 里测）。还没做的只剩镜头步骤（第二版），见文末。**

## 第一版做了什么（11-26）
- 演出卡 `Boss/2_DataAssets/BossCinematic/BossCinematic.cs`（菜单 Flandre → Boss → Cinematic）：
  - 整场开关：Boss 无敌（默认开）、锁玩家输入（默认关）、玩家无敌（默认关）。用户决定**不做清子弹**。
  - 步骤（`[SerializeReference, SubclassSelector] List<CinematicStep>`）：出招（任意 ActionNode，走现有执行器，跟动作倍率）、等待（真实时间，不跟动作倍率）、播动画。
  - 预留镜头接口 `IBossCinematicCamera`（SwitchTo / RestoreDefault）；`SubclassNameAttribute` 给下拉框起中文名。
- 演出导演 `Boss/BossLogic/Execution/Cinematic/BossCinematicDirector.cs`（挂 Boss 身上）：Phase 2 Cinematic 栏位、Camera Rig 栏位（预留）、`CinematicStarted / CinematicEnded` 事件；开始时接管，结束时还原 + `AI.ShiftTimers` 冷却顺延 + 镜头还原。
- 演出状态 `Boss/BossLogic/BossLogic/StateCard/BossCinematicState.cs`：什么都不做；被别的逻辑切走时中止演出。
- 接入点：`BossController`（转阶段先看演出卡；无敌时 TakeDamage 直接返回；演出中不打断不击退、不受击传送、破盾不眩晕；死亡中止演出）、`BossState.timersPaused`（回盾暂停）、`BossCombatState.Finish`（招式卡 `cinematicAfter`）、`ActionNode.cinematicAfter`、`SubclassSelectorDrawer`（支持列表元素、显示 SubclassName）、`PlayerController.SetInputLocked`、`InvulnerableTokens.Cinematic`。
- 规则：死亡结束演出；其他情况都不打断。演出中再触发二阶段 → 中止当前演出，转阶段优先。拼刀停顿和 cinematicAfter 同时有时拼刀停顿优先（被拼掉的招不算正常放完）。

## 想要的效果
- Boss 的「演出」和战斗逻辑分开：转二阶段传送到场地中央并停顿、放完某些特殊招后进入带运镜的连段，这类有演出效果的逻辑单独管理。
- 战斗逻辑只负责喊「该演哪一场了」，不关心怎么演。

## 定下的方向（11-26 讨论，用户回 OK）
分三层：
1. **角色被接管的状态**：Boss 状态机加一个「演出状态」（AI 不做决定、执行器不自己跑，只听演出指令）；玩家加一个「被锁定」开关（关输入，通常同时无敌）。
2. **演出导演组件**：统一播放演出。开始前接管 Boss / 玩家 / 镜头，按剧本执行，结束后归还控制权并通知战斗逻辑「演完了」。触发时机：转二阶段、某张招式卡放完、Boss 死亡等。
3. **剧本**两种写法：
   - **步骤列表式的演出卡**（数据资产，一串步骤）：战斗中的短演出用。例：传送到中央 → 无敌 → 镜头拉近 → 放动画 → 等 2 秒 → 出某张招式卡 → 镜头还原 → 解除无敌。和招式卡 / 套路同一个思路。
   - **Unity Timeline**：开场登场、死亡过场等长演出用（工程已装 Timeline 和 Cinemachine）。导演两种都能播。

Claude 的建议：**先做「Boss 演出状态 + 演出卡 + 演出导演」**，Timeline 等真有过场时再接。

## 第一个用例：转二阶段
- 现在的逻辑在 `Boss/BossLogic/BossLogic/BossController.cs` 的 `HandlePhase2()`：收掉所有招 → 停住 → 传送到中央（`BAE_Teleporter.ExecuteTeleport(Center)`）→ `AI.SwitchToPhase2()` → 回满护盾（没护盾的 Boss 跳过）→ 立刻回到走位。**没有停顿。**
- 触发在 `BossState.cs` 的 `BossMechanic.TakeDamage()`（血量 ≤ `phase2Threshold` → `OnPhase2Triggered`）。
- 计划把这几步改成一张「转阶段演出卡」，顺便加上「到中央后停顿几秒」。

## 演出导演要顺带处理的事
- 冻结时间：回盾倒计时、招式冷却、近身反应冷却在演出期间暂停。
- 清场：要不要清掉场上的子弹。
- 无敌：Boss / 玩家演出期间是否无敌。
- 跳过：以后要不要允许跳过。
- 镜头复位：演完平滑切回跟随镜头。
- 演出里的「出招」步骤直接调用现有招式执行器，伤害 / 拼刀 / 击退规则和平时一致。

## 开工前要和用户定的（11-26 已定）
1. 演出卡上开关决定，默认 Boss 无敌。
2. 等待用真实时间，不跟动作倍率；出招步骤照常跟动作倍率。
3. 有「播动画」步骤；镜头第二版做，先留接口。

## 相关的现有代码
- `Scripts/Cinemachine/CameraSequenceController.cs`：早期的演出导演原型（远景 → 近景 → 黑屏 → 切回玩家），步骤写死在代码里；定身主角那行是随便取玩家身上第一个 MonoBehaviour 关掉（注释说要换成状态机脚本名）。做演出系统时可以把镜头 / 黑屏部分收编成「镜头步骤」。
- `Scripts/Cinemachine/CS_Trigger.cs`（类名 `CameraSwitchTrigger`）：走进区域切镜头。

## 第二版（还没做）：镜头步骤
- 前提：用户在 Boss 场景里搭 Cinemachine（主摄像机加 Cinemachine Brain + 几台虚拟镜头）。Boss_Melee 现在没有 Cinemachine，主摄像机固定。
- 做法：写一个实现 `IBossCinematicCamera` 的镜头组件（镜头编号 → 虚拟镜头），拖到导演的 Camera Rig；加一个 `CinematicStep` 子类「切镜头」调用 `director.CameraRig.SwitchTo`。

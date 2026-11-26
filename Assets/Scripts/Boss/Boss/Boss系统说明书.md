# Boss 系统说明书

> 更新于 2026-11-25，按 11-22 第 4 阶段（脱手释放 + 规则表 + 近身反应）做完后的代码重写。上一版是 10-15 的。
> **主要读者：下一个对话里的 Claude。** 也适合接手 Boss 模块的开发者。
> 分四部分：**它怎么跑** → **怎么加招式** → **接下来要做什么** → **已知小问题**。
> 这份文档有两份、内容相同：claude.ai 项目里的 `claude/Boss系统说明书.md`，和仓库里的 `Assets/Scripts/Boss/Boss/Boss系统说明书.md`。开发记录、决策原因、协作规矩在仓库根目录的 `memory/`（以 memory 为准）。

---

## 0. 给 Claude 的速查

**路径**（11-22 用户改过文件夹名：`Enemies` → `Boss`，`Player_V0.2` → `Player_V0.3`）：
- Boss 脚本：`Assets/Scripts/Boss/Boss/`（下文脚本路径都相对于它）
- 招式卡脚本和资产：`Assets/Scripts/Boss/1_DataAssets/`（下文写作 `1_DataAssets/`）

**用户的规则**（项目说明里写着，必须遵守）：
1. 商量改动先讲功能目的，再讲代码方向；
2. 解释架构用通俗比喻；
3. 改文件前先说明目的、等用户确认；改完列出所有文件路径（含新文件）。

**文件地图**

| 文件 | 角色 |
|---|---|
| `BossLogic/BossController.cs` | 总控：状态机、执行器注册表、受击（打断 / 击退 / 持续推力）、招式分类 `CategoryOf`、共用走位 `DriveTowardDistance / StopHorizontal`、调试文字 |
| `BossLogic/BossAIDecider.cs` | 大脑：意图、单招 / 套路抽卡、位移卡池、近身反应卡池、弹幕在发时的规则表、战斗节奏、冷却 |
| `BossLogic/BossState.cs` | 黑板：`EnemyHealth`、`BossMechanic`（护盾 / 阶段 / 回盾计时）、`BossCombatMode` |
| `BossLogic/StateCard/BossMoveState.cs` | 移动状态：定意图 → 走位 → 出手 / 靠位超时 / 位移卡池；脱手后的走位限制；走位中的近身反应 |
| `BossLogic/StateCard/BossCombatState.cs` | 出招状态（**类名是 `BossActionExecuter`**）：兑现意图 → 找执行器 → 等演完；开火时移动、脱手、站着放弹幕时的近身反应 |
| `BossLogic/StateCard/BossKnockbackState.cs` | 受击滑行硬直 |
| `BossLogic/StateCard/BossStunState.cs` | 破盾眩晕 |
| `Core/BossContext.cs` | 只读情报总线 |
| `Core/HitboxUtility.cs` | 伤害结算、过滤器、射线工具 |
| `1_DataAssets/ActionNode.cs` | 所有招式卡的基类 + `SkillPhaseTag`、`BarrageRule` |
| `1_DataAssets/BulletNode.cs` | 弹幕卡 + `FireMoveSettings`（开火时移动）、`ReleaseSettings`（脱手释放） |
| `1_DataAssets/MeleeNode.cs` / `DashAttackNode.cs` / `LaserNode.cs` / `TeleportNode.cs` / `DashMoveNode.cs` | 其余五种卡 |
| `1_DataAssets/BossRoutine.cs` | 套路资产 + `RoutineStep` / `StepVariant` / `RoutineCondition` / `WeightModifier` |
| `1_DataAssets/BulletNode/PatternScripts/*.cs` | 弹幕图案（内嵌式，10 种） |
| `Execution/IBossActionExecutor.cs` | 执行器接口 + `IBackgroundableExecutor`（能把这一招交给弹幕层） |
| `Execution/Bullet/` | `BAE_BulletEmitter`、`EmitterTrack`、`BulletSpawnContext`、阵型类 |
| `Execution/Melee/` | `BAE_MeleeAttacker`、`MeleeSwingRunner`（近战 / 突进共用的挥砍时间轴）、`BossMeleeVisual` |
| `Execution/Special_Bullet/BAE_LaserAttacker.cs` | 激光 |
| `Execution/Special_Melee/BAE_DashAttacker.cs` | 突进斩 |
| `Execution/Tp/BAE_Teleporter.cs` | 传送 |
| `Execution/Dash/BAE_Dasher.cs` + `BossDashEndpointGuard.cs` | 冲刺卡 + Boss 自己的终点判定 |
| `Editor/BulletNodeEditor.cs`（含 `FireMoveGUI`、`ReleaseGUI`）、`RoutineStepDrawer.cs`、`TeleportNodeEditor.cs`、`DashMoveNodeEditor.cs` | 按开关 / 类型隐藏用不上的栏位（隐藏 ≠ 不生效） |
| `Editor/SubclassSelectorDrawer.cs` | 弹幕类型下拉框，**不能删** |
| `Editor/BulletNodeMigration.cs` | 一次性迁移工具，迁移已完成，**待用户删除** |
| `UI/EnemyHealthBar.cs` / `EnemyShieldBar.cs` | 血条、护盾条 |

Boss 文件夹外相关：`Items/Bullets/Enemy_Projectile.cs`、`Core/Combat/DamageInfo.cs`（含 `breakMask`、`hitStun`、`pushSpeed`、`shieldDamage`）、`Player_V0.3/Combat/ClashRegistry.cs`（拼刀）、`Player_V0.3/FSM/PassThroughController.cs`（冲刺 / 滑铲穿过敌人）。

---

# 第一部分：Boss 是怎么运作的

## 1.1 一句话概括

Boss = **黑板**（记数据）+ **状态机**（管流转）+ **大脑**（先想好下一招）+ **一堆执行器**（演出）。

比喻：大脑先决定「下一招打什么」（意图），状态机按这一招的需要去站位，到位了把卡交给对应的执行器去演。四者靠事件和接口连接，互不认识内部细节。

**两层（11-22 和用户对齐的说法，讲给用户时用可观察的效果来说）**：
- **身体层** = Boss 身体此刻正在做的那一招（`CombatState.CurrentAction`），**站着在放的弹幕也算**。玩家打断，打的就是这一招。
- **弹幕层** = 已经交给发射器自己发完、不占 Boss 身体的弹幕。Boss 可以去做别的。
- 破盾 / 转阶段 / 死亡：两层全停。
- 「层」只是归属：子弹永远由发射器发，从哪发出看「跟不跟随 Boss」。

## 1.2 三层结构

```
┌─ 数据层 ─────────────────────────────────────────────┐
│ ActionNode 派生的 SO 资产   ← 招式参数（含默认值）        │
│ BossRoutine 资产            ← 套路（步骤上可以覆盖默认值）  │
│ BossState / BossMechanic    ← HP、护盾、阶段、姿态       │
└──────────────────────────────────────────────────────┘
                    ↓ 读
┌─ 决策层 ─────────────────────────────────────────────┐
│ BossAIDecider   ← 意图、抽卡、位移卡池、近身反应、规则表、节奏、冷却 │
│ BossMoveState   ← 站到哪、什么时候出手、要不要闪 / 冲         │
│ BossCombatState ← 兑现意图、找谁来演、开火时移动、脱手、反应    │
└──────────────────────────────────────────────────────┘
                    ↓ 派发（字典查表）
┌─ 执行层（BAE_ = Boss Action Executor）───────────────┐
│ BAE_BulletEmitter / BAE_MeleeAttacker / BAE_LaserAttacker │
│ BAE_DashAttacker / BAE_Teleporter / BAE_Dasher           │
└──────────────────────────────────────────────────────┘
```

## 1.3 作战姿态（由护盾决定）

| 护盾 | 姿态 `BossCombatMode` | 含义 |
|---|---|---|
| 在 | `Ranged` 远程为主 | 远程类招式权重 ×3、近战 ×1（`rangedModeWeights`） |
| 碎 | `Melee` 近战为主 | 先眩晕 `stunDuration` 秒，之后近战 ×3、远程 ×1（`meleeModeWeights`） |

- **扣盾**：玩家的招在 `DamageInfo.shieldDamage` 里带了破盾值就按它扣（11-22 破盾值：近战招填整招总量，第一次打中一次扣完；子弹按发数平分；玩家激光每跳扣）。没填（-1）按老规矩：近战每次命中 `meleeShieldDamage`（100），远程 `rangedShieldDamage`（3）。盾默认 300。
- 护盾碎了才按 `amount` 扣真实血量；血量 ≤ `phase2Threshold` 进二阶段。
- 回盾倒计时从破盾那一刻开始算，实际回盾时间 = `max(shieldRecoverTime, stunDuration)`。
- 姿态切换（破盾 / 回盾）会作废当前意图（`AI.OnCombatModeChanged`）。
- 招式属于远程还是近战（用于姿态倍率），由它被放进**哪个卡池桶**决定：Bullets / SpecialBullets = 远程，Melees / SpecialMelees = 近战。
- `BossController` 从 `EntityBase` 继承来的 Max HP **不用**（Boss 的血在 `BossState.health`）。

## 1.4 状态机：四个状态

| 状态 | 干什么 | 什么时候离开 |
|---|---|---|
| `BossMoveState` | 定意图 → 朝意图卡的理想距离走 → 判断出手；问位移卡池（传送 / 冲刺）；走位中检查近身反应 | 能出手 → CombatState |
| `BossCombatState`（类名 `BossActionExecuter`） | `AI.SelectSkill()` → 查执行器 → `yield return executor.Execute(...)`；开火时移动、脱手、近身反应 | 演完（或脱手）→ MoveState；套路下一步能马上接（`HasImmediateFollowUp`）就留下直接出下一招 |
| `BossKnockbackState` | 被打断 / 没在出招时挨了带击退或硬直的攻击：按精确距离减速滑行，持续 max(滑行, 硬直) | 结束 → MoveState |
| `BossStunState` | 破盾眩晕：向上击飞，`BulletEmitter.StopAttack()` 停掉所有弹幕（两层） | `stunDuration` 秒后 → MoveState（护盾仍碎，近战为主） |

状态切换统一走 `BossController.ChangeState()`（先 `Exit()` 再 `Enter()`）。`BossController` 把 Update 和 FixedUpdate 都转给当前状态。

## 1.5 一次出招的完整流程

```
MoveState.Enter()
  ├ 接收出招状态交过来的「脱手后的走位限制」（脱手时间、能不能冲刺），见 1.7
  ├ AI.PlanIntent()：定意图（见 1.6）
  ├ BeginPhase()：重置计时，随机一个换位时间（CombatTempo.repositionTime）
  └ TryMovementTeleport(false)：问位移卡池要不要闪 / 冲（TooFar / TooClose / Random）

MoveState.Update() 每帧
  ├ 意图被作废了 → 重新定意图、重新计时（定不出意图时原地等，不重开移动阶段）
  ├ 位移传送前摇 / 冲刺中 → 交给它们
  ├ 弹幕层有「允许近身反应 + 跟随 Boss」的弹幕 → AI.TryPickReaction()，挑到就去出反应招（见 1.7.5）
  ├ 脱手时间内 → 只走位，不出手
  ├ AI.IsIntentReady（在射程内、不在冷却、没被规则表拦住 / 套路下一步能出 / 兜底卡）
  │    且（fireImmediatelyInRange 或 已移动满换位时间）→ ChangeState(CombatState)
  ├ 不在射程内、也不是在等弹幕，且靠位超过 maxApproachTime → AI.OnApproachTimeout()：
  │    放弃意图，改抽一张射程内的卡；没有就兜底卡；再没有就重新定意图
  │    （脱手时间内、等弹幕时，靠位超时计时暂停）
  └ MoveTowardIntent()：距离 < 目标-0.5 就后退，> 目标+0.5 就靠近，否则站住
       后退撞墙 → isCornered；第一次撞墙问一次位移卡池的 Cornered 条目

CombatState.Update() → DoCombat() 协程
  ① node = AI.SelectSkill()            兑现意图（null → 回 MoveState）
  ② 套路步骤有 delayBefore → 等待，之后 AI.RefreshStepChoice() 按此刻情况重选候补
  ③ executor = boss.GetExecutorFor(node)
  ④ 读设置：AI.ResolveFireMove / ResolveRelease / ResolveReaction（卡上默认 + 套路步骤覆盖）
  ⑤ yield return executor.Execute(node, ctx)
       期间每帧（Update）：到脱手时间 → 脱手（1.7.3）；允许近身反应 → 检查反应（1.7.5）
  ⑥ 近身反应触发过 → 出反应招 → 取回接着放 / 直接下一步
  ⑦ Finish()：脱手过的把走位限制交给 MoveState；套路能马上接就留在 CombatState
```

**关键点**：
- 理想距离（`preferredDistance`）只决定往哪走，**不参与出手判断**：只要在射程内、换位时间到了就出手。
- `DoCombat()` 里没有任何 `if (node is XNode)` 决定谁来演，靠字典查表。弹幕相关的移动 / 脱手 / 反应只看「执行器是不是发射器」。

## 1.6 AI 决策（`BossAIDecider`）

### 定意图 `PlanIntent()`（MoveState 调用；已有意图时什么都不做）

1. **套路**：当前阶段的套路池里，按条件筛选后按权重挑一个（有 `singleInterleaveChance` 的概率跳过这一层改出单招）。
   筛选：`mode` 与当前姿态匹配、套路不在冷却、不在上一个套路的「后续招式间隔」里（`RoutinesGated`）、玩家距离在 `minPlayerDistance ~ maxPlayerDistance`、玩家空中状态符合。权重 = `baseWeight` × 情境加成。
2. **单招**：只看阶段标签允许 + 不在冷却的卡，**不看当前距离**——够不着就让 Boss 走过去。
3. **兜底卡** `fallbackNode`：不管射程直接出。建议一定配一张。

### 单招权重

```
实际权重 = baseWeight × 姿态倍率 × 情境加成（所有满足条件的 WeightModifier 倍率相乘）
```

情境加成 `WeightModifier` = 条件 `RoutineCondition`（玩家在背后 / 面前、距离范围、空中状态，全部满足才算）+ 倍率。挂在卡池条目和套路条目上。

### 出手 `SelectSkill()`（CombatState 调用）的优先顺序

1. **走位中触发的近身反应**（`QueueReaction` 放进来的）：最先出，不动套路进度和单招意图。打完回 MoveState。
2. **兜底卡**：被规则表拦住就保留着等。
3. **套路** → `AdvanceRoutine()` 推进下一步。
4. **单招**：出手前再校验射程、冷却、阶段标签；够得着但被规则表拦住 → 保留意图、回去等；其他不通过 → 作废、重新定意图。
- 出手时 `Commit()` 记冷却。

### 套路（`BossRoutine`）

- 一个套路 = 若干步骤 `RoutineStep`：`action`、`delayBefore`（出招前停顿）、`whenOutOfRange`（够不着时：`Approach` 先走过去 / `Skip` 跳过 / `Abort` 整个作废）、`variants`（候补招式）。
- **步骤上可以覆盖弹幕卡的默认值**（只在这一步是弹幕卡、或候补里有弹幕卡时显示）：开火时移动 `fireMoveMode`、脱手释放 `releaseMode` + `release`、近身反应 `reactionMode` + `reclaimAfterReaction`。都是「跟随弹幕卡」或自己选。换成「替换」候补的弹幕卡也用步骤的设置；「插入」的候补用它自己卡上的。
- **候补招式** `StepVariant`：从上往下找第一个「条件满足且够得着」的。`Replace` 出候补、这一步算完成；`Insert` 先出候补再出原招，每一步最多插入一次。有 `delayBefore` 时停顿结束后再查一次。
- 套路里的招式**不检查自己的卡片冷却**（套路有自己的冷却，从开始那一刻算），但出招后照常记卡片冷却。
- **后续招式间隔** `restAfter`：最后一招真正放完（或脱手）那一刻开始算，被打断不算；期间不选新套路，单招和位移照常。
- 套路进行中，Boss 走位的目标是「下一步实际会出的招」（含候补）的理想距离（`PeekRoutine`）。下一步被规则表拦住时，也朝它的理想距离走。
- **玩家打断套路里的任何一招（包括近身反应招），整个套路作废。** 拼刀只结束 Boss 当前这一招，套路继续。

### 卡池条目 `PoolEntry<T>`

`phaseTag`（Mix / Ranged / Melee）+ `action` + `weightModifiers`。比喻：「卡套」——卡本身不变，卡套上写着哪个姿态能用。

### 战斗节奏 `CombatTempo`（远程姿态 / 近战姿态各一套）

| 字段 | 作用 |
|---|---|
| `repositionTime` (x~y) | 两招之间至少移动多久，随机取（远程默认 0.8～1.8，近战 0.2～0.5） |
| `fireImmediatelyInRange` | 勾 = 意图一进射程立刻出手；不勾 = 还要等换位时间 |
| `maxApproachTime` | 为一张意图卡靠位的最长时间（默认 4），超时换卡 |
| `moveSpeed` | 这个姿态的移动速度 |
| `phase2SpeedMultiplier` | 二阶段时速度再乘这个倍率 |

### 转阶段

`SwitchToPhase2()`：作废意图、换成二阶段的所有卡池（含位移卡池、近身反应卡池）、清空所有冷却和全局反应冷却。`BossController.HandlePhase2` 还会停掉所有执行器（`CancelAllExecutors`，含弹幕层）、传送到中央、回满护盾、回到移动状态。

## 1.7 弹幕（11-22 的改动都在这里）

### 1.7.1 开火时移动

`BulletNode.fireMoveMode` + `fireMove`（`FireMoveSettings`），套路步骤可覆盖：
- **站定**（默认）。
- **保持距离**：出招时按「范围 X～Y + 偏向滑条」抽一个距离，全程保持（±0.2 格算到位）。
- **位移后站定**：出招时定方向（远离 / 靠近玩家）和格数，走够就停。
- 外加移速倍率 `speedMultiplier`。走位用 `BossController.DriveTowardDistance(min, max, speed)`，和 MoveState 共用。
- 射程仍是出手前提。

### 1.7.2 弹幕混发（多张卡并行）

`BAE_BulletEmitter` 同时跑多张弹幕卡，每张一个编号（`comboId`），每张卡的轨道（`EmitterTrack`）各自到点结束，各带自己的击退设置。
- **前台** = 当前这一步（身体层）那张：`Execute` → `WaitForeground` 等它放完。
- `ReleaseToBackground()`：把前台这张交给弹幕层（脱手 / 托管用），`Execute` 马上返回。
- `Reclaim(id)`：近身反应打完把还在发的那张取回前台。
- `Cancel()`：只停前台那张（玩家打断）；已经发出脱手指令、还没来得及处理的，当作已经交出去（不停）。
- `StopAttack()`：全停（破盾眩晕、转阶段、死亡）。
- `HasReleasedBarrage`：弹幕层里有没有弹幕；`HasReactiveBarrage`：有没有「允许近身反应 + 跟随 Boss」的。

### 1.7.3 脱手释放（取代旧的「接下一步的时机」「为下一招靠位」）

弹幕卡：`allowRelease`（默认关）+ `release`（`ReleaseSettings`）+ `followBossWhenReleased`（默认开）。套路步骤：`releaseMode`（FollowCard / NoRelease / Release）+ 步骤自己的 `release`。单招抽到开了脱手的卡也生效。

| 参数 | 意思 |
|---|---|
| `beforeEnd` | 弹幕结束前多少秒脱手。比整张弹幕还长 = 一开火就脱手 |
| `holdTime` | **脱手时间**：从脱手那一刻算，这段时间里 Boss 不出下一招、只走位；时间到了够得着就出。0 = 够得着马上出 |
| `allowDash` | 脱手后、出下一招之前这段走位：勾 = 位移卡池只看冲刺卡（不看传送）；不勾 = 不问位移卡池，只走路。**这个限制管到 Boss 出下一招为止**（走位中的近身反应打完回来，限制还在） |
| `followBossWhenReleased` | 勾 = 子弹继续从 Boss 身上发；不勾 = 钉在原地：发射器在脱手那一刻放一个锚点 `BarrageAnchor_<id>`，这张卡之后的子弹从锚点发，卡结束时销毁 |

- 运行：CombatState 到点 `TryRelease` → 发射器交到弹幕层 → `Execute` 返回 → `Finish`：把「脱手时间 + 能不能冲刺」交给 MoveState（`ArmAfterRelease`）。脱手时间是 0 且套路下一步能马上接，就留在 CombatState 直接出。
- 套路最后一步脱手的那一刻就算套路打完，后续招式间隔从那时开始算。

### 1.7.4 弹幕在发时的规则表

只在「弹幕层里有弹幕」时起作用。`BossAIDecider.barrageRules`（`BarrageRuleTable`），**按卡类型**判断，不看放在哪个卡池：

| 类别 | 卡类型 | 默认 |
|---|---|---|
| 弹幕 | BulletNode | ✓ |
| 近战 | MeleeNode | ✓ |
| 特殊近战 | DashAttackNode | ✗ |
| 特殊远程 | LaserNode | ✗ |
| 冲刺 | DashMoveNode | ✓ |
| 传送 | TeleportNode | ✗（受击传送不归它管） |

- 每张卡上 `whileBarrage`（FollowCategory / Allow / Forbid）可以单独改。
- 被拦的套路下一步 / 单招 / 兜底卡：**不跳过、不作废，等弹幕发完**。等的时候朝那招的理想距离走，**不算靠位超时**（`AI.IsWaitingForBarrage`）。候补按原规则选出后被拦也是等。
- 位移卡池、靠位超时后的重抽：被拦的卡直接不考虑。

### 1.7.5 近身反应

弹幕卡：`allowCloseReaction`（默认关）+ `reclaimAfterReaction`（默认开）。套路步骤：`reactionMode`（FollowCard / Off / On）+ 步骤自己的 `reclaimAfterReaction`。
`BossAIDecider`：近身反应卡池 `phase1Reactions / phase2Reactions`，每条 `ReactionEntry` = 阶段标签 + 条件（`RoutineCondition`：距离、背后 / 面前、空中）+ 权重 + 概率 + 反应招（近战、突进斩等；弹幕卡放这里不生效）。全局 `reactionCooldown`（默认 2 秒）。

**挑反应招** `TryPickReaction()`：全局反应冷却没好 → 不反应。卡池里「阶段标签允许 + 条件满足 + 够得着 + 冷却好 + 规则表允许」的条目按权重挑一张，按它的概率掷骰。**不管掷没掷中都进全局反应冷却**；掷中就记这张卡的冷却。

**情况 1：Boss 站着放允许近身反应的弹幕**（CombatState 每帧检查）
1. 挑到反应招 → 弹幕不中断，托管给发射器（`ReleaseToBackground`，跟不跟随看卡上的 `followBossWhenReleased`）；开火时移动先停。
2. 身体出反应招（内联在同一个出招协程里，`CurrentAction` 换成反应招 → 玩家打断打的是它）。
3. 打完：
   - 取回 + 弹幕还在发 → `Reclaim` 回到 Boss 身上（钉住的锚点撤掉），开火时移动接着走，接着放完这一步；之后过了全局冷却还可以再反应。
   - 弹幕已经发完 → 直接做下一步。
   - 反应期间已经过了脱手时间点 → 不取回，按脱手处理。
   - 不取回 → 弹幕留在弹幕层发完，Boss 直接做下一步。

**情况 2：Boss 在走位，弹幕层里有「允许近身反应 + 跟随 Boss」的弹幕**（钉在原地的不算）
- MoveState 每帧检查，挑到就 `QueueReaction` 并切到 CombatState，`SelectSkill` 最先出它。打完回 MoveState，意图、套路进度、脱手后的走位限制都保留。

**反应招被打断**：和打断普通招一样（套路作废）；托管的弹幕照样在弹幕层发完。

## 1.8 位移：位移卡池、传送卡、冲刺卡

**位移卡池**（`BossAIDecider` 的 Phase1 / Phase2 Movement，`MovementEntry`）：阶段标签 + 触发条件 + 差距区间 + 权重 + 概率 + 卡（传送卡或冲刺卡）。

| 触发 | 什么时候问 |
|---|---|
| `TooFar` / `TooClose` | 每个移动阶段开始问一次：差距 = 当前距离 − 意图的理想距离，在 `minGap ~ maxGap` 之间才算（新条目代码默认 5～99） |
| `Random` | 每个移动阶段开始都算对得上 |
| `Cornered` | 后退撞墙时问一次（按列表顺序取第一张） |

- 对得上的条目按权重抽一张，再按 `chance` 决定闪 / 冲还是走路。每个移动阶段最多一次。
- 位移卡池**一律看冷却**，也受规则表约束；脱手后的走位按 1.7.3 的限制（只看冲刺卡 / 不问）。
- 卡没填理想距离时 Boss 原地等（不再取射程中点，以前会先往远处走 49.5 格）。

**传送卡** `TeleportNode` + `BAE_Teleporter`：三种用法共用卡上的冷却。
- 位移卡池；套路步骤；**受击传送**（卡上勾 `reactToHit`，拖进 `BAE_Teleporter` 的 Hit Reactions 列表：按姿态、伤害类型筛选，概率从 `baseChance` 起、每挨一下没触发加 `chanceGainPerHit`，`chanceResetAfter` 秒没挨打回到基础值；`interruptAction` 勾上会打断当前招式和套路）。
- 落点类型 `TeleportTargetType`：`RandomPoint` / `BehindPlayer` / `Center` / `IntentDistance`。落点参数（11-22 从执行器搬到卡上，按落点类型显示）：`snapshotPlayerPosition`（按发动那一刻的玩家位置算）、`behindDistance`（绕背距离，默认 3）、`matchPlayerHeight`（默认开）、`landingDistance`（意图距离落点离玩家多远，-1 = 下一招的理想距离）。
- 落点安检：不压地面 / 墙、连线不穿墙、脚下有地面、不和玩家重叠；一侧不行换另一侧，两侧都不行就这次不传。

**冲刺卡** `DashMoveNode` + `BAE_Dasher`（纯位移，有冷却）：
- 目标 `Target`：`ToDistance`（冲到离玩家某个距离，`Landing Distance` 填 -1 = 下一招的理想距离）/ `AwayFromPlayer`（后撤固定格数）/ `ThroughPlayer`（冲到玩家身后）。
- `Dash Duration`（用时）、`Max Dash Distance`（最远）、`Windup`（前摇）、`Pause After`（冲完停顿）、`Min Dash Distance`（差得太少就不冲）。
- 终点判定四个参数同玩家（`Endpoint Guard`、`Can Pass Through`、`Endpoint Pass Ratio`、`Endpoint Overlap Tolerance`），由 `BossDashEndpointGuard` 处理（Boss 自己一份，不复用玩家的）。穿身用刚体 `excludeLayers`，冲完还叠着就等分开（最多 0.5 秒）。
- 套路步骤里走 `Execute`；位移卡池走 `TryMovementDash / IsMovementDashing / CancelMovementDash`（MoveState.Exit 会停掉）。
- 冲刺卡的打断类别是 None：玩家打不断。

## 1.9 受击、打断链路

```
受击：BossController.TakeDamage → HandleBreakAndKnockback
  ├ 正在出招（CombatState.CurrentAction）且 DamageInfo.breakMask 含这一招的类别 → 打断：
  │    AI.ClearIntent()（套路作废）+ 切状态 → CombatState.Exit：停协程 + activeExecutor.Cancel()
  │    （发射器的 Cancel 只停前台那张，弹幕层的继续发）
  ├ 有击退距离或硬直，且（没在出招 / 被打断了）→ BossKnockbackState
  │    （在出招又没被打断 = 霸体，不吃击退）
  ├ 护盾：shieldDamage（-1 = 近战 100 / 远程 3）
  └ 带 pushSpeed → HandlePush / TickPush：一段时间里每帧把 Boss 往外推（撞墙停、眩晕时不推）

类别 CategoryOf：MeleeNode = Melee，BulletNode = Bullet，DashAttackNode = SpecialMelee，
                LaserNode = SpecialBullet，TeleportNode = Teleport，其余（含 DashMoveNode）= None（打不断）

拼刀：Boss 近战出判定前查 ClashRegistry，撞上就只结束这一招（套路继续）。

破盾：BossMechanic.BreakShield → HandleShieldBroken → AI.OnCombatModeChanged + ChangeState(StunState)
       → StunState.Enter 里 BulletEmitter.StopAttack()（弹幕层也停）
受击传送（interruptAction）：BossController.InterruptCurrentAction
转阶段 / 死亡：BossController.CancelAllExecutors()（每个执行器 Cancel + 发射器 StopAttack）
```

- **每个执行器都必须实现 `Cancel()` 且可重入**（没在执行时调用是安全的空操作）。
- `DoCombat()` 里必须是 `yield return executor.Execute(...)`，**不是** `yield return boss.StartCoroutine(...)`；后者派生的子协程不会随父协程停止。近身反应的反应招也是这样内联的。
- `MoveState.Exit` 会作废还在前摇的位移传送、停掉位移冲刺。

## 1.10 伤害通路

- 契约（`Core/Combat/DamageInfo.cs`）：`readonly struct DamageInfo`（amount、type、sourcePosition、instigator、breakMask、reaction、reactionParam、overrideKnockback、knockbackForce、hitStun、pushSpeed、pushDuration、shieldDamage）+ `IDamageable.TakeDamage(in DamageInfo)`。攻击方只表达意图，受击方自己决定怎么反应。
- Boss 打玩家：击退默认归玩家决定；招式卡勾了 `overrideKnockback` 时用卡上的 `knockbackForce`。弹幕通过 `Enemy_Projectile.SetKnockback`（每张卡各带一份），近战 / 激光通过 `HitboxUtility.ApplyDamage` 的参数带上。
- 近战判定：`PlayerCombatConfig.meleeHitsBodyCollider` 勾选（默认）时判定框直接和玩家身体碰撞箱比重叠。
- 所有判定统一走 `HitboxUtility.ApplyDamage()`：找 `IDamageable`、去重、防自伤。

## 1.11 BossContext：只读情报总线

```
ctx.boss / player / bossState / playerState / controller / playerBodyCollider
ctx.PlayerAimPoint        ← 远程瞄准点：玩家的 Hurtbox_Core
ctx.MeleeHitsPlayerBody
ctx.IsPlayerGrounded / IsPlayerFlying
ctx.IsPlayerBehindBoss    ← 玩家站在 Boss 上一次近战 / 突进出手方向的反侧（还没出过近战时为 false）
ctx.DistanceToPlayer / HorizontalSignToPlayer / PlayerHPRatio / BossHPRatio
```

- **远程攻击一律瞄 `ctx.PlayerAimPoint`**，不要用 `player.position`（玩家轴心在脚底）。
- Boss 轴心在底部中心；近战判定框以底边中点为锚点。
- `BossController.DistanceToPlayer` 是两个物体位置的直线距离（上下差也算进去）。

---

# 第二部分：怎么创建一个招式

## 2.1 现有的六种招式卡

| 卡 | 执行器 | 能做什么 | 关键字段 |
|---|---|---|---|
| `BulletNode` | `BAE_BulletEmitter` | 多段组合弹幕，每段内嵌一种图案；开火时移动、脱手释放、近身反应 | `phases`、`fireMoveMode`、`allowRelease`、`allowCloseReaction` |
| `MeleeNode` | `BAE_MeleeAttacker` | 多段挥击连招（windup → hold（按 `releaseBias` 抽蓄刀时长）→ attack → recover，可竖砍 / 横砍） | `swings`、`autoRangeFromHitbox` |
| `DashAttackNode` | `BAE_DashAttacker` | 突进斩：冲到玩家面前再挥砍 | `dashSpeed`、`swings`、`autoStopFromHitbox` |
| `LaserNode` | `BAE_LaserAttacker` | 预警 → 光束，可追踪 | `telegraphTime`、`fireTime`、追踪时间 |
| `TeleportNode` | `BAE_Teleporter` | 传送（1.8） | `targetType`、落点参数、受击反应 |
| `DashMoveNode` | `BAE_Dasher` | 纯位移冲刺（1.8） | `Target`、`Landing Distance`、用时、终点判定 |

## 2.2 所有卡共有的字段（`ActionNode`）

| 字段 | 作用 | 注意 |
|---|---|---|
| `actionName` | 显示名，用于日志和调试文字 | 留空则显示资产名 |
| `baseWeight` | 抽卡权重 | 0 = 永不抽中 |
| `cooldown` | 冷却秒数 | 套路步骤不看冷却但会记；位移卡池、近身反应卡池看冷却 |
| `minCastDistance` / `maxCastDistance` | 射程 | AI 一律读 `MaxCastDistance`：近战卡勾 `autoRangeFromHitbox` 时按第一段判定框最远边 + 余量自动算，手填的不生效 |
| `preferredDistance` | 准备出这招时走到离玩家多远 | -1 = 射程中点；会被夹在射程内 |
| `whileBarrage` | 弹幕层有弹幕时能不能出：跟随类别 / 允许 / 禁止 | 见 1.7.4 |
| `overrideKnockback` / `knockbackForce` | 命中时覆盖玩家默认击飞力度 | |
| `chargeAnimName` / `activeAnimName` / `recoverAnimName` | 动画名 | 留空则不切 |

## 2.3 创建流程

1. **建资产**：Project 右键 → Create → ScriptableObjects → XxxNode。放在 `1_DataAssets/` 下对应文件夹：`BulletNode/Combo`、`BulletNode/Prototype`、`MeleeNode`、`Sp_B_Node`（特殊远程）、`Sp_M_Node`（特殊近战）、`MoveNode/TpNode`（传送）、`MoveNode/DashNode`（冲刺）；套路放 `BossRoutine/{Melee,Mix,Range}`。
2. **填共有字段**（2.2），尤其是射程和理想距离。
3. **填专属字段**：每个字段都有 Tooltip。
4. **确认执行器挂在 Boss 上**：没挂会报 `没有执行器认领 XxxNode`。
5. **放进 `BossAIDecider`**（Inspector 从上到下）：

```
Phase1/2 Bullets / Melees / Special Bullets / Special Melees   ← 单招卡池（卡套：阶段标签 + 卡 + 情境加成）
Phase1/2 Routines + Single Interleave Chance                  ← 套路池
Phase1/2 Movement                                             ← 位移卡池（传送 / 冲刺）
Phase1/2 Reactions + Reaction Cooldown                        ← 近身反应卡池 + 全局反应冷却
Ranged / Melee Mode Weights                                   ← 姿态倍率
Ranged / Melee Tempo                                          ← 战斗节奏
Fallback Node                                                 ← 兜底卡
Barrage Rules                                                 ← 弹幕在发时的规则表
```

**加列表条目后先展开检查数值**：Unity 在 Inspector 里新加的条目可能是 0 而不是代码里的默认值（11-22 踩过：近身反应条件 Max Distance = 0 → 永远不触发；位移卡池 Min Gap = 0）。最好在非运行状态配好再点 Play，改完记得保存。

**加一种全新的招式类型**：新建 `ActionNode` 派生 SO + 实现 `IBossActionExecutor` 的 MonoBehaviour（`NodeType` 返回新卡类型）并挂到 Boss 上。要让它参与规则表 / 打断分类，还要在 `BarrageRuleTable.Allows` 和 `BossController.CategoryOf` 里加一行。

## 2.4 弹幕怎么配

- `BulletNode.phases` 每段 = `startDelay` + `duration` + `pattern`（`[SerializeReference, SubclassSelector]`，Inspector 下拉选类型）。整招时长 `TotalDuration` = 所有段 `startDelay + duration` 的最大值（脱手的「结束前 X 秒」就按它算）。
- 图案基类 `BulletPatternBase`（普通 `[Serializable]` 类，不是 ScriptableObject）：`interval`、`bulletSpeed`、`bulletScale`（0 = 预制体大小）、`lifetime`（0 = 不限）、`projectileKey`、加速三项。
- 现有 10 种：混沌、环形、扇形、直线瞄准、随机、旋转、方阵、星形、三角、全屏直线。阵型类继承 `FormationPatternBase`，只需实现 `BuildShape()`。
- 写新图案：`[Serializable, BulletPatternInfo("显示名")]` + 继承 `BulletPatternBase` + 实现 `Spawn(ctx)`，发子弹用 `ctx.host.SpawnBullet(this, 方向, 位置[, 速度])`。位置从 `ctx.firePoint` 读（钉在原地的弹幕会把它换成锚点，图案不用管）。
- 开火时移动、脱手、近身反应的默认值配在卡上，套路里要不一样就在步骤上改（2.1 / 1.7）。

## 2.5 调试

- **头顶调试文字**：`bossStatusText` 显示 `[意图] xxx` / 正在出的招（套路带 `[套路名]`，反应招带 `[近身反应]`）；`movementStatusText` 显示 `[移动] 靠近 / 后退 当前距离 → 目标距离`、`到位，等待出手`，脱手后会加前缀 `脱手 x.xs ·`（脱手时间内）/ `等弹幕 ·` / `脱手后 ·`。
- **Console 关键日志**：
  - `[AI] 意图：xxx（射程 a~b，理想距离 c，当前 d）`、`[AI] 出牌: xxx`、`[AI] 意图 xxx 靠位超时，放弃`
  - `[AI] 开始套路 / 第 N 步插入候补 / 改出候补 / 套路完成 / 套路中断`
  - `[AI] 位移（TooFar，差距 x）：xxx`
  - `[Boss] 弹幕脱手：脱手时间 x 秒，允许冲刺 / 只走路`
  - `[Emitter] 弹幕「xxx」转到弹幕层继续发（跟随 Boss / 钉在原地）`、`[Emitter] 近身反应打完，弹幕取回到 Boss 身上`
  - `[AI] 近身反应：xxx` / `[AI] 近身反应没掷中（xxx，概率 p），n 秒后再看`；两种都没有 = 条件一直没满足（多半是距离或条件里的数值）
  - `[Boss] 近身反应期间已过脱手时间，弹幕不取回，按脱手处理`
- `BAE_Teleporter.debugTeleport`、`BAE_Dasher` 的 Debug Dash：打印传送 / 冲刺的落点和距离。
- **Gizmos**：近战判定框橙红、突进橙黄、激光红。
- 想单独测一张卡：临时把卡池里只留它，或做一个只有它的套路。

---

# 第三部分：接下来要做的

做之前按用户规则先讲功能、等确认。详细记录在仓库 `memory/待定事项/`。

1. **Boss 招式卡统一面板**（`05_Boss招式卡统一面板.md`，下一步）：Boss 招式卡像玩家的 ComboNode 一样先选类型、再出参数表。**第一步不写代码**：先把所有卡、套路步骤、Boss 身上的参数列成表，和用户一起定每个参数属于哪一级（卡上 / 套路步骤上 / Boss 身上），再动手。
2. **位移卡池 · 套路专用冲刺**（`07_位移卡池_套路专用冲刺.md`）：让一张冲刺卡只在套路中（或只在套路外）用，以及这类卡要不要看冷却。
3. **到达理想距离才出手**（10-15 提的，一直没做，要不要还需和用户确认）：`CombatTempo` 加开关 + 容差，并加「在射程内但超时没到位 → 直接出手」的保底。
4. 藏掉 `BossController` 上不生效的 Max HP。

---

# 附录：已知的小问题（2026-11-25 核对）

不影响运行，改相关代码时顺手清掉：

1. **`BossCombatState.cs` 里的类名还是 `BossActionExecuter`**。改名要同步 `BossController` 里的属性类型和 `new`。
2. **`BossStunState.Enter()` 里的 `boss.BulletEmitter.StopAttack()`**：现在负责「破盾时弹幕层也一起停」，是有用的；语义上可以换成 `boss.CancelAllExecutors()`。
3. **`IBossActionExecutor.cs` 里 `IBackgroundableExecutor` 的注释还在说「接下一步的时机」**：应改成「脱手 / 托管」。
4. **`BossAIDecider` 开场时把当前阶段的位移卡池 / 近身反应卡池记成列表引用**：运行中在 Inspector 里往列表加条目可能不生效（未验证），建议在非运行状态配好再 Play。
5. **`Editor/BulletNodeMigration.cs` 还在**，迁移已完成，可以删（用户在 Unity 里删；`SubclassSelectorDrawer.cs` 不能删）。
6. **遗留枚举**：`BulletNode.cs` 的 `BossAttackType`、`BAE_BulletEmitter.cs` 的 `BossAttackFlags`。
7. **`BAE_LaserAttacker.cs` 多余的 `using Unity.VisualScripting;`**；`Execution/Laser/LaserBeamView.cs` 只是空壳。
8. **`BAE_Teleporter.debugTeleport` 默认是 true**，稳定后可关。
9. **`BossMechanic.comboExtendDuration`** 是没生效的预留字段；**`BossAIDecider.canAttack`** 在 Boss 文件夹内已无调用方。
10. **`FullScreenLinePattern` 用挂在发射器上的协程**：Boss 在子弹定住期间被销毁的话，子弹会一直停在原地。目前 Boss 不会中途销毁。
11. **`fallbackNode` 建议配一张**：全在冷却时没有兜底会在移动 / 战斗之间空转。

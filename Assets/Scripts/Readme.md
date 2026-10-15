# Flan

A 2D action platformer built solo in Unity and C#. Core files are around 15,000 lines across 97 scripts.
Four architectural decisions worth pointing are all in the combat layer.

Paths are relative to 'Assets/Scripts/'.

-------------------

1. Gem system built on a three-way verdict and stat modifiers

Instead of the state machine branching on gem types, each equipped gem is polymorphically asked (proceed, take over, or veto) for a verdict when an action fires.So states depend only on the gem interface and adding a gem touches no existing code. Stat changes are applied as source-tagged modifier objects layered over an immutable base value rather than written into it, which makes equip and unequip symmetric by construction and completely eliminates the bug of "ghost gem"(gem effect remains active even after removed).

Player_V0.2/Gem/GemRuntime.cs
Gem/GemEnums.cs
Gem/GemContext.cs
Gem/LoadoutManager.cs
Gem/Gems_actions/
Player_V0.2/Data/ModifiableStat.cs
Data/StatModifier.cs;Data/PlayerStats.cs

2. Three-tier input forgiveness system

Every command is buffered, but the grace window is anchored to whatever that input is actually waiting on. For example, an attack waits on its own animation, so its window opens when the cancel window does; a held charge waits on the player's finger, so it has no expiry at all; movement waits on world events like cooldowns and landings, so it gets a short fixed window from the keypress. 
Dispatch is tri-state rather than boolean, separating "the gate is shut but will open" from "this was refused", so a rejected command is discarded instead of resurfacing frames later as an action the player never asked for.

Player_V0.2/Input/PlayerCommandRouter.cs
Input/InputBufferQueue.cs
Input/ComboInputBuffer.cs

3. Boss AI by a weighted draw over ScriptableObject skill cards with intent reservation

Attacks are authored as ScriptableObject assets carrying their own weight, cooldown and cast range, and the boss picks one by weighted random draw from the subset those constraints leave available, so the entire fight is retuned in the Inspector without code changes. When the draw lands on an attack that is out of range the boss reserves it as an intent rather than rerolling, and locomotion then targets that attack's ideal range.

Enemies/Boss/BossLogic/BossAIDecider.cs
BossLogic/StateCard/BossMoveState.cs
Boss/DataAssets/ActionNode.cs
DataAssets/BulletNode.cs
DataAssets/MeleeNode.cs
DataAssets/LaserNode.cs
DataAssets/TeleportNode.cs

4. Ability dispatch through a type-keyed executor registry

Executors register themselves against the card type they handle and are discovered by reflection at startup, so dispatch is a dictionary lookup instead of a type switch and a new ability costs one asset plus one component with zero edits to existing files. 
The same interface mandates a cancellation contract, letting interrupts such as a shield break or phase transition fan out to every ability at once rather than each interrupt site remembering which systems are currently running.

Enemies/Boss/Execution/IBossActionExecutor.cs
BossLogic/BossController.cs
Execution/Bullet/BAE_BulletEmitter.cs
Execution/Melee/BAE_MeleeAttacker.cs
Execution/Special_Bullet/BAE_LaserAttacker.cs
Execution/Special_Melee/BAE_DashAttacker.cs
Execution/Tp/BAE_Teleporter.cs

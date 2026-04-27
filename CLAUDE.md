# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

TPGame is an Unreal Engine 5.7 C++ sample project demonstrating three distinct game variants within a single module, built on the **ModularGameplay** architecture. It uses GameFeatures, EnhancedInput, StateTree, and GameplayAbilities.

## Build Requirements

- **Unreal Engine 5.7** (custom engine build: `{DB54F5D0-49E9-4E60-BE73-3288D85EC11D}`)
- **.NET 9.0** SDK (see `global.json`)
- **Visual Studio 2022** with `Microsoft.VisualStudio.Workload.NativeGame` workload (see `.vsconfig`)

## Build Commands

The project does not use a separate build system — all compilation is done through Unreal Build Tool (UBT).

```bash
# Open in Visual Studio (generates project files if needed)
# Run from the project root; UE must be registered or discoverable
# TPGame.sln is pre-generated

# Build from command line (requires UE installed):
# RunUAT.bat BuildCookRun -project="D:/UnrealProjects/TPGame/TPGame.uproject" -platform=Win64 -clientconfig=Development -build
```

In practice, development is done inside the Unreal Editor or Visual Studio. Use the `TPGame.sln` solution at the project root. The editor target is `TPGameEditor` (Debug/Development), the game target is `TPGame`.

## Architecture

### Module & Target Structure

- **Single runtime module**: `TPGame` (`Source/TPGame/`), containing all game code
- **Two build targets**: `TPGame` (Game) and `TPGameEditor` (Editor), both using UE 5.7 build settings

### Core Architecture: Variant-Based Design

The project is organized around three "variant" subdirectories, each containing a complete self-contained gameplay style. All variants live under `Source/TPGame/`:

| Directory | Gameplay Style | Key Features |
|---|---|---|
| `Variant_Combat/` | Melee combat | Combo attacks, charged attacks, damage/death/respawn, AI enemies, danger detection |
| `Variant_Platforming/` | Platforming | Double jump, wall jump, dash, coyote time, hold-to-jump-higher |
| `Variant_SideScrolling/` | Side-scrolling | AI NPCs with StateTree, pickups, moving platforms, jump pads, camera manager |

Each variant provides its own `Character`, `GameMode`, and `PlayerController` subclasses. The base classes at `Source/TPGame/` root (`ATPGameCharacter`, `ATPGameGameMode`) are abstract base implementations.

### ModularGameplay Pattern

The project uses the **ModularGameplayActors** plugin (`Plugins/Systems/ModularGameplayActors/`), which provides modular base classes:
- `AModularCharacter`, `AModularPawn`, `AModularAIController`
- `AModularPlayerController`, `AModularGameMode`, `AModularGameState`, `AModularPlayerState`

The base `ATPGameCharacter` extends `AModularCharacter` (not the standard `ACharacter`). Variant characters extend either `AModularCharacter` (through `ATPGameCharacter`) or standard `ACharacter` directly, depending on whether they need modular gameplay features.

### Input System

Uses **EnhancedInput** with a modular component approach:
- `UPlayerControlsComponent` (`Source/TPGame/Input/`) — a `UPawnComponent` that manages `UInputMappingContext` binding and provides `SetupPlayerControls`/`TeardownPlayerControls` BlueprintNativeEvents
- Each variant's Character class binds input actions (Jump, Move, Look, plus variant-specific actions like Dash, ComboAttack, ChargedAttack)
- Character classes expose `UFUNCTION(BlueprintCallable)` methods (`DoMove`, `DoLook`, `DoJumpStart`, etc.) so both C++ and UI can drive character movement

### GameFeatures System

`Source/TPGame/GameFeatures/` contains `UGameFeatureAction` subclasses for modular feature activation:
- `GameFeatureAction_WorldActionBase` — base class that listens for game instance start and delegates to `AddToWorld()`
- `GameFeatureAction_AddAbilities` — grants gameplay abilities
- `GameFeatureAction_AddInputContextMapping` — adds input mappings
- `GameFeatureAction_AddLevelInstances` — streams in level instances
- `GameFeatureAction_AddSpawnedActors` — spawns actors
- `GameFeatureAction_AddWorldSystem` — adds world subsystems

### AI (Combat & SideScrolling Variants)

- AI controllers use **StateTree** for behavior logic (via `StateTreeModule` + `GameplayStateTreeModule`)
- `CombatAIController` / `SideScrollingAIController` drive enemy/NPC behavior
- `CombatStateTreeUtility` / `SideScrollingStateTreeUtility` provide utility functions for StateTree tasks
- EQS (Environment Query System) contexts: `EnvQueryContext_Player` and `EnvQueryContext_Danger` for spatial queries

### Ability System

- `AbilityInputBindingComponent` — bridges GameplayAbility input binding
- `TPGameAbilityAttributeSet` — GAS attribute set definition
- Used in conjunction with `GameFeatureAction_AddAbilities`

### Plugins

- **ModularGameplayActors** (`Plugins/Systems/`) — modular base classes for GameMode, Character, PlayerController, etc.
- **Explosions** (`Plugins/GameFeatures/`) — game feature plugin (runtime module only, content-driven)

### Key Dependencies (from Build.cs)

`Core`, `CoreUObject`, `Engine`, `InputCore`, `EnhancedInput`, `AIModule`, `StateTreeModule`, `GameplayStateTreeModule`, `UMG`, `Slate`, `GameplayAbilities`, `GameFeatures`, `ModularGameplay`, `ModularGameplayActors`

### Log Category

The primary log category is `LogTPGame` (declared in `TPGame.h`). Individual variants may declare additional categories (e.g., `LogCombatCharacter`).

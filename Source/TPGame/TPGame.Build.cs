// Copyright Epic Games, Inc. All Rights Reserved.

using UnrealBuildTool;

public class TPGame : ModuleRules
{
	public TPGame(ReadOnlyTargetRules Target) : base(Target)
	{
		PCHUsage = PCHUsageMode.UseExplicitOrSharedPCHs;

		PublicDependencyModuleNames.AddRange(new string[] {
			"Core",
			"CoreUObject",
			"Engine",
			"InputCore",
			"EnhancedInput",
			"AIModule",
			"StateTreeModule",
			"GameplayStateTreeModule",
			"UMG",
			"Slate",
			"GameplayAbilities",
			"GameFeatures",
			"ModularGameplay",
			"ModularGameplayActors",
			"EnhancedInput"
		});

		PrivateDependencyModuleNames.AddRange(new string[] { });

		PublicIncludePaths.AddRange(new string[] {
			"TPGame",
			"TPGame/Variant_Platforming",
			"TPGame/Variant_Platforming/Animation",
			"TPGame/Variant_Combat",
			"TPGame/Variant_Combat/AI",
			"TPGame/Variant_Combat/Animation",
			"TPGame/Variant_Combat/Gameplay",
			"TPGame/Variant_Combat/Interfaces",
			"TPGame/Variant_Combat/UI",
			"TPGame/Variant_SideScrolling",
			"TPGame/Variant_SideScrolling/AI",
			"TPGame/Variant_SideScrolling/Gameplay",
			"TPGame/Variant_SideScrolling/Interfaces",
			"TPGame/Variant_SideScrolling/UI"
		});

		// Uncomment if you are using Slate UI
		// PrivateDependencyModuleNames.AddRange(new string[] { "Slate", "SlateCore" });

		// Uncomment if you are using online features
		// PrivateDependencyModuleNames.Add("OnlineSubsystem");

		// To include OnlineSubsystemSteam, add it to the plugins section in your uproject file with the Enabled attribute set to true
	}
}

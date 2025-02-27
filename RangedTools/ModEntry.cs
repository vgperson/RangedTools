﻿using HarmonyLib;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Netcode;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Monsters;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace RangedTools
{
    /// <summary>The mod entry point.</summary>
    public class ModEntry : Mod
    {
        private static IMonitor myMonitor;
        private static IInputHelper myInput;
        private static ITranslationHelper str;
        private static ModConfig Config;
        
        public static bool specialClickActive = false;
        public static Vector2 specialClickLocation = Vector2.Zero;
        public static List<SButton> knownToolButtons = new List<SButton>();
        
        public static bool disableToolLocationOverride = false;
        public static int tileRadiusOverride = 0;
        public static bool mouseFacingOverride = false;
        
        public static bool preventDraw = false;
        public static Texture2D preventDrawTexture;
        public static Rectangle? preventDrawSourceRect;
        
        /***************************
         ** Mod Injection Methods **
         ***************************/
        
        /// <summary>The mod entry point, called after the mod is first loaded.</summary>
        /// <param name="helper">Provides simplified APIs for writing mods.</param>
        public override void Entry(IModHelper helper)
        {
            try
            {
                myMonitor = this.Monitor;
                myInput = this.Helper.Input;
                str = this.Helper.Translation;
                Config = this.Helper.ReadConfig<ModConfig>();
                
                helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
                helper.Events.Input.ButtonPressed += this.OnButtonPressed;
                helper.Events.Input.CursorMoved += this.OnCursorMoved;
                
                Harmony harmonyInstance = new Harmony(this.ModManifest.UniqueID);
                
                patchPrefix(harmonyInstance, typeof(Farmer), nameof(Farmer.useTool),
                            typeof(ModEntry), nameof(ModEntry.Prefix_useTool));
                
                patchPostfix(harmonyInstance, typeof(Farmer), nameof(Farmer.useTool),
                             typeof(ModEntry), nameof(ModEntry.Postfix_useTool));
                
                patchPrefix(harmonyInstance, typeof(Character), nameof(Character.GetToolLocation),
                            typeof(ModEntry), nameof(ModEntry.Prefix_GetToolLocation),
                            new Type[] { typeof(Vector2), typeof(bool) });
                
                patchPrefix(harmonyInstance, typeof(Utility), nameof(Utility.isWithinTileWithLeeway),
                            typeof(ModEntry), nameof(ModEntry.Prefix_isWithinTileWithLeeway));
                
                patchPrefix(harmonyInstance, typeof(Game1), nameof(Game1.pressUseToolButton),
                            typeof(ModEntry), nameof(ModEntry.Prefix_pressUseToolButton));
                
                patchPrefix(harmonyInstance, typeof(Character), nameof(Character.getGeneralDirectionTowards),
                            typeof(ModEntry), nameof(ModEntry.Prefix_getGeneralDirectionTowards));
                
                patchPrefix(harmonyInstance, typeof(Farmer), nameof(Farmer.draw),
                            typeof(ModEntry), nameof(ModEntry.Prefix_Farmer_draw),
                            new Type[] { typeof(SpriteBatch) });
                
                patchPostfix(harmonyInstance, typeof(Farmer), nameof(Farmer.draw),
                             typeof(ModEntry), nameof(ModEntry.Postfix_Farmer_draw),
                             new Type[] { typeof(SpriteBatch) });
                
                patchPrefix(harmonyInstance, typeof(SpriteBatch), nameof(SpriteBatch.Draw),
                            typeof(ModEntry), nameof(ModEntry.Prefix_SpriteBatch_Draw),
                            new Type[] { typeof(Texture2D), typeof(Vector2), typeof(Rectangle?), typeof(Color),
                                         typeof(float), typeof(Vector2), typeof(Vector2), typeof(SpriteEffects), typeof(float) });
                
                patchPrefix(harmonyInstance, typeof(Utility), nameof(Utility.playerCanPlaceItemHere),
                            typeof(ModEntry), nameof(ModEntry.Prefix_playerCanPlaceItemHere));
                
                patchPostfix(harmonyInstance, typeof(Utility), nameof(Utility.playerCanPlaceItemHere),
                                typeof(ModEntry), nameof(ModEntry.Postfix_playerCanPlaceItemHere));
                
                patchPrefix(harmonyInstance, typeof(Utility), nameof(Utility.withinRadiusOfPlayer),
                            typeof(ModEntry), nameof(ModEntry.Prefix_withinRadiusOfPlayer));
                
                patchPrefix(harmonyInstance, typeof(Utility), nameof(Utility.tileWithinRadiusOfPlayer),
                            typeof(ModEntry), nameof(ModEntry.Prefix_tileWithinRadiusOfPlayer));
                
                patchPostfix(harmonyInstance, typeof(MeleeWeapon), nameof(MeleeWeapon.getAreaOfEffect),
                             typeof(ModEntry), nameof(ModEntry.Postfix_getAreaOfEffect));
                
                patchPostfix(harmonyInstance, typeof(MeleeWeapon), nameof(MeleeWeapon.DoDamage),
                             typeof(ModEntry), nameof(ModEntry.Postfix_DoDamage));
                
                patchPrefix(harmonyInstance, typeof(GameLocation), nameof(GameLocation.damageMonster),
                            typeof(ModEntry), nameof(ModEntry.Prefix_damageMonster),
                            new Type[] { typeof(Rectangle), typeof(int), typeof(int), typeof(bool), typeof(float),
                                         typeof(int), typeof(float), typeof(float), typeof(bool), typeof(Farmer), typeof(bool) });
                
                patchPrefix(harmonyInstance, typeof(GameLocation), "isMonsterDamageApplicable",
                            typeof(ModEntry), nameof(ModEntry.Prefix_isMonsterDamageApplicable));
               
                patchPrefix(harmonyInstance, typeof(GameLocation), nameof(GameLocation.checkAction),
                            typeof(ModEntry), nameof(ModEntry.Prefix_checkAction));
                
                if (helper.ModRegistry.IsLoaded("Thor.HoeWaterDirection"))
                {
                    patchPostfix(harmonyInstance, null, "",
                                 typeof(ModEntry), nameof(ModEntry.Postfix_HandleChangeDirectoryImpl),
                                 null, "Thor.Stardew.Mods.HoeWaterDirection.ModEntry:HandleChangeDirectoryImpl");
                }
            }
            catch (Exception ex)
            {
                Log("Error in mod setup: " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }
        
        /// <summary>Attempts to patch the given source method with the given prefix method.</summary>
        /// <param name="harmonyInstance">The Harmony instance to patch with.</param>
        /// <param name="sourceClass">The class the source method is part of.</param>
        /// <param name="sourceName">The name of the source method.</param>
        /// <param name="patchClass">The class the patch method is part of.</param>
        /// <param name="patchName">The name of the patch method.</param>
        /// <param name="sourceParameters">The source method's parameter list, when needed for disambiguation.</param>
        /// <param name="sourceLiteralName">The source method given as a string, if type cannot be directly accessed.</param>
        void patchPrefix(Harmony harmonyInstance, System.Type sourceClass, string sourceName, System.Type patchClass, string patchName, Type[] sourceParameters = null, string sourceLiteralName = "")
        {
            try
            {
                MethodBase sourceMethod;
                if (sourceLiteralName != "")
                    sourceMethod = AccessTools.Method(sourceLiteralName, sourceParameters);
                else
                    sourceMethod = AccessTools.Method(sourceClass, sourceName, sourceParameters);
                
                HarmonyMethod prefixPatch = new HarmonyMethod(patchClass, patchName);
                
                if (sourceMethod != null && prefixPatch != null)
                    harmonyInstance.Patch(sourceMethod, prefixPatch);
                else
                {
                    if (sourceMethod == null)
                        Log("Warning: Source method (" + sourceClass.ToString() + "::" + sourceName + ") not found or ambiguous.");
                    if (prefixPatch == null)
                        Log("Warning: Patch method (" + patchClass.ToString() + "::" + patchName + ") not found.");
                }
            }
            catch (Exception ex)
            {
                Log("Error patching prefix method to " + sourceClass.Name + "." + sourceName + "." + Environment.NewLine + ex.InnerException + Environment.NewLine + ex.StackTrace);
            }
        }
        
        /// <summary>Attempts to patch the given source method with the given postfix method.</summary>
        /// <param name="harmonyInstance">The Harmony instance to patch with.</param>
        /// <param name="sourceClass">The class the source method is part of.</param>
        /// <param name="sourceName">The name of the source method.</param>
        /// <param name="patchClass">The class the patch method is part of.</param>
        /// <param name="patchName">The name of the patch method.</param>
        /// <param name="sourceParameters">The source method's parameter list, when needed for disambiguation.</param>
        /// <param name="sourceLiteralName">The source method given as a string, if type cannot be directly accessed.</param>
        void patchPostfix(Harmony harmonyInstance, Type sourceClass, string sourceName, Type patchClass, string patchName, Type[] sourceParameters = null, string sourceLiteralName = "")
        {
            try
            {
                MethodBase sourceMethod;
                if (sourceLiteralName != "")
                    sourceMethod = AccessTools.Method(sourceLiteralName, sourceParameters);
                else
                    sourceMethod = AccessTools.Method(sourceClass, sourceName, sourceParameters);
                
                HarmonyMethod postfixPatch = new HarmonyMethod(patchClass, patchName);
                
                if (sourceMethod != null && postfixPatch != null)
                    harmonyInstance.Patch(sourceMethod, postfix: postfixPatch);
                else
                {
                    if (sourceMethod == null)
                        Log("Warning: Source method (" + sourceClass.ToString() + "::" + sourceName + ") not found or ambiguous.");
                    if (postfixPatch == null)
                        Log("Warning: Patch method (" + patchClass.ToString() + "::" + patchName + ") not found.");
                }
            }
            catch (Exception ex)
            {
                Log("Error patching postfix method to " + sourceClass.Name + "." + sourceName + "." + Environment.NewLine + ex.InnerException + Environment.NewLine + ex.StackTrace);
            }
        }
        
        /********************************
         ** Config Menu Initialization **
         ********************************/
        
        /// <summary>Initializes menu for Generic Mod Config Menu on game launch.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnGameLaunched(object sender, GameLaunchedEventArgs e)
        {
            try
            {
                // Get Generic Mod Config Menu's API (if it's installed).
                var configMenu = Helper.ModRegistry.GetApi<IGenericModConfigMenuApi>("spacechase0.GenericModConfigMenu");
                if (configMenu is null)
                    return;
                
                // Register mod.
                configMenu.Register(mod: ModManifest, reset: () => Config = new ModConfig(), save: () => Helper.WriteConfig(Config));
                
                // Add options.
                configMenu.AddSectionTitle(mod: ModManifest, text: () => str.Get("headerRanges"));
                
                List<string> rangeList = new List<string>();
                rangeList.Add("1");
                rangeList.Add("-1");
                for (int i = 2; i <= 20; i++)
                    rangeList.Add(i.ToString());
                
                foreach (string subject in new string[] { "axe", "pickaxe", "hoe", "wateringCan", "scythe", "weapons", "seeds", "objects" })
                {
                    configMenu.AddTextOption(
                        mod: ModManifest,
                        name: () => str.Get("optionRangeName", new { subject = str.Get(subject + "ForRangeName") }),
                        tooltip: () => str.Get("optionRangeTooltip", new { subject = str.Get(subject + "ForRangeTooltip") }),
                        getValue: () =>
                        {
                            int value = 1;
                            switch (subject)
                            {
                                case "axe": value = Config.AxeRange; break;
                                case "pickaxe": value = Config.PickaxeRange; break;
                                case "hoe": value = Config.HoeRange; break;
                                case "wateringCan": value = Config.WateringCanRange; break;
                                case "scythe": value = Config.ScytheRange; break;
                                case "weapons": value = Config.WeaponRange; break;
                                case "seeds": value = Config.SeedRange; break;
                                case "objects": value = Config.ObjectPlaceRange; break;
                            }
                            return rangeList[value == 1? 0 // Default
                                           : value < 0? 1 // Unlimited
                                           : value]; // Extended
                        },
                        setValue: strValue =>
                        {
                            int value = 1;
                            if (strValue.Equals(rangeList[0]))
                                value = 1;
                            else if (strValue.Equals(rangeList[1]))
                                value = -1;
                            else
                            {
                                for (int i = 2; i <= 20; i++)
                                {
                                    if (strValue.Equals(rangeList[i]))
                                    {
                                        value = i;
                                        break;
                                    }
                                }
                            }
                            
                            switch (subject)
                            {
                                case "axe": Config.AxeRange = value; break;
                                case "pickaxe": Config.PickaxeRange = value; break;
                                case "hoe": Config.HoeRange = value; break;
                                case "wateringCan": Config.WateringCanRange = value; break;
                                case "scythe": Config.ScytheRange = value; break;
                                case "weapons": Config.WeaponRange = value; break;
                                case "seeds": Config.SeedRange = value; break;
                                case "objects": Config.ObjectPlaceRange = value; break;
                            }
                        },
                        allowedValues: rangeList.ToArray(),
                        formatAllowedValue: value =>
                        {
                            if (value.Equals("1"))
                                return str.Get("rangeDefault");
                            else if (value.Equals("-1"))
                                return str.Get("rangeUnlimited");
                            else
                                return str.Get("rangeExtended", new { tiles = value });
                        }
                    );
                }
                
                configMenu.AddSectionTitle(mod: ModManifest, text: () => str.Get("headerRangedSwings"));
                
                configMenu.AddBoolOption(
                    mod: ModManifest,
                    name: () => str.Get("optionScytheSwingOriginName"),
                    tooltip: () => str.Get("optionScytheSwingOriginTooltip"),
                    getValue: () => Config.CenterScytheOnCursor,
                    setValue: value => Config.CenterScytheOnCursor = value
                );
                
                configMenu.AddBoolOption(
                    mod: ModManifest,
                    name: () => str.Get("optionWeaponSwingOriginName"),
                    tooltip: () => str.Get("optionWeaponSwingOriginTooltip"),
                    getValue: () => Config.CenterWeaponOnCursor,
                    setValue: value => Config.CenterWeaponOnCursor = value
                );
                
                configMenu.AddSectionTitle(mod: ModManifest, text: () => str.Get("headerUseOnTile"));
                
                foreach (string tool in new string[] { "axe", "pickaxe", "hoe" })
                {
                    configMenu.AddBoolOption(
                        mod: ModManifest,
                        name: () => str.Get("optionSelfUsabilityName", new { tool = str.Get(tool + "ForUsabilityName") }),
                        tooltip: () => str.Get("optionSelfUsabilityTooltip", new { tool = str.Get(tool + "ForUsabilityTooltip") }),
                        getValue: () =>
                        {
                            switch (tool)
                            {
                                case "axe": return Config.AxeUsableOnPlayerTile;
                                case "pickaxe": return Config.PickaxeUsableOnPlayerTile;
                                case "hoe": return Config.HoeUsableOnPlayerTile;
                            }
                            return true;
                        },
                        setValue: value =>
                        {
                            switch (tool)
                            {
                                case "axe": Config.AxeUsableOnPlayerTile = value; break;
                                case "pickaxe": Config.PickaxeUsableOnPlayerTile = value; break;
                                case "hoe": Config.HoeUsableOnPlayerTile = value; break;
                            }
                        }
                    );
                }
                
                configMenu.AddSectionTitle(mod: ModManifest, text: () => str.Get("headerFaceClick"));
                
                configMenu.AddBoolOption(
                    mod: ModManifest,
                    name: () => str.Get("optionToolFaceClickName"),
                    tooltip: () => str.Get("optionToolFaceClickTooltip"),
                    getValue: () => Config.ToolAlwaysFaceClick,
                    setValue: value => Config.ToolAlwaysFaceClick = value
                );
                
                configMenu.AddBoolOption(
                    mod: ModManifest,
                    name: () => str.Get("optionWeaponFaceClickName"),
                    tooltip: () => str.Get("optionWeaponFaceClickTooltip"),
                    getValue: () => Config.WeaponAlwaysFaceClick,
                    setValue: value => Config.WeaponAlwaysFaceClick = value
                );
                
                configMenu.AddSectionTitle(mod: ModManifest, text: () => str.Get("headerMisc"));
                
                configMenu.AddTextOption(
                    mod: ModManifest,
                    name: () => str.Get("optionToolHitLocationName"),
                    tooltip: () => str.Get("optionToolHitLocationTooltip"),
                    getValue: () => Config.ToolHitLocationDisplay.ToString(),
                    setValue: value => Config.ToolHitLocationDisplay = int.Parse(value),
                    allowedValues: new string[] { "0", "1", "2" },
                    formatAllowedValue: value =>
                    {
                        switch (value)
                        {
                            case "0": return str.Get("locationLogicOriginal");
                            case "1": default: return str.Get("locationLogicNew");
                            case "2": return str.Get("locationLogicCombined");
                        }
                    }
                );
                
                configMenu.AddBoolOption(
                    mod: ModManifest,
                    name: () => str.Get("optionHalfTilePositionsName"),
                    tooltip: () => str.Get("optionHalfTilePositionsTooltip"),
                    getValue: () => Config.UseHalfTilePositions,
                    setValue: value => Config.UseHalfTilePositions = value
                );
                
                configMenu.AddBoolOption(
                    mod: ModManifest,
                    name: () => str.Get("optionAllowRangedChargeName"),
                    tooltip: () => str.Get("optionAllowRangedChargeTooltip"),
                    getValue: () => Config.AllowRangedChargeEffects,
                    setValue: value => Config.AllowRangedChargeEffects = value
                );
                
                configMenu.AddBoolOption(
                    mod: ModManifest,
                    name: () => str.Get("optionAttacksIgnoreObstaclesName"),
                    tooltip: () => str.Get("optionAttacksIgnoreObstaclesTooltip"),
                    getValue: () => Config.AttacksIgnoreObstacles,
                    setValue: value => Config.AttacksIgnoreObstacles = value
                );
                
                configMenu.AddBoolOption(
                    mod: ModManifest,
                    name: () => str.Get("optionDontCutGrassName"),
                    tooltip: () => str.Get("optionDontCutGrassTooltip"),
                    getValue: () => Config.DontCutGrassPastNormalRange,
                    setValue: value => Config.DontCutGrassPastNormalRange = value
                );
                
                configMenu.AddBoolOption(
                    mod: ModManifest,
                    name: () => str.Get("optionMultigrabCrabPotsInRangeName"),
                    tooltip: () => str.Get("optionMultigrabCrabPotsInRangeTooltip"),
                    getValue: () => Config.MultigrabCrabPotsInRange,
                    setValue: value => Config.MultigrabCrabPotsInRange = value
                );
                
                configMenu.AddBoolOption(
                    mod: ModManifest,
                    name: () => str.Get("optionOnClickOnlyName"),
                    tooltip: () => str.Get("optionOnClickOnlyTooltip"),
                    getValue: () => Config.CustomRangeOnClickOnly,
                    setValue: value => Config.CustomRangeOnClickOnly = value
                );
            }
            catch (Exception ex)
            {
                Log("Error setting up mod config menu (menu may not appear): " + ex.InnerException + Environment.NewLine + ex.StackTrace);
            }
        }
        
        /******************
         ** Input Method **
         ******************/
        
        /// <summary>Checks whether to enable ToolLocation override when a Tool Button is pressed.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnButtonPressed(object sender, ButtonPressedEventArgs e)
        {
            try
            {
                if (!Context.IsWorldReady || Game1.player == null) // If no save is loaded yet or player doesn't exist
                    return;
                
                if (!isAppropriateTimeToUseTool()) // Tools not allowed in various circumstances
                    return;
                
                bool withClick = e.Button.ToString().Contains("Mouse");
                
                // Tool Button was pressed; required to be mouse button if that setting is enabled.
                if (e.Button.IsUseToolButton() && (withClick || !Config.CustomRangeOnClickOnly))
                {
                    Farmer player = Game1.player;
                    if (player != null && player.CurrentTool != null && !player.UsingTool) // Have a tool selected, not in the middle of using it
                    {
                        Vector2 mousePosition = e.Cursor.AbsolutePixels;
                        
                        // If setting is enabled, face all mouse clicks when a tool/weapon is equipped.
                        if (withClick && shouldToolTurnToFace(player.CurrentTool))
                            player.faceGeneralDirection(mousePosition);
                        
                        // Begin tool location override, setting it to current mouse position if in range.
                        specialClickActive = true;
                        if (positionValidForExtendedRange(player, mousePosition))
                            specialClickLocation = mousePosition;
                        else
                            specialClickLocation = Vector2.Zero;
                        
                        if (!knownToolButtons.Contains(e.Button)) // Keep a list of Tool Buttons (accounting for click-only option)
                            knownToolButtons.Add(e.Button);
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Error in button press: " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }
        
        /// <summary>Checks for mouse drag while holding any Tool Button.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnCursorMoved(object sender, CursorMovedEventArgs e)
        {
            try
            {
                // Update override location as long as a Tool Button is held.
                if (Game1.player != null && specialClickActive && holdingToolButton())
                {
                    if (positionValidForExtendedRange(Game1.player, e.NewPosition.AbsolutePixels)) // Update if in a valid range
                        specialClickLocation = e.NewPosition.AbsolutePixels;
                }
            }
            catch (Exception ex)
            {
                Log("Error in cursor move: " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }
        
        /// <summary>Checks whether the given Farmer and mouse position are within extended range for the current tool.</summary>
        /// <param name="who">The Farmer using the tool.</param>
        /// <param name="mousePosition">The position of the mouse.</param>
        public static bool positionValidForExtendedRange(Farmer who, Vector2 mousePosition)
        {
            if (mousePosition.Equals(Vector2.Zero)) // Not set, not valid
                return false;
            
            Tool currentTool = who.CurrentTool;
            
            if (isToolOverridable(currentTool)) // Only override ToolLocation for applicable tools
            {
                int range = getCustomRange(currentTool);
                
                // Mouse position is within tool's range, or range setting is negative (infinite).
                if (range < 0 || Utility.withinRadiusOfPlayer((int)mousePosition.X, (int)mousePosition.Y, range, who))
                {
                    bool usableOnPlayerTile = getPlayerTileSetting(currentTool);
                    
                    // If not allowed to use on player tile, ensure mouse is not within radius 0 of player.
                    if (usableOnPlayerTile || !Utility.withinRadiusOfPlayer((int)mousePosition.X, (int)mousePosition.Y, 0, who))
                        return true;
                }
            }
            
            return false;
        }
        
        /// <summary>Returns whether a known Tool Button is still being held.</summary>
        /// <param name="clickOnly">Whether to only check for mouse Tool Buttons.</param>
        public static bool holdingToolButton(bool clickOnly = false)
        {
            foreach (SButton button in knownToolButtons)
                if (myInput.IsDown(button)
                 && (!clickOnly || button.ToString().Contains("Mouse"))
                 && button.IsUseToolButton()) // Double-check in case it changed
                    return true;
            
            return false;
        }
        
        /// <summary>Returns whether tools can be used currently. Does not check whether player is in the middle of using tool.</summary>
        public static bool isAppropriateTimeToUseTool()
        {
            return !Game1.fadeToBlack
                && !Game1.dialogueUp
                && !Game1.eventUp
                && Game1.currentMinigame == null
                && !Game1.player.hasMenuOpen.Value
                && !Game1.player.isRidingHorse()
                && (Game1.CurrentEvent == null || Game1.CurrentEvent.canPlayerUseTool());
        }
        
        /// <summary>Returns whether the given tool is a type that supports ToolLocation override.</summary>
        /// <param name="tool">The Tool being checked.</param>
        public static bool isToolOverridable(Tool tool)
        {
            return tool is Axe
                || tool is Pickaxe
                || tool is Hoe
                || tool is WateringCan;
        }
        
        /// <summary>Returns whether the given tool is a type that should face mouse clicks (i.e. sprite won't glitch).</summary>
        /// <param name="tool">The Tool being checked.</param>
        public static bool shouldToolTurnToFace(Tool tool, bool buttonHeld = false)
        {
            return (tool is Axe && Config.ToolAlwaysFaceClick)
                || (tool is Pickaxe && Config.ToolAlwaysFaceClick)
                || (tool is Hoe && Config.ToolAlwaysFaceClick && !buttonHeld)
                || (tool is WateringCan && Config.ToolAlwaysFaceClick && !buttonHeld)
                || (tool is MeleeWeapon && Config.WeaponAlwaysFaceClick && !buttonHeld);
        }
        
        /// <summary>Returns custom range setting for overridable tools (1 for any others).</summary>
        /// <param name="tool">The Tool being checked.</param>
        public static int getCustomRange(Tool tool)
        {
            return tool is Axe? Config.AxeRange
                 : tool is Pickaxe? Config.PickaxeRange
                 : tool is Hoe? Config.HoeRange
                 : tool is WateringCan? Config.WateringCanRange
                 : 1;
        }
       
        /// <summary>Returns "usable on player tile" setting for overridable tools (true for any others).</summary>
        /// <param name="tool">The Tool being checked.</param>
        public static bool getPlayerTileSetting(Tool tool)
        {
            return tool is Axe? Config.AxeUsableOnPlayerTile
                 : tool is Pickaxe? Config.PickaxeUsableOnPlayerTile
                 : tool is Hoe? Config.HoeUsableOnPlayerTile
                 : true;
        }
        
        /********************
         ** Method Patches **
         ********************/
        
        /// <summary>Prefix to Farmer.useTool that updates specialClickActive just before use.</summary>
        /// <param name="who">The Farmer using the tool.</param>
        public static bool Prefix_useTool(Farmer who)
        {
            try
            {
                if (!positionValidForExtendedRange(who, specialClickLocation) // Disable override if target position is out of range
                 || (who.toolPower.Value > 0 && !Config.AllowRangedChargeEffects)) // Disable override for charged tool use unless enabled
                    specialClickActive = false;
                else if (holdingToolButton()) // Itherwise, force use of override as long as a Tool Button is being held
                    specialClickActive = true;
                
                return true; // Go to original function
            }
            catch (Exception ex)
            {
                Log("Error in useTool: " + ex.Message + Environment.NewLine + ex.StackTrace);
                return true; // Go to original function
            }
        }
        
        /// <summary>Postfix to Farmer.useTool that disables override after use if button has been let go.</summary>
        public static void Postfix_useTool()
        {
            if (!holdingToolButton())
                specialClickActive = false;
        }
        
        /// <summary>Prefix to Character.GetToolLocation function that overrides it with click location.</summary>
        /// <param name="__result">The result of the function.</param>
        public static bool Prefix_GetToolLocation(ref Vector2 __result)
        {
            // If tool has been charged and ranged charge option is not enabled, disable override.
            if (Game1.player != null && Game1.player.toolPower.Value > 0 && !Config.AllowRangedChargeEffects)
            {
                specialClickActive = false;
                return true; // Go to original function
            }
            
            if (specialClickActive && !specialClickLocation.Equals(Vector2.Zero) && !disableToolLocationOverride)
            {
                __result = specialClickLocation;
                return false; // Don't do original function anymore
            }
            return true; // Go to original function
        }
        
        /// <summary>Rewrite of Utility.isWithinTileWithLeeway that returns true if within object/seed custom range.</summary>
        /// <param name="x">The X location.</param>
        /// <param name="y">The Y location.</param>
        /// <param name="item">The item being placed.</param>
        /// <param name="f">The Farmer placing it.</param>
        /// <param name="__result">The returned result.</param>
        public static bool Prefix_isWithinTileWithLeeway(int x, int y, Item item, Farmer f, ref bool __result)
        {
            try
            {
                // Base game relies on short range to prevent placing Crab Pots in unreachable places, so always use default range.
                if (item.QualifiedItemId == "(O)710") // Crab Pot
                    return true; // Go to original function
                
                // Though original behavior shows green when placing Tapper as long as highlighted tile is in range,
                // this becomes particularly confusing at longer range settings, so check that there is in fact an empty tree.
                if (item.QualifiedItemId == "(BC)105") // Tapper
                {
                    Vector2 tile = new Vector2(x / 64, y / 64);
                    if (!f.currentLocation.terrainFeatures.ContainsKey(tile) // No special terrain at tile
                     || !(f.currentLocation.terrainFeatures[tile] is Tree) // Terrain at tile is not a tree
                     || f.currentLocation.objects.ContainsKey(tile)) // Tree tile is already occupied
                    {
                        __result = false;
                        return false; // Don't do original function anymore
                    }
                }
                
                int range = item.Category == StardewValley.Object.SeedsCategory
                         || item.Category == StardewValley.Object.fertilizerCategory? Config.SeedRange
                                                                                    : Config.ObjectPlaceRange;
                
                if (range < 0 || Utility.withinRadiusOfPlayer(x, y, range, f))
                {
                    __result = true;
                    return false; // Don't do original function anymore
                }
                return true; // Go to original function
            }
            catch (Exception ex)
            {
                Log("Error in isWithinTileWithLeeway: " + ex.Message + Environment.NewLine + ex.StackTrace);
                return true; // Go to original function
            }
        }
        
        /// <summary>Turns player to face mouse if Tool Button is being held. If not desired, set override to keep direction.</summary>
        public static bool Prefix_pressUseToolButton()
        {
            try
            {
                if (Game1.player == null) // Go to original function
                    return true;
                
                mouseFacingOverride = false;
                if (specialClickActive && !specialClickLocation.Equals(Vector2.Zero)
                 && holdingToolButton(true) && shouldToolTurnToFace(Game1.player.CurrentTool, true))
                    Game1.player.faceGeneralDirection(specialClickLocation);
                else
                    mouseFacingOverride = true;
                
                return true; // Go to original function
            }
            catch (Exception ex)
            {
                Log("Error in pressUseToolButton: " + ex.Message + Environment.NewLine + ex.StackTrace);
                return true; // Go to original function
            }
        }
        
        /// <summary>When override is set by pressUseToolButton, return current direction so it doesn't change.</summary>
        /// <param name="__instance">The instance of the Character.</param>
        /// <param name="__result">The returned direction.</param>
        public static bool Prefix_getGeneralDirectionTowards(Character __instance, ref int __result)
        {
            try
            {
                if (mouseFacingOverride)
                {
                    __result = __instance.FacingDirection;
                    mouseFacingOverride = false;
                    return false; // Don't do original function anymore
                }
                
                return true; // Go to original function
            }
            catch (Exception ex)
            {
                Log("Error in getGeneralDirectionTowards: " + ex.Message + Environment.NewLine + ex.StackTrace);
                return true; // Go to original function
            }
        }
        
        /// <summary>Prefix to Farmer.draw that takes over drawing of Tool Hit Location indicator.</summary>
        /// <param name="__instance">The instance of the Farmer.</param>
        /// <param name="b">The sprite batch.</param>
        public static bool Prefix_Farmer_draw(Farmer __instance, SpriteBatch b)
        {
            try
            {
                if (__instance.toolPower.Value > 0) // If charging tool, just use original function
                    return true; // Go to original function
                
                // Abort cases from original function
                if (__instance.currentLocation == null
                 || (!__instance.currentLocation.Equals(Game1.currentLocation)
                  && !__instance.IsLocalPlayer
                  && !Game1.currentLocation.Name.Equals("Temp")
                  && !__instance.isFakeEventActor)
                 || (((NetFieldBase<bool, NetBool>)__instance.hidden).Value
                  && (__instance.currentLocation.currentEvent == null || __instance != __instance.currentLocation.currentEvent.farmer)
                  && (!__instance.IsLocalPlayer || Game1.locationRequest == null))
                 || (__instance.viewingLocation.Value != null
                  && __instance.IsLocalPlayer))
                    return true; // Go to original function
                
                // Conditions for drawing tool hit indicator
                if (Game1.activeClickableMenu == null
                 && !Game1.eventUp
                 && (__instance.IsLocalPlayer && __instance.CurrentTool != null)
                 && (Game1.oldKBState.IsKeyDown(Keys.LeftShift) || Game1.options.alwaysShowToolHitLocation)
                 && __instance.CurrentTool.doesShowTileLocationMarker()
                 && (!Game1.options.hideToolHitLocationWhenInMotion || !__instance.isMoving()))
                {
                    Vector2 mousePosition = Utility.PointToVector2(Game1.getMousePosition()) 
                                            + new Vector2((float)Game1.viewport.X, (float)Game1.viewport.Y);
                    
                    disableToolLocationOverride = true; // Use old logic for GetToolLocation
                    Vector2 limitedLocal = Game1.GlobalToLocal(Game1.viewport, Utility.clampToTile(__instance.GetToolLocation(mousePosition)));
                    disableToolLocationOverride = false;
                    Vector2 extendedLocal = Game1.GlobalToLocal(Game1.viewport, Utility.clampToTile(mousePosition));
                    
                    bool drawnExtended = false;
                    if (Config.ToolHitLocationDisplay == 1 || Config.ToolHitLocationDisplay == 2) // New Logic or Combined
                    {
                        if (positionValidForExtendedRange(__instance, mousePosition)) // Only draw at this range if it's valid
                        {
                            drawnExtended = true;
                            b.Draw(Game1.mouseCursors, extendedLocal,
                                   new Rectangle?(Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 29)),
                                   Color.White, 0.0f, Vector2.Zero, 1f, SpriteEffects.None, extendedLocal.Y / 10000f);
                        }
                    }
                    
                    if (!drawnExtended || !extendedLocal.Equals(limitedLocal)) // Don't draw in same position twice
                    {
                        if (!drawnExtended // Always draw original if extended position wasn't valid
                         || Config.ToolHitLocationDisplay == 0 || Config.ToolHitLocationDisplay == 2) // Old Logic or Combined
                        {
                            b.Draw(Game1.mouseCursors, limitedLocal,
                                   new Rectangle?(Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 29)),
                                   Color.White, 0.0f, Vector2.Zero, 1f, SpriteEffects.None, limitedLocal.Y / 10000f);
                        }
                    }
                    
                    // Specifically prevent drawing of original indicator by SpriteBatch.Draw().
                    preventDraw = true;
                    preventDrawTexture = Game1.mouseCursors;
                    preventDrawSourceRect = new Rectangle?(Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 29));
                }
                return true; // Go to original function
            }
            catch (Exception ex)
            {
                Log("Error in Farmer draw: " + ex.Message + Environment.NewLine + ex.StackTrace);
                return true; // Go to original function
            }
        }
        
        /// <summary>Postfix to Farmer.draw that resets preventDraw override.</summary>
        public static void Postfix_Farmer_draw()
        {
            preventDraw = false;
        }
        
        /// <summary>Prefix to SpriteBatch.Draw that cancels it (when enabled) if arguments match the established parameters.</summary>
        /// <param name="__instance">The instance of the SpriteBatch.</param>
        /// <param name="texture">The base texture.</param>
        /// <param name="position">The position to draw at.</param>
        /// <param name="sourceRectangle">The area of the texture to use.</param>
        /// <param name="color">The color to draw with.</param>
        /// <param name="rotation">The rotation to draw with.</param>
        /// <param name="origin">The origin point.</param>
        /// <param name="scale">The scale to draw at.</param>
        /// <param name="effects">Effects to draw with.</param>
        /// <param name="layerDepth">Depth to draw at.</param>
        public static bool Prefix_SpriteBatch_Draw(SpriteBatch __instance, Texture2D texture, Vector2 position, Rectangle? sourceRectangle,
            Color color, float rotation, Vector2 origin, Vector2 scale, SpriteEffects effects, float layerDepth)
        {
            try
            {
                if (preventDraw && preventDrawTexture.Equals(texture) && preventDrawSourceRect.Equals(sourceRectangle))
                    return false; // Don't do original function anymore
                
                return true; // Go to original function
            }
            catch (Exception ex)
            {
                Log("Error in SpriteBatch Draw: " + ex.Message + Environment.NewLine + ex.StackTrace);
                return true; // Go to original function
            }
        }
        
        /// <summary>Prefix to Utility.playerCanPlaceItemHere to aid in mod compatibility.
        /// Some methods add postfixes to use withinRadiusOfPlayer, so this sets an override for it.</summary>
        /// <param name="location">The location being tested.</param>
        /// <param name="item">The object being tested.</param>
        /// <param name="x">The X position.</param>
        /// <param name="y">The Y position.</param>
        /// <param name="f">The Farmer placing the object.</param>
        public static bool Prefix_playerCanPlaceItemHere(GameLocation location, Item item, int x, int y, Farmer f)
        {
            try
            {
                // Base game relies on short range to prevent placing Crab Pots in unreachable places, so always use default range.
                if (item.QualifiedItemId == "(O)710") // Crab Pot
                {
                    tileRadiusOverride = 0;
                    return true; // Go to original function
                }
                
                tileRadiusOverride = item.Category == StardewValley.Object.SeedsCategory
                                  || item.Category == StardewValley.Object.fertilizerCategory? Config.SeedRange
                                                                                             : Config.ObjectPlaceRange;
                
                return true; // Go to original function
            }
            catch (Exception ex)
            {
                Log("Error in playerCanPlaceItemHere: " + ex.Message + Environment.NewLine + ex.StackTrace);
                return true; // Go to original function
            }
        }
        
        /// <summary>Postfix to Utility.playerCanPlaceItemHere to reset override set by prefix.</summary>
        public static void Postfix_playerCanPlaceItemHere()
        {
            tileRadiusOverride = 0;
        }
        
        /// <summary>Rewrite of Utility.withinRadiusOfPlayer to add an override for the tileRadius argument.</summary>
        /// <param name="__result">The result of the function.</param>
        /// <param name="x">The X position.</param>
        /// <param name="y">The Y position.</param>
        /// <param name="tileRadius">The allowed radius, overriden if tileRadiusOverride is set.</param>
        /// <param name="f">The Farmer placing the object.</param>
        public static bool Prefix_withinRadiusOfPlayer(ref bool __result, int x, int y, int tileRadius, Farmer f)
        {
            try
            {
                if (tileRadiusOverride == -1)
                {
                    __result = true;
                    return false; // Don't do original function anymore
                }
                
                if (tileRadiusOverride != 0)
                    tileRadius = tileRadiusOverride;
                
                Point point = new Point(x / 64, y / 64);
                if (!Config.UseHalfTilePositions) // Standard method: Round player's position down to nearest tile
                {
                    Vector2 tileLocation = f.Tile;
                    __result = (double)Math.Abs((float)point.X - tileLocation.X) <= (double)tileRadius && (double)Math.Abs((float)point.Y - tileLocation.Y) <= (double)tileRadius;
                }
                else // New method: Determine extents of tiles in range based on player position rounded favorably up/down
                {
                    // Round player position to nearest half-tile (i.e. 0, 0.5, 1, 1.5, 2, 2.5...).
                    Vector2 playerPosition = new Vector2((float)Math.Round(f.position.Value.X / 32f) / 2f,
                                                         (float)Math.Round(f.position.Value.Y / 32f) / 2f);
                    
                    // Determine the tiles on the edge of the range, rounding down for minimums and up for maximums.
                    int minX = (int)playerPosition.X - tileRadius;
                    int minY = (int)playerPosition.Y - tileRadius;
                    int maxX = (int)Math.Ceiling(playerPosition.X) + tileRadius;
                    int maxY = (int)Math.Ceiling(playerPosition.Y) + tileRadius;
                    
                    __result = point.X >= minX && point.X <= maxX && point.Y >= minY && point.Y <= maxY;
                }
                return false; // Don't do original function anymore
            }
            catch (Exception ex)
            {
                Log("Error in withinRadiusOfPlayer: " + ex.Message + Environment.NewLine + ex.StackTrace);
                return true; // Go to original function
            }
        }

        /// <summary>Rewrite of Utility.tileWithinRadiusOfPlayer to add an override for the tileRadius argument.</summary>
        /// <param name="__result">The result of the function.</param>
        /// <param name="xTile">The tile X coordinate.</param>
        /// <param name="yTile">The tile Y coordinate.</param>
        /// <param name="tileRadius">The allowed radius, overriden if tileRadiusOverride is set.</param>
        /// <param name="f">The Farmer placing the object.</param>
        public static bool Prefix_tileWithinRadiusOfPlayer(ref bool __result, int xTile, int yTile, int tileRadius, Farmer f)
        {
            try
            {
                if (tileRadiusOverride == -1)
                {
                    __result = true;
                    return false; // Don't do original function anymore
                }
                
                if (tileRadiusOverride != 0)
                    tileRadius = tileRadiusOverride;
                
                Point point = new Point(xTile, yTile);
                if (!Config.UseHalfTilePositions) // Standard method: Round player's position down to nearest tile
                {
                    Vector2 tileLocation = f.Tile;
                    __result = (double)Math.Abs((float)point.X - tileLocation.X) <= (double)tileRadius && (double)Math.Abs((float)point.Y - tileLocation.Y) <= (double)tileRadius;
                }
                else // New method: Determine extents of tiles in range based on player position rounded favorably up/down
                {
                    // Round player position to nearest half-tile (i.e. 0, 0.5, 1, 1.5, 2, 2.5...).
                    Vector2 playerPosition = new Vector2((float)Math.Round(f.position.Value.X / 32f) / 2f,
                                                         (float)Math.Round(f.position.Value.Y / 32f) / 2f);
                    
                    // Determine the tiles on the edge of the range, rounding down for minimums and up for maximums.
                    int minX = (int)playerPosition.X - tileRadius;
                    int minY = (int)playerPosition.Y - tileRadius;
                    int maxX = (int)Math.Ceiling(playerPosition.X) + tileRadius;
                    int maxY = (int)Math.Ceiling(playerPosition.Y) + tileRadius;
                    
                    __result = point.X >= minX && point.X <= maxX && point.Y >= minY && point.Y <= maxY;
                }
                return false; // Don't do original function anymore
            }
            catch (Exception ex)
            {
                Log("Error in tileWithinRadiusOfPlayer: " + ex.Message + Environment.NewLine + ex.StackTrace);
                return true; // Go to original function
            }
        }
        
        /// <summary>Postfix to MeleeWeapon.getAreaOfEffect that shifts effect area of scythes/melee weapons to cursor if enabled.</summary>
        /// <param name="__result">Rectangle for the area of effect.</param>
        /// <param name="x">Farmer X position.</param>
        /// <param name="y">Farmer Y position.</param>
        /// <param name="facingDirection">Farmer facing direction.</param>
        /// <param name="tileLocation1">Central position in rectangle.</param>
        /// <param name="tileLocation2">Central position in rectangle.</param>
        /// <param name="wielderBoundingBox">Farmer bounds rectangle.</param>
        /// <param name="indexInCurrentAnimation">Frame of tool/weapon use animation.</param>
        public static void Postfix_getAreaOfEffect(MeleeWeapon __instance, ref Rectangle __result, int x, int y, int facingDirection,
            ref Vector2 tileLocation1, ref Vector2 tileLocation2, Rectangle wielderBoundingBox, int indexInCurrentAnimation)
        {
            bool centerOnCursor = __instance.isScythe()? Config.CenterScytheOnCursor : Config.CenterWeaponOnCursor;
            if (centerOnCursor)
            {
                Vector2 mousePosition = Utility.PointToVector2(Game1.getMousePosition()) 
                                      + new Vector2((float)Game1.viewport.X, (float)Game1.viewport.Y);
                __result.X = (int)mousePosition.X - __result.Width / 2;
                __result.Y = (int)mousePosition.Y - __result.Height / 2;
            }
        }
        
        /// <summary>Postfix to MeleeWeapon.DoDamage to make scythe affect not just borders of rectangle, but anything inside it.</summary>
        /// <param name="__instance">The MeleeWeapon object.</param>
        /// <param name="location">Target location.</param>
        /// <param name="x">Target X position.</param>
        /// <param name="y">Target Y position.</param>
        /// <param name="facingDirection">Farmer facing direction.</param>
        /// <param name="power">Power of the attack.</param>
        /// <param name="who">The attacking Farmer.</param>
        public static void Postfix_DoDamage(MeleeWeapon __instance, GameLocation location, int x, int y, int facingDirection, int power, Farmer who)
        {
            int myRange = __instance.isScythe()? Config.ScytheRange : Config.WeaponRange;
            if (myRange == 1) // Default behavior
                return;
            
            if (!who.IsLocalPlayer)
                return;
            
            // After DoDamage has acted on normal area of effect, take that area and expand it.
            Rectangle areaOfEffect = __instance.mostRecentArea;
            
            if (myRange < 0) // Infinite
            {
                areaOfEffect.X = 0;
                areaOfEffect.Y = 0;
                areaOfEffect.Width = Game1.currentLocation.map.DisplayWidth;
                areaOfEffect.Height = Game1.currentLocation.map.DisplayHeight;
            }
            else if (myRange > 1)
                areaOfEffect.Inflate((myRange - 1) * 64, (myRange - 1) * 64);
            
            string cueName = "";
            
            foreach (Vector2 terrainKey in location.terrainFeatures.Keys)
            {
                // Terrain must be in effect range, or range must be infinite.
                if (myRange > 1 && !areaOfEffect.Contains(Vector2.Multiply(terrainKey, 64)))
                    continue;
                
                // Grass option keeps grass from being cut beyond normal range.
                if (Config.DontCutGrassPastNormalRange && location.terrainFeatures[terrainKey] is Grass)
                    continue;
                
                // Perform tool action on terrain.
                if (location.terrainFeatures[terrainKey].performToolAction(__instance, 0, terrainKey))
                    location.terrainFeatures.Remove(terrainKey);
            }
            
            foreach (Vector2 objectKey in location.objects.Keys)
            {
                // Terrain must be in effect range, or range must be infinite.
                if (myRange > 1 && !areaOfEffect.Contains(Vector2.Multiply(objectKey, 64)))
                    continue;
                
                // Perform tool action on object.
                if (location.objects[objectKey].performToolAction(__instance))
                    location.objects.Remove(objectKey);
            }
            
            // Perform tool action on every tile in range for miscellaneous actions.
            for (int tileX = areaOfEffect.Left; tileX < areaOfEffect.Right; tileX += 64)
                for (int tileY = areaOfEffect.Top; tileY < areaOfEffect.Bottom; tileY += 64)
                    location.performToolAction(__instance, tileX / 64, tileY / 64);
            
            if (!cueName.Equals(""))
                Game1.playSound(cueName);
        }

        /// <summary>Prefix to GameLocation.damageMonster to inflate areaOfEffect for melee attacks.</summary>
        /// <param name="areaOfEffect">The area affected by the attack.</param>
        /// <param name="minDamage">Minimum damage of attack.</param>
        /// <param name="maxDamage">Maximum damage of attack.</param>
        /// <param name="isBomb">Whether the damage is being done by a bomb.</param>
        /// <param name="knockBackModifier">Amount of knockback done to monster.</param>
        /// <param name="addedPrecision">Precision for bomb hitbox.</param>
        /// <param name="critChance">Chance of critical hit.</param>
        /// <param name="critMultiplier">Damage multiplier for critical hit.</param>
        /// <param name="triggerMonsterInvincibleTimer">Whether to give monster invincibility cooldown.</param>
        /// <param name="who">The attacking Farmer.</param>
        public static bool Prefix_damageMonster(ref Rectangle areaOfEffect, int minDamage, int maxDamage, bool isBomb, float knockBackModifier, int addedPrecision, float critChance, float critMultiplier, bool triggerMonsterInvincibleTimer, Farmer who)
        {
            try
            {
                if (isBomb) // Don't change bomb radius
                    return true; // Go to original function
                
                int myRange = Config.WeaponRange;
                if (myRange == 1) // Default behavior
                    return true; // Go to original function
                
                if (!who.IsLocalPlayer)
                    return true; // Go to original function
                
                if (myRange < 0) // Infinite
                {
                    areaOfEffect.X = 0;
                    areaOfEffect.Y = 0;
                    areaOfEffect.Width = Game1.currentLocation.map.DisplayWidth;
                    areaOfEffect.Height = Game1.currentLocation.map.DisplayHeight;
                }
                else if (myRange > 1)
                    areaOfEffect.Inflate((myRange - 1) * 64, (myRange - 1) * 64);
                
                return true; // Go to original function
            }
            catch (Exception ex)
            {
                Log("Error in damageMonster: " + ex.Message + Environment.NewLine + ex.StackTrace);
                return true; // Go to original function
            }
        }
        
        /// <summary>Prefix to GameLocation.isMonsterDamageApplicable that overrides it if the setting to ignore obstacles is enabled.</summary>
        /// <param name="__instance">The current GameLocation.</param>
        /// <param name="__result">The result of the function.</param>
        /// <param name="who">The attacking Farmer.</param>
        /// <param name="monster">The monster in question.</param>
        /// <param name="horizontalBias">Whether attack is more horizontal than vertical.</param>
        public static bool Prefix_isMonsterDamageApplicable(GameLocation __instance, ref bool __result, Farmer who, Monster monster, bool horizontalBias = true)
        {
            try
            {
                if (Config.AttacksIgnoreObstacles)
                {
                    __result = true;
                    return false; // Don't do original function anymore
                }
                return true; // Go to original function
            }
            catch (Exception ex)
            {
                Log("Error in isMonsterDamageApplicable: " + ex.Message + Environment.NewLine + ex.StackTrace);
                return true; // Go to original function
            }
        }
        
        /// <summary>Prefix to GameLocation.checkAction that makes additional checks for Crab Pots at a distance.</summary>
        /// <param name="__instance">The current GameLocation.</param>
        /// <param name="tileLocation">The tile being acted upon.</param>
        /// <param name="viewport">The viewport of the screen.</param>
        /// <param name="who">The acting Farmer.</param>
        public static bool Prefix_checkAction(GameLocation __instance, xTile.Dimensions.Location tileLocation, xTile.Dimensions.Rectangle viewport, Farmer who)
        {
            try
            {
                if (!Config.MultigrabCrabPotsInRange)
                    return true; // Go to original function
                
                // Go through all keys in objects looking for Crab Pots, and check if the tile is within range to be acted upon.
                int objectRadius = Config.ObjectPlaceRange;
                Vector2 playerLocation = who.Tile;
                foreach (Vector2 objectKey in __instance.objects.Keys)
                {
                    StardewValley.Object obj = __instance.objects[objectKey];
                    if (obj.ParentSheetIndex == 710) // Crab Pot
                    {
                        if (objectKey.X == tileLocation.X && objectKey.Y == tileLocation.Y)
                            continue;
                        
                        if (objectRadius == -1
                         || (Math.Abs(objectKey.X - playerLocation.X) <= objectRadius
                          && Math.Abs(objectKey.Y - playerLocation.Y) <= objectRadius))
                            obj.checkForAction(who);
                    }
                }
                return true; // Go to original function
            }
            catch (Exception ex)
            {
                Log("Error in checkAction: " + ex.Message + Environment.NewLine + ex.StackTrace);
                return true; // Go to original function
            }
        }
        
        /// <summary>Postfix to function in Hoe and Water Direction mod that shifts affected tiles to match target location.</summary>
        /// <param name="__result">The result of the function.</param>
        /// <param name="tileLocation">The targeted location.</param>
        public static void Postfix_HandleChangeDirectoryImpl(ref List<Vector2> __result, ref Vector2 tileLocation)
        {
            if (specialClickActive)
            {
                // __result[0] is tileLocations[0], which is always the "start point"; shift all tiles to match target tile instead
                Vector2 offset = tileLocation - __result[0];
                for (int index = 0; index < __result.Count; index++)
                    __result[index] += offset;
            }
        }
        
        /*******************
         ** Debug Methods **
         *******************/
        
        /// <summary>Prints a message to the SMAPI console.</summary>
        /// <param name="message">The log message.</param>
        public static void Log(string message)
        {
            if (myMonitor != null)
                myMonitor.Log(message, LogLevel.Debug);
        }
    }
}
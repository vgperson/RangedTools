using System;
using System.Reflection;
using Harmony;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Netcode;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace RangedTools
{
    /// <summary>The mod entry point.</summary>
    public class ModEntry : Mod
    {
        private static IMonitor myMonitor;
        private static ModConfig Config;
        
        public static Vector2 specialClickLocation = Vector2.Zero;
        public static SButton heldButton = SButton.None;
        
        public static bool eventUpReset = false;
        public static bool eventUpOld = false;
        
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
                Config = this.Helper.ReadConfig<ModConfig>();
                
                helper.Events.Input.ButtonPressed += this.OnButtonPressed;
                helper.Events.Input.CursorMoved += this.OnCursorMoved;
                
                HarmonyInstance harmonyInstance = HarmonyInstance.Create(this.ModManifest.UniqueID);
                
                patchPrefix(harmonyInstance, typeof(Farmer), nameof(Farmer.useTool),
                            typeof(ModEntry), nameof(ModEntry.Prefix_useTool));
                
                patchPrefix(harmonyInstance, typeof(Utility), nameof(Utility.isWithinTileWithLeeway),
                            typeof(ModEntry), nameof(ModEntry.Prefix_isWithinTileWithLeeway));
                
                if (Config.ToolHitLocationDisplay > 0)
                {
                    patchPrefix(harmonyInstance, typeof(Farmer), nameof(Farmer.draw),
                                typeof(ModEntry), nameof(ModEntry.Prefix_draw),
                                new Type[] { typeof(SpriteBatch) });
                    
                    patchPostfix(harmonyInstance, typeof(Farmer), nameof(Farmer.draw),
                                 typeof(ModEntry), nameof(ModEntry.Postfix_draw),
                                 new Type[] { typeof(SpriteBatch) });
                }
            }
            catch (Exception e)
            {
                Log("Error in mod setup: " + e.Message + Environment.NewLine + e.StackTrace);
            }
        }
        
        /// <summary>Attempts to patch the given source method with the given prefix method.</summary>
        /// <param name="harmonyInstance">The Harmony instance to patch with.</param>
        /// <param name="sourceClass">The class the source method is part of.</param>
        /// <param name="sourceName">The name of the source method.</param>
        /// <param name="patchClass">The class the patch method is part of.</param>
        /// <param name="patchName">The name of the patch method.</param>
        /// <param name="sourceParameters">The source method's parameter list, when needed for disambiguation.</param>
        void patchPrefix(HarmonyInstance harmonyInstance, System.Type sourceClass, string sourceName, System.Type patchClass, string patchName, Type[] sourceParameters = null)
        {
            try
            {
                MethodBase sourceMethod = AccessTools.Method(sourceClass, sourceName, sourceParameters);
                HarmonyMethod prefixPatch = new HarmonyMethod(patchClass, patchName);
                
                if (sourceMethod != null && prefixPatch != null)
                    harmonyInstance.Patch(sourceMethod, prefixPatch);
                else
                {
                    if (sourceMethod == null)
                        Log("Warning: Source method (" + sourceClass.ToString() + "::" + sourceName + ") not found.");
                    if (prefixPatch == null)
                        Log("Warning: Patch method (" + patchClass.ToString() + "::" + patchName + ") not found.");
                }
            }
            catch (Exception e)
            {
                Log("Error in code patching: " + e.Message + Environment.NewLine + e.StackTrace);
            }
        }
        
        /// <summary>Attempts to patch the given source method with the given postfix method.</summary>
        /// <param name="harmonyInstance">The Harmony instance to patch with.</param>
        /// <param name="sourceClass">The class the source method is part of.</param>
        /// <param name="sourceName">The name of the source method.</param>
        /// <param name="patchClass">The class the patch method is part of.</param>
        /// <param name="patchName">The name of the patch method.</param>
        /// <param name="sourceParameters">The source method's parameter list, when needed for disambiguation.</param>
        void patchPostfix(HarmonyInstance harmonyInstance, Type sourceClass, string sourceName, Type patchClass, string patchName, Type[] sourceParameters = null)
        {
            try
            {
                MethodBase sourceMethod = AccessTools.Method(sourceClass, sourceName, sourceParameters);
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
            catch (Exception e)
            {
                Log("Error in code patching: " + e.Message + Environment.NewLine + e.StackTrace);
            }
        }
        
        /******************
         ** Input Method **
         ******************/
        
        /// <summary>Checks whether to set ToolLocation override when Tool Button is pressed.</summary>
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
                    Vector2 mousePosition = Utility.ModifyCoordinatesFromUIScale(e.Cursor.ScreenPixels);
                    mousePosition.X += Game1.viewport.X;
                    mousePosition.Y += Game1.viewport.Y;
                    
                    if (player.CurrentTool != null && !player.UsingTool) // Have a tool selected, not in the middle of using it
                    {
                        // If setting is enabled, face all mouse clicks when a tool/weapon is equipped.
                        if (withClick && shouldToolTurnToFace(player.CurrentTool))
                            player.faceGeneralDirection(mousePosition);
                        
                        if (positionValidForExtendedRange(player, mousePosition))
                        {
                            // Set this as an override location once tool is used.
                            specialClickLocation = mousePosition;
                            heldButton = e.Button;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log("Error in button press: " + ex.Message + Environment.NewLine + ex.StackTrace);
            }
        }
        
        /// <summary>Checks for mouse drag while holding the tool button.</summary>
        /// <param name="sender">The event sender.</param>
        /// <param name="e">The event data.</param>
        private void OnCursorMoved(object sender, CursorMovedEventArgs e)
        {
            if (heldButton == SButton.None)
                return;
            
            if (Helper.Input.IsDown(heldButton)) // Dragging mouse while holding tool button should update click location
            {
                specialClickLocation = Utility.ModifyCoordinatesFromUIScale(e.NewPosition.ScreenPixels);
                specialClickLocation.X += Game1.viewport.X;
                specialClickLocation.Y += Game1.viewport.Y;
            }
            else // Once you let go, don't update position unless you press tool button again
                heldButton = SButton.None;
        }
        
        /// <summary>Checks whether the given Farmer and mouse position are within extended range for the current tool.</summary>
        /// <param name="who">The Farmer using the tool.</param>
        /// <param name="mousePosition">The position of the mouse.</param>
        public static bool positionValidForExtendedRange(Farmer who, Vector2 mousePosition)
        {
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
        
        /// <summary>Returns whether tools can be used currently. Does not check whether player is in the middle of using tool.</summary>
        public static bool isAppropriateTimeToUseTool()
        {
            return !Game1.fadeToBlack
                && !Game1.dialogueUp
                && !Game1.eventUp
                && !Game1.menuUp
                && Game1.currentMinigame == null
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
        public static bool shouldToolTurnToFace(Tool tool)
        {
            return (tool is Axe && Config.ToolAlwaysFaceClick)
                || (tool is Pickaxe && Config.ToolAlwaysFaceClick)
                || (tool is Hoe && Config.ToolAlwaysFaceClick)
                || (tool is WateringCan && Config.ToolAlwaysFaceClick)
                || (tool is MeleeWeapon && Config.WeaponAlwaysFaceClick);
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
       
        /// <summary>Returns "usable on player tile" setting for overridable tools (1 for any others).</summary>
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
        
        /// <summary>Prefix to Farmer.useTool that overrides GetToolLocation with click location.</summary>
        /// <param name="who">The Farmer using the tool.</param>
        public static bool Prefix_useTool(Farmer who)
        {
            try
            {
                if (who.toolOverrideFunction == null)
                {
                    if (who.CurrentTool == null)
                        return true; // Go to original function (where it should just terminate due to tool being null, but still)
                    if (who.toolPower > 0 && !Config.AllowRangedChargeEffects)
                        return true; // Go to original function
                    float stamina = who.stamina;
                    if (who.IsLocalPlayer)
                        who.CurrentTool.DoFunction(who.currentLocation, (int)ModEntry.specialClickLocation.X, (int)ModEntry.specialClickLocation.Y, 1, who);
                    
                    // Usual post-DoFunction checks from original
                    who.lastClick = Vector2.Zero;
                    who.checkForExhaustion(stamina);
                    Game1.toolHold = 0.0f;
                    return false; // Don't do original function anymore
                }
                return true; // Go to original function
            }
            catch (Exception e)
            {
                Log("Error in useTool: " + e.Message + Environment.NewLine + e.StackTrace);
                return true; // Go to original function
            }
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
                bool bigCraftable = (item as StardewValley.Object).bigCraftable;
                
                // Base game relies on short range to prevent placing Crab Pots in unreachable places, so always use default range.
                if (!bigCraftable && item.parentSheetIndex == 710) // Crab Pot
                    return true; // Go to original function
                
                // Though original behavior shows green when placing Tapper as long as highlighted tile is in range,
                // this becomes particularly confusing at longer range settings, so check that there is in fact an empty tree.
                if (bigCraftable && item.parentSheetIndex == 105) // Tapper
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
                
                int range = item.category == StardewValley.Object.SeedsCategory
                         || item.category == StardewValley.Object.fertilizerCategory? Config.SeedRange
                                                                                    : Config.ObjectPlaceRange;
                
                if (range < 0 || Utility.withinRadiusOfPlayer(x, y, range, f))
                {
                    __result = true;
                    return false; // Don't do original function anymore
                }
                return true; // Go to original function
            }
            catch (Exception e)
            {
                Log("Error in isWithinTileWithLeeway: " + e.Message + Environment.NewLine + e.StackTrace);
                return true; // Go to original function
            }
        }
        
        /// <summary>Prefix to Farmer.draw that draws tool hit location at extended ranges.</summary>
        /// <param name="__instance">The instance of the Farmer.</param>
        /// <param name="b">The sprite batch.</param>
        public static bool Prefix_draw(Farmer __instance, SpriteBatch b)
        {
            try
            {
                // Abort cases from original function
                if (__instance.currentLocation == null
                 || (!__instance.currentLocation.Equals(Game1.currentLocation)
                  && !__instance.IsLocalPlayer
                  && !Game1.currentLocation.Name.Equals("Temp")
                  && !__instance.isFakeEventActor)
                 || ((bool)(NetFieldBase<bool, NetBool>)__instance.hidden
                  && (__instance.currentLocation.currentEvent == null || __instance != __instance.currentLocation.currentEvent.farmer)
                  && (!__instance.IsLocalPlayer || Game1.locationRequest == null))
                 || (__instance.viewingLocation.Value != null
                  && __instance.IsLocalPlayer))
                    return true; // Go to original function
                
                // Conditions for drawing tool hit indicator from original function
                if (Game1.activeClickableMenu == null
                 && !Game1.eventUp
                 && (__instance.IsLocalPlayer && __instance.CurrentTool != null)
                 && (Game1.oldKBState.IsKeyDown(Keys.LeftShift) || Game1.options.alwaysShowToolHitLocation)
                 && __instance.CurrentTool.doesShowTileLocationMarker()
                 && (!Game1.options.hideToolHitLocationWhenInMotion || !__instance.isMoving()))
                {
                    Vector2 mousePosition = Utility.PointToVector2(Game1.getMousePosition()) 
                                          + new Vector2((float)Game1.viewport.X, (float)Game1.viewport.Y);
                    Vector2 limitedLocal = Game1.GlobalToLocal(Game1.viewport, Utility.clampToTile(__instance.GetToolLocation(mousePosition)));
                    Vector2 extendedLocal = Game1.GlobalToLocal(Game1.viewport, Utility.clampToTile(mousePosition));
                    if (!limitedLocal.Equals(extendedLocal)) // Just fall back on original if clamped position is the same
                    {
                        if (positionValidForExtendedRange(__instance, mousePosition))
                        {
                            b.Draw(Game1.mouseCursors, extendedLocal,
                                    new Rectangle?(Game1.getSourceRectForStandardTileSheet(Game1.mouseCursors, 29)),
                                    Color.White, 0.0f, Vector2.Zero, 1f, SpriteEffects.None, extendedLocal.Y / 10000f);
                            
                            if (Config.ToolHitLocationDisplay == 1) // 2 shows both, but 1 should hide the default one
                            {
                                eventUpOld = Game1.eventUp;
                                Game1.eventUp = true;
                                eventUpReset = true;
                            }
                        }
                    }
                }
                return true; // Go to original function
            }
            catch (Exception e)
            {
                Log("Error in Farmer draw: " + e.Message + Environment.NewLine + e.StackTrace);
                return true; // Go to original function
            }
        }
        
        /// <summary>Postfix to Farmer.draw that resets eventUp to what it was.</summary>
        public static void Postfix_draw()
        {
            if (eventUpReset)
            {
                Game1.eventUp = eventUpOld;
                eventUpReset = false;
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
namespace RangedTools
{
    class ModConfig
    {
        public int AxeRange { get; set; } = 1;
        public int PickaxeRange { get; set; } = 1;
        public int HoeRange { get; set; } = 1;
        public int WateringCanRange { get; set; } = 1;
        public int ScytheRange { get; set; } = 1;
        public int WeaponRange { get; set; } = 1;
        public int SeedRange { get; set; } = 1;
        public int ObjectPlaceRange { get; set; } = 1;
        
        public bool CenterScytheOnCursor { get; set; } = false;
        public bool CenterWeaponOnCursor { get; set; } = false;
        
        public bool AxeUsableOnPlayerTile { get; set; } = false;
        public bool PickaxeUsableOnPlayerTile { get; set; } = true;
        public bool HoeUsableOnPlayerTile { get; set; } = true;
        
        public bool ToolAlwaysFaceClick { get; set; } = true;
        public bool WeaponAlwaysFaceClick { get; set; } = true;
        
        public int ToolHitLocationDisplay { get; set; } = 1;
        
        public bool UseHalfTilePositions { get; set; } = true;
        public bool AllowRangedChargeEffects { get; set; } = false;
        public bool AttacksIgnoreObstacles { get; set; } = false;
        public bool DontCutGrassPastNormalRange { get; set; } = false;
        public bool MultigrabCrabPotsInRange { get; set; } = false;
        public bool CustomRangeOnClickOnly { get; set; } = true;
    }
}

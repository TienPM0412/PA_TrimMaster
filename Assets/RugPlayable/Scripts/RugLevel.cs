using System;

namespace RugMaster
{
    [Serializable] public sealed class LevelDefinition
    {
        public int index, cols, rows, unitsPerCell = 1, channelCapacity;
        public string name;
        public string[] palette, grid, hints;
        public BasketDefinition[] baskets;
        public RegionDefinition[] ribbons;
        public ClipDefinition[] clips;
    }

    [Serializable] public sealed class BasketDefinition
    {
        public int column, capacity, tied = -1;
        public string color;
        public bool hidden;
    }

    [Serializable] public class RegionDefinition { public float[] rect; }
    [Serializable] public sealed class ClipDefinition : RegionDefinition { public int count; }
}

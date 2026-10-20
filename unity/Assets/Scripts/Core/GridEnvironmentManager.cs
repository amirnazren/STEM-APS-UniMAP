using System;
using System.Collections.Generic;
using UnityEngine;

namespace EcoBotBuilders.Core
{
    /// <summary>
    /// Represents the type of a tile in the grid.
    /// </summary>
    public enum TileType
    {
        Empty = 0,
        Floor = 1,
        Dirt = 2,
        Trash = 3,
        SolarPanel = 4,
        Blocked = 5,
        Goal = 6,
        Soil = 7
    }

    /// <summary>
    /// Cardinal directions used by the bot and grid logic.
    /// </summary>
    public enum Direction
    {
        North = 0,
        East = 1,
        South = 2,
        West = 3
    }

    /// <summary>
    /// Data container for a single grid tile.
    /// </summary>
    [Serializable]
    public class GridTile
    {
        [SerializeField] private Vector2Int coordinates;
        [SerializeField] private TileType tileType = TileType.Empty;
        [SerializeField] private bool isWalkable = true;
        [SerializeField] private bool isDirty;
        [SerializeField] private bool hasSolarPanel;
        [SerializeField] private bool isSunlit;
        [SerializeField, Range(0f, 1f)] private float moistureLevel;

        /// <summary>Gets or sets the tile coordinates in grid space.</summary>
        public Vector2Int Coordinates { get => coordinates; set => coordinates = value; }

        /// <summary>Gets or sets the tile type.</summary>
        public TileType TileType { get => tileType; set => tileType = value; }

        /// <summary>Gets or sets whether the tile can be walked on.</summary>
        public bool IsWalkable { get => isWalkable; set => isWalkable = value; }

        /// <summary>Gets or sets whether the tile is dirty.</summary>
        public bool IsDirty { get => isDirty; set => isDirty = value; }

        /// <summary>Gets or sets whether the tile contains a solar panel.</summary>
        public bool HasSolarPanel { get => hasSolarPanel; set => hasSolarPanel = value; }

        /// <summary>Gets or sets whether the tile is currently sunlit.</summary>
        public bool IsSunlit { get => isSunlit; set => isSunlit = value; }

        /// <summary>Gets or sets the soil moisture (0 = dry, 1 = saturated). Only meaningful for Soil tiles.</summary>
        public float MoistureLevel { get => moistureLevel; set => moistureLevel = Mathf.Clamp01(value); }

        /// <summary>
        /// Creates a tile with the provided configuration.
        /// </summary>
        public GridTile(Vector2Int coordinates, TileType type = TileType.Empty, bool walkable = true)
        {
            this.coordinates = coordinates;
            tileType = type;
            isWalkable = walkable;
            isDirty = type == TileType.Dirt || type == TileType.Trash;
            hasSolarPanel = type == TileType.SolarPanel;
            isSunlit = false;
        }

        /// <summary>
        /// Creates a default tile for serialization and runtime use.
        /// </summary>
        public GridTile()
        {
            coordinates = Vector2Int.zero;
        }
    }

    /// <summary>
    /// Represents the player-controlled eco bot.
    /// </summary>
    [Serializable]
    public class EcoBot
    {
        [SerializeField] private Vector2Int gridPosition;
        [SerializeField] private Direction facingDirection = Direction.North;
        [SerializeField] private int energy = 100;
        [SerializeField] private int cleaningPower = 1;
        [SerializeField] private int tokensEarned;

        /// <summary>Gets or sets the bot grid position.</summary>
        public Vector2Int GridPosition { get => gridPosition; set => gridPosition = value; }

        /// <summary>Gets or sets the bot facing direction.</summary>
        public Direction FacingDirection { get => facingDirection; set => facingDirection = value; }

        /// <summary>Gets or sets the bot energy.</summary>
        public int Energy { get => energy; set => energy = Mathf.Max(0, value); }

        /// <summary>Gets or sets the bot cleaning power.</summary>
        public int CleaningPower { get => cleaningPower; set => cleaningPower = Mathf.Max(1, value); }

        /// <summary>Gets or sets tokens earned by the bot.</summary>
        public int TokensEarned { get => tokensEarned; set => tokensEarned = Mathf.Max(0, value); }
    }

    /// <summary>
    /// Minimal level definition used by the environment manager.
    /// </summary>
    [CreateAssetMenu(menuName = "Eco-Bot Builders/Level Data", fileName = "LevelData")]
    public class LevelData : ScriptableObject
    {
        [Header("Grid")]
        [SerializeField] private int width = 8;
        [SerializeField] private int height = 8;
        [SerializeField] private Vector2Int botStartPosition = Vector2Int.zero;
        [SerializeField] private Direction botStartFacing = Direction.North;

        [Header("Tile Layout")]
        [Tooltip("Optional tile layout; if empty, the level is initialized as a walkable floor grid.")]
        [SerializeField] private TileType[] tileTypes;

        /// <summary>Gets the level width.</summary>
        public int Width => Mathf.Max(1, width);

        /// <summary>Gets the level height.</summary>
        public int Height => Mathf.Max(1, height);

        /// <summary>Gets the bot start position.</summary>
        public Vector2Int BotStartPosition => botStartPosition;

        /// <summary>Gets the bot start facing direction.</summary>
        public Direction BotStartFacing => botStartFacing;

        /// <summary>Gets the serialized tile layout.</summary>
        public TileType[] TileTypes => tileTypes;
    }

    /// <summary>
    /// Manages the game grid, bot interactions, and level loading.
    /// </summary>
    public class GridEnvironmentManager : MonoBehaviour
    {
        [Header("Grid")]
        [SerializeField, Min(1)] private int gridWidth = 8;
        [SerializeField, Min(1)] private int gridHeight = 8;
        [SerializeField, Min(0.1f)] private float tileWorldSpacing = 1f;
        [SerializeField] private Vector3 gridOrigin = Vector3.zero;

        [Header("Isometric")]
        [SerializeField, Min(0.1f)] private float isoTileWidth = 1f;
        [SerializeField, Min(0.1f)] private float isoTileHeight = 0.5f;

        [Header("Level Data")]
        [SerializeField] private LevelData initialLevel;

        [Header("Runtime State")]
        [SerializeField] private EcoBot ecoBot = new EcoBot();
        [SerializeField] private List<GridTile> tiles = new List<GridTile>();

        public event Action<int> OnTokenEarned;
        public event Action<int> OnLevelComplete;

        /// <summary>Gets the current grid width.</summary>
        public int GridWidth => gridWidth;

        /// <summary>Gets the current grid height.</summary>
        public int GridHeight => gridHeight;

        /// <summary>Gets the bot state.</summary>
        public EcoBot Bot => ecoBot;

        /// <summary>Gets a read-only view of the tile map.</summary>
        public IReadOnlyList<GridTile> TileMap => tiles;

        private void Awake()
        {
            InitializeDefaultGrid();

            if (initialLevel != null)
            {
                LoadLevelFromData(initialLevel);
            }
        }

        /// <summary>
        /// Loads a level by identifier. This implementation supports a minimal in-file level registry.
        /// </summary>
        public void LoadLevel(int levelId)
        {
            LevelData level = ResolveLevel(levelId);
            if (level != null)
            {
                LoadLevelFromData(level);
                return;
            }

            InitializeDefaultGrid();
            ecoBot.GridPosition = ClampToGrid(Vector2Int.zero);
            ecoBot.FacingDirection = Direction.North;
            UpdateSunlitTiles();
        }

        /// <summary>
        /// Resizes the grid to <paramref name="width"/> x <paramref name="height"/> walkable floor tiles
        /// and places the bot at <paramref name="botStart"/> facing <paramref name="facing"/>.
        /// Used by LevelManager to apply the gridSize from schemas/level-configs.json.
        /// </summary>
        public void ConfigureGrid(int width, int height, Vector2Int botStart, Direction facing = Direction.North)
        {
            gridWidth = Mathf.Max(1, width);
            gridHeight = Mathf.Max(1, height);
            InitializeDefaultGrid();
            ecoBot.GridPosition = ClampToGrid(botStart);
            ecoBot.FacingDirection = facing;
            ecoBot.TokensEarned = 0;
            UpdateSunlitTiles();
        }

        /// <summary>
        /// Sets the type of a single tile (for example to scatter Trash or SolarPanel tiles for a level).
        /// Returns false when the position is outside the grid.
        /// </summary>
        public bool SetTileType(Vector2Int gridPosition, TileType type)
        {
            GridTile tile = GetTile(gridPosition);
            if (tile == null)
            {
                return false;
            }

            ApplyTileType(tile, type);
            UpdateSunlitTiles();
            return true;
        }

        /// <summary>
        /// Moves the bot one tile forward if possible.
        /// </summary>
        public bool MoveForward()
        {
            Vector2Int target = ecoBot.GridPosition + DirectionToOffset(ecoBot.FacingDirection);
            return TryMoveBotTo(target);
        }

        /// <summary>
        /// Turns the bot left by 90 degrees.
        /// </summary>
        public void TurnLeft()
        {
            ecoBot.FacingDirection = (Direction)(((int)ecoBot.FacingDirection + 3) % 4);
        }

        /// <summary>
        /// Turns the bot right by 90 degrees.
        /// </summary>
        public void TurnRight()
        {
            ecoBot.FacingDirection = (Direction)(((int)ecoBot.FacingDirection + 1) % 4);
        }

        /// <summary>
        /// Cleans the current tile if it is dirty or trash, earning tokens when successful.
        /// </summary>
        public bool CleanCurrentTile()
        {
            GridTile tile = GetTile(ecoBot.GridPosition);
            if (tile == null || !tile.IsDirty)
            {
                return false;
            }

            tile.IsDirty = false;
            tile.TileType = tile.HasSolarPanel ? TileType.SolarPanel : TileType.Floor;
            int earned = Mathf.Max(1, ecoBot.CleaningPower);
            ecoBot.TokensEarned += earned;
            OnTokenEarned?.Invoke(earned);
            CheckLevelCompletion();
            return true;
        }

        /// <summary>
        /// Waters the Soil tile under the bot, raising its moisture by <paramref name="amount"/> (clamped to 1).
        /// Returns false when the bot is not standing on a Soil tile.
        /// </summary>
        public bool WaterCurrentTile(float amount = 0.5f)
        {
            GridTile tile = GetTile(ecoBot.GridPosition);
            if (tile == null || tile.TileType != TileType.Soil)
            {
                return false;
            }

            tile.MoistureLevel += Mathf.Max(0f, amount);
            return true;
        }

        /// <summary>
        /// Places a solar panel on the specified tile if valid.
        /// </summary>
        public bool PlaceSolarPanel(Vector2Int gridPosition)
        {
            GridTile tile = GetTile(gridPosition);
            if (tile == null || !tile.IsWalkable || tile.TileType == TileType.Blocked)
            {
                return false;
            }

            tile.HasSolarPanel = true;
            tile.TileType = TileType.SolarPanel;
            UpdateSunlitTiles();
            return true;
        }

        /// <summary>
        /// Converts a grid position to a world position using isometric projection.
        /// </summary>
        public Vector3 GridToWorld(Vector2Int gridPosition)
        {
            float x = (gridPosition.x - gridPosition.y) * (isoTileWidth * 0.5f);
            float y = (gridPosition.x + gridPosition.y) * (isoTileHeight * 0.5f);
            return gridOrigin + new Vector3(x, y, 0f);
        }

        /// <summary>
        /// Converts a world position to the nearest grid position using isometric projection.
        /// </summary>
        public Vector2Int WorldToGrid(Vector3 worldPosition)
        {
            Vector3 local = worldPosition - gridOrigin;
            float a = isoTileWidth * 0.5f;
            float b = isoTileHeight * 0.5f;
            if (Mathf.Approximately(a, 0f) || Mathf.Approximately(b, 0f))
            {
                return Vector2Int.zero;
            }

            float gridX = ((local.x / a) + (local.y / b)) * 0.5f;
            float gridY = ((local.y / b) - (local.x / a)) * 0.5f;
            return ClampToGrid(new Vector2Int(Mathf.RoundToInt(gridX), Mathf.RoundToInt(gridY)));
        }

        /// <summary>
        /// Returns whether the specified tile is sunlit.
        /// </summary>
        public bool IsTileSunlit(Vector2Int gridPosition)
        {
            GridTile tile = GetTile(gridPosition);
            return tile != null && tile.IsSunlit;
        }

        private void LoadLevelFromData(LevelData level)
        {
            if (level == null)
            {
                return;
            }

            gridWidth = Mathf.Max(1, level.Width);
            gridHeight = Mathf.Max(1, level.Height);
            EnsureTileCapacity();

            TileType[] serializedTiles = level.TileTypes;
            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    int index = ToIndex(x, y);
                    GridTile tile = tiles[index];
                    tile.Coordinates = new Vector2Int(x, y);
                    tile.TileType = TileType.Floor;
                    tile.IsWalkable = true;
                    tile.IsDirty = false;
                    tile.HasSolarPanel = false;
                    tile.IsSunlit = false;

                    if (serializedTiles != null && index < serializedTiles.Length)
                    {
                        ApplyTileType(tile, serializedTiles[index]);
                    }
                }
            }

            ecoBot.GridPosition = ClampToGrid(level.BotStartPosition);
            ecoBot.FacingDirection = level.BotStartFacing;
            UpdateSunlitTiles();
        }

        private void ApplyTileType(GridTile tile, TileType type)
        {
            tile.TileType = type;
            tile.IsWalkable = type != TileType.Blocked;
            tile.IsDirty = type == TileType.Dirt || type == TileType.Trash;
            tile.HasSolarPanel = type == TileType.SolarPanel;
            tile.MoistureLevel = 0f;
        }

        private void InitializeDefaultGrid()
        {
            EnsureTileCapacity();
            for (int y = 0; y < gridHeight; y++)
            {
                for (int x = 0; x < gridWidth; x++)
                {
                    int index = ToIndex(x, y);
                    GridTile tile = tiles[index];
                    tile.Coordinates = new Vector2Int(x, y);
                    tile.TileType = TileType.Floor;
                    tile.IsWalkable = true;
                    tile.IsDirty = false;
                    tile.HasSolarPanel = false;
                    tile.IsSunlit = false;
                    tile.MoistureLevel = 0f;
                }
            }

            ecoBot.GridPosition = ClampToGrid(ecoBot.GridPosition);
            ecoBot.FacingDirection = ecoBot.FacingDirection;
        }

        private void EnsureTileCapacity()
        {
            int required = gridWidth * gridHeight;
            if (tiles == null)
            {
                tiles = new List<GridTile>(required);
            }

            while (tiles.Count < required)
            {
                tiles.Add(new GridTile());
            }

            if (tiles.Count > required)
            {
                tiles.RemoveRange(required, tiles.Count - required);
            }
        }

        private bool TryMoveBotTo(Vector2Int target)
        {
            if (!IsWithinBounds(target))
            {
                return false;
            }

            GridTile tile = GetTile(target);
            if (tile == null || !tile.IsWalkable)
            {
                return false;
            }

            ecoBot.GridPosition = target;
            UpdateSunlitTiles();
            return true;
        }

        private void CheckLevelCompletion()
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                GridTile tile = tiles[i];
                if (tile != null && tile.IsDirty)
                {
                    return;
                }
            }

            OnLevelComplete?.Invoke(ecoBot.TokensEarned);
        }

        private void UpdateSunlitTiles()
        {
            for (int i = 0; i < tiles.Count; i++)
            {
                GridTile tile = tiles[i];
                if (tile != null)
                {
                    tile.IsSunlit = false;
                }
            }

            for (int x = 0; x < gridWidth; x++)
            {
                bool blockedByObstacle = false;
                for (int y = 0; y < gridHeight; y++)
                {
                    GridTile tile = GetTile(new Vector2Int(x, y));
                    if (tile == null)
                    {
                        continue;
                    }

                    if (blockedByObstacle)
                    {
                        tile.IsSunlit = false;
                        continue;
                    }

                    tile.IsSunlit = true;
                    if (tile.TileType == TileType.Blocked)
                    {
                        blockedByObstacle = true;
                    }
                }
            }
        }

        private LevelData ResolveLevel(int levelId)
        {
            if (initialLevel != null && levelId == 0)
            {
                return initialLevel;
            }

            return null;
        }

        private GridTile GetTile(Vector2Int position)
        {
            if (!IsWithinBounds(position))
            {
                return null;
            }

            return tiles[ToIndex(position.x, position.y)];
        }

        private bool IsWithinBounds(Vector2Int position)
        {
            return position.x >= 0 && position.y >= 0 && position.x < gridWidth && position.y < gridHeight;
        }

        private Vector2Int ClampToGrid(Vector2Int position)
        {
            return new Vector2Int(
                Mathf.Clamp(position.x, 0, gridWidth - 1),
                Mathf.Clamp(position.y, 0, gridHeight - 1));
        }

        private int ToIndex(int x, int y)
        {
            return y * gridWidth + x;
        }

        private static Vector2Int DirectionToOffset(Direction direction)
        {
            switch (direction)
            {
                case Direction.North: return new Vector2Int(0, 1);
                case Direction.East: return new Vector2Int(1, 0);
                case Direction.South: return new Vector2Int(0, -1);
                case Direction.West: return new Vector2Int(-1, 0);
                default: return Vector2Int.zero;
            }
        }
    }
}
